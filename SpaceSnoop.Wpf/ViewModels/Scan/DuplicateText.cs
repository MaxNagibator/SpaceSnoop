namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class DuplicateText
{
    internal const int DirectoryMaxChars = 60;

    private const string Ellipsis = "…";
    private const string ScanRoot = ".";

    internal static string Name(string path)
    {
        var name = System.IO.Path.GetFileName(path);

        return name.Length > 0 ? name : path;
    }

    internal static string Directory(string path, string? root, int maxChars = DirectoryMaxChars)
    {
        var directory = System.IO.Path.GetDirectoryName(path) ?? string.Empty;

        if (directory.Length == 0)
        {
            return string.Empty;
        }

        if (root is { Length: > 0 } && directory.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            directory = directory[root.Length..]
                .TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        }

        if (directory.Length == 0)
        {
            return ScanRoot;
        }

        if (maxChars > 1 && directory.Length > maxChars)
        {
            directory = Ellipsis + directory[^(maxChars - 1)..];
        }

        return directory;
    }
}
