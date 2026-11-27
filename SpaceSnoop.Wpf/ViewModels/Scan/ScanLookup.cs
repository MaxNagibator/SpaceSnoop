using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanLookup
{
    public static SpaceBase? Find(IEnumerable<SpaceBase> roots, string path)
    {
        var target = Normalize(path);

        if (target.Length == 0)
        {
            return null;
        }

        List<SpaceBase> reachable = [];

        foreach (var root in roots)
        {
            Collect(root, target, reachable);
        }

        return Pick(reachable, target);
    }

    public static string Normalize(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static void Collect(SpaceBase space, string target, List<SpaceBase> reachable)
    {
        var current = Normalize(space.AbsolutePath);

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            reachable.Add(space);
            return;
        }

        if (space is not DirectorySpace dir || !IsInside(current, target))
        {
            return;
        }

        var subs = dir.SubDirectoriesArray;

        for (var i = 0; i < subs.Length; i++)
        {
            Collect(subs[i], target, reachable);
        }

        var remaining = target.AsSpan(current.Length + 1);

        if (remaining.IndexOfAny('\\', '/') >= 0)
        {
            return;
        }

        var files = dir.FileEntries;

        for (var i = 0; i < files.Length; i++)
        {
            if (dir.FileNameEquals(i, remaining, StringComparison.OrdinalIgnoreCase))
            {
                reachable.Add(dir.GetFile(i));
            }
        }
    }

    private static SpaceBase? Pick(List<SpaceBase> reachable, string target)
    {
        SpaceBase? loose = null;
        var ambiguous = false;

        foreach (var space in reachable)
        {
            var current = Normalize(space.AbsolutePath);

            if (string.Equals(current, target, StringComparison.Ordinal))
            {
                return space;
            }

            if (!ScanTreeEditor.SamePath(current, target))
            {
                continue;
            }

            ambiguous |= loose is not null && !string.Equals(Normalize(loose.AbsolutePath), current, StringComparison.Ordinal);
            loose ??= space;
        }

        return ambiguous ? null : loose;
    }

    private static bool IsInside(string directory, string target)
    {
        return target.Length > directory.Length
            && target.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
            && target[directory.Length] is '\\' or '/';
    }
}
