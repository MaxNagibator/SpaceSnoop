namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static partial class SyncRowsProjector
{
    private static readonly char[] PathSeparators = ['/', '\\'];

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
            buffer.Add(
                new(file, 0, host, true)
                {
                    Outcome = request.Outcomes.GetValueOrDefault(file),
                });
        }

        foreach (var dir in dirs.Where(dir => !IsGroupedPath(dir.RelativePath, groupFolders)))
        {
            buffer.Add(
                new(dir, 0, false, 0, 0, host, true)
                {
                    Outcome = request.Outcomes.GetValueOrDefault(dir),
                });
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
            buffer.Add(
                new(file, 2, host, true)
                {
                    Outcome = request.Outcomes.GetValueOrDefault(file),
                });
        }

        foreach (var dir in dirs)
        {
            buffer.Add(
                new(dir, 2, false, 0, 0, host, true)
                {
                    Outcome = request.Outcomes.GetValueOrDefault(dir),
                });
        }
    }

}
