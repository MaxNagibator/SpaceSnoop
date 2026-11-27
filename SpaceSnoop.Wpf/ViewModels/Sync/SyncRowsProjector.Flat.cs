namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static partial class SyncRowsProjector
{
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

        if (folders is string[] array)
        {
            var index = GroupedFolderIndex(relativePath, array);
            return index >= 0 ? array[index] : null;
        }

        var copy = folders as IReadOnlyList<string> ?? [.. folders];
        var matched = GroupedFolderIndex(relativePath, copy);
        return matched >= 0 ? copy[matched] : null;
    }

    internal static bool IsGroupedPath(string relativePath, IReadOnlyCollection<string> folders)
    {
        return GroupedKey(relativePath, folders) is not null;
    }

    private static List<SyncNodeViewModel> BuildFlatRows(SyncRowsRequest request, ISyncRowHost host)
    {
        var root = request.Result.Root;
        var search = request.SearchText.Trim();
        var files = new List<FileComparison>();
        CollectVisibleFilesTo(root, request.ShowIdentical, request.HideApplied, request.Outcomes, search, files);

        if (files.Count > 1)
        {
            SortFilesInPlace(files, request.RowSort, request.RowSortDescending);
        }

        var dirs = new List<DirectoryComparison>();
        CollectEmptyDirsTo(root, request.HideApplied, request.Outcomes, search, dirs);

        if (dirs.Count > 1)
        {
            dirs.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath));
        }

        var groupFolders = DeduplicateFolders(ParseGroupFolders(request.GroupFolders));

        if (groupFolders.Length == 0)
        {
            var simple = new List<SyncNodeViewModel>(files.Count + dirs.Count);
            AppendFlatFiles(simple, files, 0, request.Outcomes, host);
            AppendFlatDirs(simple, dirs, 0, request.Outcomes, host);
            return simple;
        }

        var buffer = new List<SyncNodeViewModel>(files.Count + dirs.Count + 1 + groupFolders.Length);
        var subFiles = new List<FileComparison>?[groupFolders.Length];
        var subDirs = new List<DirectoryComparison>?[groupFolders.Length];
        var groupedTotal = 0;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var groupIndex = GroupedFolderIndex(file.RelativePath, groupFolders);

            if (groupIndex < 0)
            {
                buffer.Add(
                    new(file, 0, host, true)
                    {
                        Outcome = request.Outcomes.GetValueOrDefault(file),
                    });
            }
            else
            {
                (subFiles[groupIndex] ??= []).Add(file);
                groupedTotal++;
            }
        }

        for (var i = 0; i < dirs.Count; i++)
        {
            var dir = dirs[i];
            var groupIndex = GroupedFolderIndex(dir.RelativePath, groupFolders);

            if (groupIndex < 0)
            {
                buffer.Add(
                    new(dir, 0, false, 0, 0, host, true)
                    {
                        Outcome = request.Outcomes.GetValueOrDefault(dir),
                    });
            }
            else
            {
                (subDirs[groupIndex] ??= []).Add(dir);
                groupedTotal++;
            }
        }

        if (groupedTotal == 0)
        {
            return buffer;
        }

        buffer.Add(SyncNodeViewModel.CreateGroupHeader(groupedTotal, request.GitGroupExpanded, host));

        if (!request.GitGroupExpanded)
        {
            return buffer;
        }

        for (var i = 0; i < groupFolders.Length; i++)
        {
            var groupFiles = subFiles[i];
            var groupDirs = subDirs[i];
            var count = (groupFiles?.Count ?? 0) + (groupDirs?.Count ?? 0);

            if (count == 0)
            {
                continue;
            }

            var folder = groupFolders[i];
            var expanded = !request.CollapsedSubGroups.Contains(folder);
            buffer.Add(SyncNodeViewModel.CreateSubGroupHeader(folder, count, expanded, host));

            if (expanded)
            {
                if (groupFiles is not null)
                {
                    AppendFlatFiles(buffer, groupFiles, 2, request.Outcomes, host);
                }

                if (groupDirs is not null)
                {
                    AppendFlatDirs(buffer, groupDirs, 2, request.Outcomes, host);
                }
            }
        }

        return buffer;
    }

    private static string[] DeduplicateFolders(string[] folders)
    {
        if (folders.Length <= 1)
        {
            return folders;
        }

        var unique = new List<string>(folders.Length);

        for (var i = 0; i < folders.Length; i++)
        {
            var candidate = folders[i];
            var duplicate = false;

            for (var j = 0; j < unique.Count; j++)
            {
                if (string.Equals(unique[j], candidate, StringComparison.OrdinalIgnoreCase))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
            {
                unique.Add(candidate);
            }
        }

        return [.. unique];
    }

    private static int GroupedFolderIndex(string relativePath, IReadOnlyList<string> folders)
    {
        var span = relativePath.AsSpan();
        var count = folders.Count;

        while (true)
        {
            var separator = span.IndexOfAny('/', '\\');
            var segment = separator >= 0 ? span[..separator] : span;

            for (var i = 0; i < count; i++)
            {
                if (segment.Equals(folders[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            if (separator < 0)
            {
                return -1;
            }

            span = span[(separator + 1)..];
        }
    }

    private static void CollectVisibleFilesTo(
        DirectoryComparison dir,
        bool showIdentical,
        bool hideApplied,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        string search,
        List<FileComparison> target)
    {
        var subs = dir.SubDirectories;

        for (var i = 0; i < subs.Count; i++)
        {
            CollectVisibleFilesTo(subs[i], showIdentical, hideApplied, outcomes, search, target);
        }

        var files = dir.Files;
        var hasSearch = search.Length > 0;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];

            if (!showIdentical && file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            if (hideApplied && outcomes.GetValueOrDefault(file) == SyncOutcome.Applied)
            {
                continue;
            }

            if (hasSearch && !Matches(file.RelativePath, search))
            {
                continue;
            }

            target.Add(file);
        }
    }

    private static void CollectEmptyDirsTo(
        DirectoryComparison dir,
        bool hideApplied,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        string search,
        List<DirectoryComparison> target)
    {
        var subs = dir.SubDirectories;
        var hasSearch = search.Length > 0;

        for (var i = 0; i < subs.Count; i++)
        {
            var sub = subs[i];

            if (sub.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly && SubtreeHasNoFiles(sub))
            {
                if ((!hideApplied || outcomes.GetValueOrDefault(sub) != SyncOutcome.Applied)
                    && (!hasSearch || Matches(sub.RelativePath, search)))
                {
                    target.Add(sub);
                }

                continue;
            }

            CollectEmptyDirsTo(sub, hideApplied, outcomes, search, target);
        }

        static bool SubtreeHasNoFiles(DirectoryComparison node)
        {
            if (node.Files.Count > 0)
            {
                return false;
            }

            var nested = node.SubDirectories;

            for (var i = 0; i < nested.Count; i++)
            {
                if (!SubtreeHasNoFiles(nested[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private static void AppendFlatFiles(
        List<SyncNodeViewModel> buffer,
        List<FileComparison> files,
        int indent,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        ISyncRowHost host)
    {
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            buffer.Add(
                new(file, indent, host, true)
                {
                    Outcome = outcomes.GetValueOrDefault(file),
                });
        }
    }

    private static void AppendFlatDirs(
        List<SyncNodeViewModel> buffer,
        List<DirectoryComparison> dirs,
        int indent,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        ISyncRowHost host)
    {
        for (var i = 0; i < dirs.Count; i++)
        {
            var dir = dirs[i];
            buffer.Add(
                new(dir, indent, false, 0, 0, host, true)
                {
                    Outcome = outcomes.GetValueOrDefault(dir),
                });
        }
    }
}
