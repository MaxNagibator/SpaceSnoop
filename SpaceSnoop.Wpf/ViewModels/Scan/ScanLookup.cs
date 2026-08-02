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

        foreach (var root in roots)
        {
            if (Find(root, target) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    public static string Normalize(string path)
    {
        return path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static SpaceBase? Find(SpaceBase space, string target)
    {
        var current = Normalize(space.AbsolutePath);

        if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase))
        {
            return space;
        }

        if (space is not DirectorySpace dir || !IsInside(current, target))
        {
            return null;
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (Find(sub, target) is { } found)
            {
                return found;
            }
        }

        return dir.Files.FirstOrDefault(file => string.Equals(Normalize(file.AbsolutePath), target, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsInside(string directory, string target)
    {
        return target.Length > directory.Length
               && target.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
               && target[directory.Length] is '\\' or '/';
    }
}
