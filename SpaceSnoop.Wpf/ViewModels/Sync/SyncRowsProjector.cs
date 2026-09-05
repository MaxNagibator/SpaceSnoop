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

internal static class SyncRowsProjector
{
    private static readonly char[] PathSeparators = ['/', '\\'];

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

    internal static IEnumerable<FileComparison> CollectVisibleFiles(DirectoryComparison dir, bool showIdentical, bool hideApplied, IReadOnlyDictionary<object, SyncOutcome> outcomes)
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

    internal static string[] ParseGroupFolders(string folders)
    {
        return folders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string? GroupedKey(string relativePath, IReadOnlyCollection<string> folders)
    {
        if (folders.Count == 0)
        {
            return null;
        }

        foreach (var segment in relativePath.Split(PathSeparators))
        {
            foreach (var folder in folders)
            {
                if (string.Equals(segment, folder, StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }
        }

        return null;
    }

    internal static bool IsGroupedPath(string relativePath, IReadOnlyCollection<string> folders)
    {
        return GroupedKey(relativePath, folders) is not null;
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

    private static List<SyncNodeViewModel> BuildFlatRows(SyncRowsRequest request, ISyncRowHost host)
    {
        var root = request.Result.Root;
        var search = request.SearchText.Trim();
        var files = FilterBySearch(CollectVisibleFiles(root, request.ShowIdentical, request.HideApplied, request.Outcomes), search, static file => file.RelativePath);
        var sortedFiles = SortFiles(files, request.RowSort, request.RowSortDescending).ToList();
        var dirs = FilterBySearch(CollectEmptyDirs(root, request.HideApplied, request.Outcomes), search, static dir => dir.RelativePath);
        var sortedDirs = dirs.OrderBy(dir => dir.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        var groupFolders = ParseGroupFolders(request.GroupFolders);
        var buffer = new List<SyncNodeViewModel>();

        AddUngroupedRows(buffer, sortedFiles, sortedDirs, groupFolders, request, host);
        AddGroupedRows(buffer, sortedFiles, sortedDirs, groupFolders, request, host);
        return buffer;
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

    private static IEnumerable<T> FilterBySearch<T>(IEnumerable<T> items, string search, Func<T, string> path)
    {
        return search.Length == 0
            ? items
            : items.Where(item => path(item).Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static void AddUngroupedRows(
        List<SyncNodeViewModel> buffer,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders,
        SyncRowsRequest request,
        ISyncRowHost host)
    {
        foreach (var file in files.Where(file => !IsGroupedPath(file.RelativePath, groupFolders)))
        {
            buffer.Add(new(file, 0, host, true) { Outcome = request.Outcomes.GetValueOrDefault(file) });
        }

        foreach (var dir in dirs.Where(dir => !IsGroupedPath(dir.RelativePath, groupFolders)))
        {
            buffer.Add(new(dir, 0, false, 0, 0, host, true) { Outcome = request.Outcomes.GetValueOrDefault(dir) });
        }
    }

    private static void AddGroupedRows(
        List<SyncNodeViewModel> buffer,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders,
        SyncRowsRequest request,
        ISyncRowHost host)
    {
        var groupedFiles = files.Where(file => IsGroupedPath(file.RelativePath, groupFolders)).ToList();
        var groupedDirs = dirs.Where(dir => IsGroupedPath(dir.RelativePath, groupFolders)).ToList();
        var total = groupedFiles.Count + groupedDirs.Count;

        if (total == 0)
        {
            return;
        }

        buffer.Add(SyncNodeViewModel.CreateGroupHeader(total, request.GitGroupExpanded, host));

        if (!request.GitGroupExpanded)
        {
            return;
        }

        foreach (var folder in groupFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddGroup(buffer, folder, groupedFiles, groupedDirs, groupFolders, request, host);
        }
    }

    private static void AddGroup(
        List<SyncNodeViewModel> buffer,
        string folder,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders,
        SyncRowsRequest request,
        ISyncRowHost host)
    {
        var subFiles = files.Where(file => string.Equals(GroupedKey(file.RelativePath, groupFolders), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var subDirs = dirs.Where(dir => string.Equals(GroupedKey(dir.RelativePath, groupFolders), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var total = subFiles.Count + subDirs.Count;

        if (total == 0)
        {
            return;
        }

        var expanded = !request.CollapsedSubGroups.Contains(folder);
        buffer.Add(SyncNodeViewModel.CreateSubGroupHeader(folder, total, expanded, host));

        if (expanded)
        {
            AddGroupItems(buffer, subFiles, subDirs, request, host);
        }
    }

    private static void AddGroupItems(
        List<SyncNodeViewModel> buffer,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        SyncRowsRequest request,
        ISyncRowHost host)
    {
        foreach (var file in files)
        {
            buffer.Add(new(file, 2, host, true) { Outcome = request.Outcomes.GetValueOrDefault(file) });
        }

        foreach (var dir in dirs)
        {
            buffer.Add(new(dir, 2, false, 0, 0, host, true) { Outcome = request.Outcomes.GetValueOrDefault(dir) });
        }
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
            buffer.Add(new(sub, indent, expanded, sizes?.Left ?? 0, sizes?.Right ?? 0, host) { Outcome = outcome });

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
            buffer.Add(new(file, indent, host) { Outcome = outcome });
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
