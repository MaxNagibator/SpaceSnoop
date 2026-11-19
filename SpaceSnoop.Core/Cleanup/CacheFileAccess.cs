using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Core.Cleanup;

internal static class CacheFileAccess
{
    internal static DirectoryLease LockDirectory(string path)
    {
        return new(path);
    }

    internal static SafeFileHandle OpenFile(string path, bool deleting)
    {
        var handle = CreateFile(
            ExtendedPath(path),
            deleting ? 0x10080u : 0x80u,
            deleting ? 0u : 7u,
            nint.Zero,
            3,
            0x00200000,
            nint.Zero);

        CheckHandle(handle, path);
        return handle;
    }

    internal static SafeFileHandle OpenDirectoryForDelete(string path)
    {
        var handle = CreateFile(ExtendedPath(path), 0x10080, 0, nint.Zero, 3, 0x02200000, nint.Zero);
        CheckHandle(handle, path);
        try
        {
            Read(handle, path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static CacheFileStamp Read(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw Error(path);
        }

        if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"«{path}»: ссылки и reparse-точки не очищаются.");
        }

        var buffer = new StringBuilder(512);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity && length < 32768)
        {
            buffer.EnsureCapacity((int)length + 1);
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, 0);
        }

        if (length == 0 || length >= buffer.Capacity)
        {
            throw Error(path);
        }

        var canonical = buffer.ToString();
        if (!canonical.StartsWith(@"\\?\", StringComparison.Ordinal)
            || !string.Equals(Path.TrimEndingDirectorySeparator(path), canonical[4..], StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"«{path}»: путь является псевдонимом другого каталога или файла.");
        }

        return new(
            path,
            info.Volume,
            ((ulong)info.IndexHigh << 32) | info.IndexLow,
            checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)),
            ((ulong)info.WrittenHigh << 32) | info.WrittenLow,
            ((ulong)info.CreatedHigh << 32) | info.CreatedLow,
            info.Attributes);
    }

    internal static void Delete(SafeFileHandle handle, string path)
    {
        byte delete = 1;
        if (!SetFileInformationByHandle(handle, 4, ref delete, 1))
        {
            throw Error(path);
        }
    }

    internal static void Move(SafeFileHandle handle, string destination)
    {
        var name = Encoding.Unicode.GetBytes(destination + '\0');
        var offset = IntPtr.Size == 8 ? 20 : 12;
        var buffer = Marshal.AllocHGlobal(offset + name.Length);
        try
        {
            Marshal.Copy(new byte[offset + name.Length], 0, buffer, offset + name.Length);
            Marshal.WriteInt32(buffer, offset - 4, name.Length - 2);
            Marshal.Copy(name, 0, buffer + offset, name.Length);
            if (!SetFileInformationByHandle(handle, 3, buffer, (uint)(offset + name.Length)))
            {
                throw Error(destination);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void CheckHandle(SafeFileHandle handle, string path)
    {
        if (!handle.IsInvalid)
        {
            return;
        }

        var error = Error(path);
        handle.Dispose();
        throw error;
    }

    private static IOException Error(string path)
    {
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        return new($"«{path}»: {error.Message} (Win32: {error.NativeErrorCode}).", error);
    }

    private static string ExtendedPath(string path)
    {
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path : @"\\?\" + path;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path,
        uint access,
        uint share,
        nint security,
        uint disposition,
        uint flags,
        nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out NativeFileInfo info);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        ref byte information,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        nint information,
        uint size);

    internal sealed class DirectoryLease : IDisposable
    {
        private readonly List<SafeFileHandle> _handles = [];

        internal CacheFileStamp Stamp { get; }

        internal DirectoryLease(string path)
        {
            try
            {
                var chain = new Stack<string>();
                for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
                {
                    chain.Push(directory.FullName);
                }

                while (chain.TryPop(out var current))
                {
                    var handle = CreateFile(ExtendedPath(current), 0x80, 1, nint.Zero, 3, 0x02200000, nint.Zero);
                    CheckHandle(handle, current);
                    _handles.Add(handle);
                    var stamp = Read(handle, current);
                    if ((stamp.Attributes & (uint)FileAttributes.Directory) == 0)
                    {
                        throw new IOException($"«{current}»: ожидался каталог.");
                    }

                    Stamp = stamp;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            for (var index = _handles.Count - 1; index >= 0; index--)
            {
                _handles[index].Dispose();
            }

            _handles.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileInfo
    {
        internal uint Attributes;
        internal uint CreatedLow;
        internal uint CreatedHigh;
        internal uint AccessedLow;
        internal uint AccessedHigh;
        internal uint WrittenLow;
        internal uint WrittenHigh;
        internal uint Volume;
        internal uint SizeHigh;
        internal uint SizeLow;
        internal uint Links;
        internal uint IndexHigh;
        internal uint IndexLow;
    }
}
