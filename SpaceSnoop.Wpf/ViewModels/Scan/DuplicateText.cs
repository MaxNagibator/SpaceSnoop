using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class DuplicateText
{
    private const string ScanRoot = ".";

    internal static string Name(string path)
    {
        var name = Path.GetFileName(path);

        return name.Length > 0 ? name : path;
    }

    internal static string Directory(string path, string? root)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;

        if (directory.Length == 0)
        {
            return string.Empty;
        }

        if (root is { Length: > 0 } && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            directory = directory[root.Length..]
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return directory.Length == 0 ? ScanRoot : directory;
    }
}
