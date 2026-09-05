using System.Runtime.InteropServices;

namespace SpaceSnoop.Core;

internal static class ReparsePoint
{
    private const uint NameSurrogateBit = 0x2000_0000;
    private const int ShortPathLimit = 259;
    private const string ExtendedPrefix = @"\\?\";
    private const string UncPrefix = @"\\";

    private static readonly IntPtr InvalidHandle = new(-1);

    public static bool IsLink(string path, bool whenUnknown)
    {
        return TryGetTag(path, out var tag) ? (tag & NameSurrogateBit) != 0 : whenUnknown;
    }

    private static bool TryGetTag(string path, out uint tag)
    {
        tag = 0;

        var handle = FindFirstFile(Extended(path), out var data);

        if (handle == InvalidHandle)
        {
            return false;
        }

        FindClose(handle);

        if ((data.dwFileAttributes & (uint)FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }

        tag = data.dwReserved0;
        return true;
    }

    private static string Extended(string path)
    {
        if (path.Length <= ShortPathLimit
            || path.StartsWith(ExtendedPrefix, StringComparison.Ordinal)
            || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        return path.StartsWith(UncPrefix, StringComparison.Ordinal)
            ? ExtendedPrefix + "UNC" + path[1..]
            : ExtendedPrefix + path;
    }

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFile(string lpFileName, out Win32FindData lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public uint dwFileAttributes;
        public FileTime ftCreationTime;
        public FileTime ftLastAccessTime;
        public FileTime ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }
}
