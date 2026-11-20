using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public readonly record struct DriveCapacity(string Name, long TotalBytes, long UsedBytes)
{
    internal static Func<string, (ulong Total, ulong Free)?> FreeSpaceReader { get; set; } = ReadFreeSpace;

    public static DriveCapacity? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (UncPath.IsUnc(path))
        {
            return TryReadUnc(path);
        }

        try
        {
            var drive = new DriveInfo(new DirectoryInfo(path).Root.FullName);

            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return null;
            }

            var name = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return new(name, drive.TotalSize, drive.TotalSize - drive.TotalFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static DriveCapacity? TryReadUnc(string path)
    {
        if (UncPath.IsIncompleteUnc(path))
        {
            return null;
        }

        try
        {
            var current = Path.GetFullPath(path.Trim());
            var reader = FreeSpaceReader;

            while (!string.IsNullOrEmpty(current))
            {
                if (reader(WithSeparator(current)) is { Total: > 0 } space)
                {
                    var total = (long)Math.Min(space.Total, long.MaxValue);
                    var free = Math.Min(space.Free, space.Total);
                    var used = (long)Math.Min(space.Total - free, long.MaxValue);
                    var name = UncPath.TryParseShareRoot(path, out var shareRoot)
                        ? shareRoot
                        : Path.TrimEndingDirectorySeparator(current);

                    return new(name, total, used);
                }

                current = Path.GetDirectoryName(current);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
        {
            return null;
        }
    }

    private static string WithSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static (ulong Total, ulong Free)? ReadFreeSpace(string directoryPath)
    {
        return GetDiskFreeSpaceExW(directoryPath, out _, out var total, out var free) && total > 0
            ? (total, free)
            : null;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
