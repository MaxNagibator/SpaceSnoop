using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public static class VolumeSpace
{
    public static long? TryReadFree(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var probe = Task.Run(() => Read(path));

        return probe.Wait(TimeSpan.FromMilliseconds(AppDefaults.VolumeSpaceTimeoutMs)) ? probe.Result : null;
    }

    private static long? Read(string path)
    {
        try
        {
            var current = Path.GetFullPath(path);

            while (!string.IsNullOrEmpty(current))
            {
                if (GetDiskFreeSpaceEx(WithSeparator(current), out var free, out _, out _))
                {
                    return (long)Math.Min(free, long.MaxValue);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(
        string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller,
        out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);
}
