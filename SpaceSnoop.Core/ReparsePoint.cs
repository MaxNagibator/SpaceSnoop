using Microsoft.Win32.SafeHandles;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Core;

internal static class ReparsePoint
{
    private const uint NameSurrogateBit = 0x2000_0000;
    private const uint MountPointTag = 0xA000_0003;
    private const uint FsctlGetReparsePoint = 0x0009_00A8;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint OpenReparsePointFlags = 0x0200_0000 | 0x0020_0000;
    private const int MaximumReparseDataSize = 16 * 1024;
    private const int MountPointHeaderSize = 16;
    private const string VolumePrefix = @"\??\Volume{";
    private const string VolumeSuffix = @"}\";
    private const int ShortPathLimit = 259;
    private const string ExtendedPrefix = @"\\?\";
    private const string UncPrefix = @"\\";

    private static readonly IntPtr InvalidHandle = new(-1);

    public static bool IsLink(string path, bool whenUnknown)
    {
        return IsLink(path) ?? whenUnknown;
    }

    public static bool? IsLink(string path)
    {
        return TryGetTag(path, out var tag) ? (tag & NameSurrogateBit) != 0 : null;
    }

    public static bool IsMountedVolume(string path)
    {
        return TryGetTag(path, out var tag)
            && tag == MountPointTag
            && TryReadMountTarget(path) is { } target
            && IsVolumeTarget(target);
    }

    internal static bool IsVolumeTarget(string substituteName)
    {
        return substituteName.Length > (VolumePrefix.Length + VolumeSuffix.Length)
            && substituteName.StartsWith(VolumePrefix, StringComparison.OrdinalIgnoreCase)
            && substituteName.EndsWith(VolumeSuffix, StringComparison.Ordinal)
            && Guid.TryParse(substituteName.AsSpan(VolumePrefix.Length, substituteName.Length - VolumePrefix.Length - VolumeSuffix.Length), out _);
    }

    internal static string Extended(string path)
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

    private static string? TryReadMountTarget(string path)
    {
        using var handle = CreateFile(Extended(path), 0, FileShareAll, IntPtr.Zero, OpenExisting, OpenReparsePointFlags, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new byte[MaximumReparseDataSize];

        if (!DeviceIoControl(handle, FsctlGetReparsePoint, IntPtr.Zero, 0, buffer, buffer.Length, out var returned, IntPtr.Zero)
            || returned < MountPointHeaderSize)
        {
            return null;
        }

        var offset = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(8));
        var length = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(10));
        var start = MountPointHeaderSize + offset;

        return (start + length) > returned ? null : Encoding.Unicode.GetString(buffer, start, length);
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

    [DllImport("kernel32.dll", EntryPoint = "FindFirstFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFile(string lpFileName, out Win32FindData lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FindClose(IntPtr hFindFile);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

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
