using System.IO;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap;

public readonly record struct DriveCapacity(string Name, long TotalBytes, long UsedBytes)
{
    public static DriveCapacity? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var drive = new DriveInfo(new DirectoryInfo(path).Root.FullName);

            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return null;
            }

            var name = drive.Name.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);

            return new(name, drive.TotalSize, drive.TotalSize - drive.TotalFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or SecurityException or NotSupportedException)
        {
            return null;
        }
    }
}
