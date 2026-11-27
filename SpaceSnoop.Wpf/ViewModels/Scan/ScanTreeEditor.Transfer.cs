namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static partial class ScanTreeEditor
{
    internal static (List<SpaceBase> Transferred, int Lost) TransferMarks(
        IEnumerable<ScanNodeViewModel> previousRoots,
        DirectorySpace fresh,
        bool marksPresent,
        int usnLostMarks = 0)
    {
        var transferred = new List<SpaceBase>();
        var lost = 0;

        if (!marksPresent)
        {
            return (transferred, lost);
        }

        TransferLookups? lookups = null;
        var collectedFresh = false;

        foreach (var root in previousRoots)
        {
            if (root.Space is not DirectorySpace previous)
            {
                continue;
            }

            if (ReferenceEquals(previous, fresh))
            {
                if (!collectedFresh)
                {
                    collectedFresh = true;
                    lost += usnLostMarks;

                    if (fresh.IsDeleted)
                    {
                        transferred.Add(fresh);
                    }
                    else
                    {
                        CollectMarked(fresh, transferred);
                    }
                }

                continue;
            }

            lookups ??= new();
            var marked = new List<SpaceBase>();

            if (previous.IsDeleted)
            {
                marked.Add(previous);
            }
            else
            {
                CollectMarked(previous, marked);
            }

            foreach (var item in marked)
            {
                switch (FindCounterpart(previous, item, fresh, lookups))
                {
                    case null:
                        lost++;
                        break;

                    case { IsDeleted: false } counterpart:
                        counterpart.Delete();

                        if (counterpart.IsDeleted)
                        {
                            transferred.Add(counterpart);
                        }

                        break;
                }
            }
        }

        transferred.RemoveAll(HasMarkedAncestor);

        return (transferred, lost);
    }

    private static bool HasMarkedAncestor(SpaceBase space)
    {
        for (var parent = space.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.IsDeleted)
            {
                return true;
            }
        }

        return false;
    }

    private static SpaceBase? FindCounterpart(
        DirectorySpace previousRoot,
        SpaceBase item,
        DirectorySpace fresh,
        TransferLookups lookups)
    {
        var steps = new Stack<SpaceBase>();

        for (var current = item; current is not null && !ReferenceEquals(current, previousRoot); current = current.Parent)
        {
            steps.Push(current);
        }

        SpaceBase match = fresh;

        while (steps.TryPop(out var step))
        {
            if (match is not DirectorySpace dir || lookups.FindChild(dir, step) is not { } child)
            {
                return null;
            }

            match = child;
        }

        return match;
    }

    private sealed class TransferLookups
    {
        private readonly HashSet<DirectorySpace> _seenDirs = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<DirectorySpace> _seenFiles = new(ReferenceEqualityComparer.Instance);
        private Dictionary<DirectorySpace, ILookup<string, DirectorySpace>>? _dirLookups;
        private Dictionary<DirectorySpace, ILookup<string, int>>? _fileLookups;

        public SpaceBase? FindChild(DirectorySpace dir, SpaceBase step)
        {
            return step is DirectorySpace
                ? FindSubDirectory(dir, step.Name)
                : FindFile(dir, step.Name);
        }

        private DirectorySpace? FindSubDirectory(DirectorySpace dir, string stepName)
        {
            var subs = dir.SubDirectoriesArray;

            if (subs.Length == 0)
            {
                return null;
            }

            if (subs.Length > 16 && !_seenDirs.Add(dir))
            {
                return FindSubDirectoryIndexed(dir, stepName);
            }

            DirectorySpace? firstInsensitive = null;

            for (var i = 0; i < subs.Length; i++)
            {
                var sub = subs[i];

                if (sub.NameEquals(stepName, StringComparison.Ordinal))
                {
                    return sub;
                }

                if (firstInsensitive is null && sub.NameEquals(stepName, StringComparison.OrdinalIgnoreCase))
                {
                    firstInsensitive = sub;
                }
            }

            return firstInsensitive is not null && !PathCase.IsCaseSensitive(dir.AbsolutePath)
                ? firstInsensitive
                : null;
        }

        private DirectorySpace? FindSubDirectoryIndexed(DirectorySpace dir, string stepName)
        {
            _dirLookups ??= new(ReferenceEqualityComparer.Instance);

            if (!_dirLookups.TryGetValue(dir, out var lookup))
            {
                lookup = dir.SubDirectoriesArray.ToLookup(static sub => sub.Name, StringComparer.OrdinalIgnoreCase);
                _dirLookups[dir] = lookup;
            }

            DirectorySpace? firstInsensitive = null;

            foreach (var sub in lookup[stepName])
            {
                if (sub.NameEquals(stepName, StringComparison.Ordinal))
                {
                    return sub;
                }

                firstInsensitive ??= sub;
            }

            return firstInsensitive is not null && !PathCase.IsCaseSensitive(dir.AbsolutePath)
                ? firstInsensitive
                : null;
        }

        private FileSpace? FindFile(DirectorySpace dir, string stepName)
        {
            var files = dir.FileEntries;

            if (files.Length == 0)
            {
                return null;
            }

            if (files.Length > 16 && !_seenFiles.Add(dir))
            {
                return FindFileIndexed(dir, stepName);
            }

            var firstInsensitive = -1;

            for (var i = 0; i < files.Length; i++)
            {
                if (dir.FileNameEquals(i, stepName, StringComparison.Ordinal))
                {
                    return dir.GetFile(i);
                }

                if (firstInsensitive < 0 && dir.FileNameEquals(i, stepName, StringComparison.OrdinalIgnoreCase))
                {
                    firstInsensitive = i;
                }
            }

            return firstInsensitive >= 0 && !PathCase.IsCaseSensitive(dir.AbsolutePath)
                ? dir.GetFile(firstInsensitive)
                : null;
        }

        private FileSpace? FindFileIndexed(DirectorySpace dir, string stepName)
        {
            _fileLookups ??= new(ReferenceEqualityComparer.Instance);

            if (!_fileLookups.TryGetValue(dir, out var lookup))
            {
                lookup = Enumerable.Range(0, dir.FileCount).ToLookup(dir.GetFileName, StringComparer.OrdinalIgnoreCase);
                _fileLookups[dir] = lookup;
            }

            var firstInsensitive = -1;

            foreach (var index in lookup[stepName])
            {
                if (dir.FileNameEquals(index, stepName, StringComparison.Ordinal))
                {
                    return dir.GetFile(index);
                }

                if (firstInsensitive < 0)
                {
                    firstInsensitive = index;
                }
            }

            return firstInsensitive >= 0 && !PathCase.IsCaseSensitive(dir.AbsolutePath)
                ? dir.GetFile(firstInsensitive)
                : null;
        }
    }
}
