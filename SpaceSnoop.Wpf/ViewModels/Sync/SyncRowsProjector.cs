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
        var initialCapacity = Math.Max(16, root.SubDirectories.Count + root.Files.Count);
        var buffer = new List<SyncNodeViewModel>(initialCapacity);
        var search = request.SearchText.Trim();
        HashSet<object>? searchHits = null;

        if (search.Length > 0)
        {
            searchHits = [];
            CollectSearchHits(root, search, searchHits);
        }

        FlattenDirectory(root, 0, buffer, request, host, searchHits, search);
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
        HashSet<object>? searchHits,
        string search)
    {
        AddVisibleDirectories(dir, indent, buffer, request, host, searchHits, search);
        AddVisibleFiles(dir, indent, buffer, request, host, searchHits, search);
    }

    private static void AddVisibleDirectories(
        DirectoryComparison dir,
        int indent,
        List<SyncNodeViewModel> buffer,
        SyncRowsRequest request,
        ISyncRowHost host,
        HashSet<object>? searchHits,
        string search)
    {
        var subs = dir.SubDirectories;

        if (subs.Count == 0)
        {
            return;
        }

        var visible = new List<DirectoryComparison>(subs.Count);

        for (var i = 0; i < subs.Count; i++)
        {
            var sub = subs[i];

            if (IsVisible(sub, request) && (searchHits is null || searchHits.Contains(sub)))
            {
                visible.Add(sub);
            }
        }

        if (visible.Count == 0)
        {
            return;
        }

        if (visible.Count > 1)
        {
            SortDirectoriesInPlace(visible, request);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var sub = visible[i];
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
                FlattenDirectory(sub, indent + 1, buffer, request, host, searchHits, search);
            }
        }
    }

    private static void AddVisibleFiles(
        DirectoryComparison dir,
        int indent,
        List<SyncNodeViewModel> buffer,
        SyncRowsRequest request,
        ISyncRowHost host,
        HashSet<object>? searchHits,
        string search)
    {
        var files = dir.Files;

        if (files.Count == 0)
        {
            return;
        }

        var visible = new List<FileComparison>(files.Count);
        var hasSearch = searchHits is not null;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];

            if (IsVisible(file, request) && (!hasSearch || Matches(file.RelativePath, search)))
            {
                visible.Add(file);
            }
        }

        if (visible.Count == 0)
        {
            return;
        }

        if (visible.Count > 1)
        {
            SortFilesInPlace(visible, request.RowSort, request.RowSortDescending);
        }

        for (var i = 0; i < visible.Count; i++)
        {
            var file = visible[i];
            var outcome = request.Outcomes.GetValueOrDefault(file);
            buffer.Add(
                new(file, indent, host)
                {
                    Outcome = outcome,
                });
        }
    }

    private static readonly Comparison<DirectoryComparison> DirByNameAsc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    private static readonly Comparison<DirectoryComparison> DirByNameDesc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(right.Name, left.Name);
    private static readonly Comparison<DirectoryComparison> DirByStatusAsc = static (left, right) =>
    {
        var cmp = ((int)left.Status).CompareTo((int)right.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    };
    private static readonly Comparison<DirectoryComparison> DirByStatusDesc = static (left, right) =>
    {
        var cmp = ((int)right.Status).CompareTo((int)left.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
    };

    private static readonly Comparison<FileComparison> FileByPathAsc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    private static readonly Comparison<FileComparison> FileByPathDesc = static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(right.RelativePath, left.RelativePath);
    private static readonly Comparison<FileComparison> FileBySizeAsc = static (left, right) => FileSize(left).CompareTo(FileSize(right));
    private static readonly Comparison<FileComparison> FileBySizeDesc = static (left, right) => FileSize(right).CompareTo(FileSize(left));
    private static readonly Comparison<FileComparison> FileByStatusAsc = static (left, right) =>
    {
        var cmp = ((int)left.Status).CompareTo((int)right.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByStatusDesc = static (left, right) =>
    {
        var cmp = ((int)right.Status).CompareTo((int)left.Status);
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByModifiedAsc = static (left, right) =>
    {
        var cmp = FileModified(left).CompareTo(FileModified(right));
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };
    private static readonly Comparison<FileComparison> FileByModifiedDesc = static (left, right) =>
    {
        var cmp = FileModified(right).CompareTo(FileModified(left));
        return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
    };

    private static long FileSize(FileComparison file)
    {
        return Math.Max(file.LeftSize ?? 0, file.RightSize ?? 0);
    }

    private static DateTime FileModified(FileComparison file)
    {
        var left = file.LeftModified ?? DateTime.MinValue;
        var right = file.RightModified ?? DateTime.MinValue;

        return left > right ? left : right;
    }

    private static void SortDirectoriesInPlace(List<DirectoryComparison> dirs, SyncRowsRequest request)
    {
        var descending = request.RowSortDescending;

        switch (request.RowSort)
        {
            case SyncSortField.Size:
                var cache = request.DirSizeCache;
                dirs.Sort((left, right) =>
                {
                    var leftSizes = cache?.GetValueOrDefault(left);
                    var rightSizes = cache?.GetValueOrDefault(right);
                    var leftSize = leftSizes is null ? 0L : Math.Max(leftSizes.Value.Left, leftSizes.Value.Right);
                    var rightSize = rightSizes is null ? 0L : Math.Max(rightSizes.Value.Left, rightSizes.Value.Right);
                    var cmp = leftSize.CompareTo(rightSize);
                    return descending ? -cmp : cmp;
                });
                break;

            case SyncSortField.Status:
                dirs.Sort(descending ? DirByStatusDesc : DirByStatusAsc);
                break;

            default:
                dirs.Sort(descending ? DirByNameDesc : DirByNameAsc);
                break;
        }
    }

    private static void SortFilesInPlace(List<FileComparison> files, SyncSortField field, bool descending)
    {
        var comparison = field switch
        {
            SyncSortField.Size => descending ? FileBySizeDesc : FileBySizeAsc,
            SyncSortField.Status => descending ? FileByStatusDesc : FileByStatusAsc,
            SyncSortField.Modified => descending ? FileByModifiedDesc : FileByModifiedAsc,
            _ => descending ? FileByPathDesc : FileByPathAsc,
        };

        files.Sort(comparison);
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
