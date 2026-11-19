namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal sealed record SyncRowsRequest
{
    public required ComparisonResult Result { get; init; }

    public bool FlatView { get; init; }

    public string SearchText { get; init; } = string.Empty;

    public bool ShowIdentical { get; init; }

    public bool HideApplied { get; init; }

    public SyncSortField RowSort { get; init; }

    public bool RowSortDescending { get; init; }

    public Dictionary<object, SyncOutcome> Outcomes { get; init; } = [];

    public Dictionary<DirectoryComparison, (long Left, long Right)>? DirSizeCache { get; init; }

    public HashSet<DirectoryComparison> Collapsed { get; init; } = [];

    public HashSet<string> CollapsedSubGroups { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public bool GitGroupExpanded { get; init; }

    public string GroupFolders { get; init; } = string.Empty;
}

internal static partial class SyncRowsProjector
{
    public static List<SyncNodeViewModel> Build(SyncRowsRequest request, ISyncRowHost host)
    {
        return request.FlatView
            ? BuildFlatRows(request, host)
            : BuildTreeRows(request, host);
    }

    internal static IEnumerable<FileComparison> SortFiles(IEnumerable<FileComparison> files, SyncSortField field, bool descending)
    {
        return field switch
        {
            SyncSortField.Size => descending
                ? files.OrderByDescending(FileSize)
                : files.OrderBy(FileSize),
            SyncSortField.Status => descending
                ? files.OrderByDescending(file => (int)file.Status).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(file => (int)file.Status).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase),
            SyncSortField.Modified => descending
                ? files.OrderByDescending(FileModified).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(FileModified).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? files.OrderByDescending(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase),
        };

        static long FileSize(FileComparison file)
        {
            return Math.Max(file.LeftSize ?? 0, file.RightSize ?? 0);
        }

        static DateTime FileModified(FileComparison file)
        {
            var left = file.LeftModified ?? DateTime.MinValue;
            var right = file.RightModified ?? DateTime.MinValue;

            return left > right ? left : right;
        }
    }

    internal static IEnumerable<DirectoryComparison> CollectEmptyDirs(DirectoryComparison dir, bool hideApplied, IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly && SubtreeHasNoFiles(sub))
            {
                if (!hideApplied || outcomes.GetValueOrDefault(sub) != SyncOutcome.Applied)
                {
                    yield return sub;
                }

                continue;
            }

            foreach (var nested in CollectEmptyDirs(sub, hideApplied, outcomes))
            {
                yield return nested;
            }
        }

        static bool SubtreeHasNoFiles(DirectoryComparison node)
        {
            return node.Files.Count == 0 && node.SubDirectories.All(SubtreeHasNoFiles);
        }
    }

    internal static IEnumerable<FileComparison> CollectVisibleFiles(
        DirectoryComparison dir,
        bool showIdentical,
        bool hideApplied,
        IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        foreach (var sub in dir.SubDirectories)
        {
            foreach (var file in CollectVisibleFiles(sub, showIdentical, hideApplied, outcomes))
            {
                yield return file;
            }
        }

        foreach (var file in dir.Files)
        {
            if (!showIdentical && file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            if (hideApplied && outcomes.GetValueOrDefault(file) == SyncOutcome.Applied)
            {
                continue;
            }

            yield return file;
        }
    }

    internal static bool CollectSearchHits(DirectoryComparison dir, string search, HashSet<object> hits)
    {
        var matched = false;

        foreach (var sub in dir.SubDirectories)
        {
            matched |= CollectSearchHits(sub, search, hits);
        }

        matched |= dir.Files.Any(file => Matches(file.RelativePath, search));

        if (matched || Matches(dir.RelativePath, search))
        {
            hits.Add(dir);
            matched = true;
        }

        return matched;
    }

    internal static Dictionary<DirectoryComparison, (long Left, long Right)> BuildDirSizeCache(DirectoryComparison root)
    {
        var cache = new Dictionary<DirectoryComparison, (long Left, long Right)>();
        Accumulate(root, cache);
        return cache;
    }

    internal static void CollapseAllDirectories(HashSet<DirectoryComparison> collapsed, DirectoryComparison root)
    {
        collapsed.Clear();
        AddCollapsedChildren(collapsed, root);
    }

    internal static void AddCollapsed(HashSet<DirectoryComparison> collapsed, DirectoryComparison dir)
    {
        collapsed.Add(dir);
        AddCollapsedChildren(collapsed, dir);
    }

    internal static void RemoveCollapsed(HashSet<DirectoryComparison> collapsed, DirectoryComparison dir)
    {
        collapsed.Remove(dir);

        foreach (var sub in dir.SubDirectories)
        {
            RemoveCollapsed(collapsed, sub);
        }
    }

    private static void AddCollapsedChildren(HashSet<DirectoryComparison> collapsed, DirectoryComparison dir)
    {
        foreach (var sub in dir.SubDirectories)
        {
            AddCollapsed(collapsed, sub);
        }
    }

    private static (long Left, long Right) Accumulate(DirectoryComparison dir, Dictionary<DirectoryComparison, (long Left, long Right)> cache)
    {
        long left = 0;
        long right = 0;

        foreach (var file in dir.Files)
        {
            if (file.LeftSize.HasValue)
            {
                left += file.LeftSize.Value;
            }

            if (file.RightSize.HasValue)
            {
                right += file.RightSize.Value;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            var (subLeft, subRight) = Accumulate(sub, cache);
            left += subLeft;
            right += subRight;
        }

        cache[dir] = (left, right);
        return (left, right);
    }

    private static List<SyncNodeViewModel> BuildTreeRows(SyncRowsRequest request, ISyncRowHost host)
    {
        var root = request.Result.Root;
        var buffer = new List<SyncNodeViewModel>();
        var search = request.SearchText.Trim();
        HashSet<object>? searchHits = null;

        if (search.Length > 0)
        {
            searchHits = [];
            CollectSearchHits(root, search, searchHits);
        }

        FlattenDirectory(root, 0, buffer, request, host, searchHits);
        return buffer;
    }

    private static bool Matches(string path, string search)
    {
        return path.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static void FlattenDirectory(
        DirectoryComparison dir,
        int indent,
        List<SyncNodeViewModel> buffer,
        SyncRowsRequest request,
        ISyncRowHost host,
        HashSet<object>? searchHits)
    {
        AddVisibleDirectories(dir, indent, buffer, request, host, searchHits);
        AddVisibleFiles(dir, indent, buffer, request, host, searchHits);
    }

    private static void AddVisibleDirectories(
        DirectoryComparison dir,
        int indent,
        List<SyncNodeViewModel> buffer,
        SyncRowsRequest request,
        ISyncRowHost host,
        HashSet<object>? searchHits)
    {
        foreach (var sub in SortDirectories(dir.SubDirectories, request))
        {
            if (!IsVisible(sub, request) || searchHits?.Contains(sub) == false)
            {
                continue;
            }

            var outcome = request.Outcomes.GetValueOrDefault(sub);
            var expanded = searchHits is not null || !request.Collapsed.Contains(sub);
            var sizes = request.DirSizeCache?.GetValueOrDefault(sub);
            buffer.Add(
                new(sub, indent, expanded, sizes?.Left ?? 0, sizes?.Right ?? 0, host)
                {
                    Outcome = outcome,
                });

            if (expanded)
            {
                FlattenDirectory(sub, indent + 1, buffer, request, host, searchHits);
            }
        }
    }

    private static void AddVisibleFiles(
        DirectoryComparison dir,
        int indent,
        List<SyncNodeViewModel> buffer,
        SyncRowsRequest request,
        ISyncRowHost host,
        HashSet<object>? searchHits)
    {
        var search = searchHits is null ? null : request.SearchText.Trim();

        foreach (var file in SortFiles(dir.Files, request.RowSort, request.RowSortDescending))
        {
            if (!IsVisible(file, request) || (search is not null && !Matches(file.RelativePath, search)))
            {
                continue;
            }

            var outcome = request.Outcomes.GetValueOrDefault(file);
            buffer.Add(
                new(file, indent, host)
                {
                    Outcome = outcome,
                });
        }
    }

    private static IEnumerable<DirectoryComparison> SortDirectories(IEnumerable<DirectoryComparison> dirs, SyncRowsRequest request)
    {
        return request.RowSort switch
        {
            SyncSortField.Size => request.RowSortDescending
                ? dirs.OrderByDescending(DirSize)
                : dirs.OrderBy(DirSize),
            SyncSortField.Status => request.RowSortDescending
                ? dirs.OrderByDescending(sub => (int)sub.Status).ThenBy(sub => sub.Name, StringComparer.OrdinalIgnoreCase)
                : dirs.OrderBy(sub => (int)sub.Status).ThenBy(sub => sub.Name, StringComparer.OrdinalIgnoreCase),
            _ => request.RowSortDescending
                ? dirs.OrderByDescending(sub => sub.Name, StringComparer.OrdinalIgnoreCase)
                : dirs.OrderBy(sub => sub.Name, StringComparer.OrdinalIgnoreCase),
        };

        long DirSize(DirectoryComparison sub)
        {
            var sizes = request.DirSizeCache?.GetValueOrDefault(sub);

            return sizes is null ? 0 : Math.Max(sizes.Value.Left, sizes.Value.Right);
        }
    }

    private static bool IsVisible(DirectoryComparison dir, SyncRowsRequest request)
    {
        return (request.ShowIdentical || dir.Status != ComparisonStatus.Identical)
            && (!request.HideApplied || request.Outcomes.GetValueOrDefault(dir) != SyncOutcome.Applied);
    }

    private static bool IsVisible(FileComparison file, SyncRowsRequest request)
    {
        return (request.ShowIdentical || file.Status != ComparisonStatus.Identical)
            && (!request.HideApplied || request.Outcomes.GetValueOrDefault(file) != SyncOutcome.Applied);
    }
}
