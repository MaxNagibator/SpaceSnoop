using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Duplicates;

public readonly record struct FileIdentity(ulong VolumeSerialNumber, ulong FileIdLow, ulong FileIdHigh)
{
    private const int FileIdInfoClass = 18;

    public static FileIdentity? Read(SafeFileHandle handle)
    {
        return GetFileInformationByHandleEx(handle, FileIdInfoClass, out var info, Marshal.SizeOf<FileIdInfoData>())
            ? new(info.VolumeSerialNumber, info.FileIdLow, info.FileIdHigh)
            : null;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass, out FileIdInfoData info, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfoData
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }
}
