using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

public enum StorageKind
{
    None = 0,
    SolidState = 1,
    Rotational = 2,
}

public static class StorageMedia
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceSeekPenaltyProperty = 7;
    private const int PropertyStandardQuery = 0;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;

    private static readonly VolumeProbe SharedProbe = new(Query);

    public static StorageKind Detect(string path)
    {
        return SharedProbe.Detect(path);
    }

    public static int LimitParallelism(string path, int requested)
    {
        return Detect(path) == StorageKind.Rotational ? 1 : requested;
    }

    public static Task<int> LimitParallelismAsync(string path, int requested, TimeSpan timeout)
    {
        return SharedProbe.LimitParallelismAsync(path, requested, timeout);
    }

    private static string? VolumeLetter(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        string? root;

        try
        {
            root = Path.GetPathRoot(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return null;
        }

        if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static StorageKind Query(string letter)
    {
        try
        {
            using var device = CreateFileW(
                $@"\\.\{letter}",
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (device.IsInvalid)
            {
                return StorageKind.None;
            }

            var query = new StoragePropertyQuery
            {
                PropertyId = StorageDeviceSeekPenaltyProperty,
                QueryType = PropertyStandardQuery,
            };

            var descriptor = default(DeviceSeekPenaltyDescriptor);

            var ok = DeviceIoControl(
                device,
                IOCTL_STORAGE_QUERY_PROPERTY,
                ref query,
                Marshal.SizeOf<StoragePropertyQuery>(),
                ref descriptor,
                Marshal.SizeOf<DeviceSeekPenaltyDescriptor>(),
                out _,
                IntPtr.Zero);

            if (!ok)
            {
                return StorageKind.None;
            }

            return descriptor.IncursSeekPenalty ? StorageKind.Rotational : StorageKind.SolidState;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return StorageKind.None;
        }
    }

    internal sealed class VolumeProbe(Func<string, StorageKind> query)
    {
        private static readonly Task<StorageKind> None = Task.FromResult(StorageKind.None);

        private readonly Lock _gate = new();

        // TODO: кэш живёт до конца процесса и не переспрашивает драйвер, поэтому съёмный носитель,
        // подменённый под той же буквой, отвечает типом прежнего. Апгрейд – сброс по событию
        // подключения тома, когда появится сценарий со съёмными дисками.
        private readonly Dictionary<string, Task<StorageKind>> _cache = new(StringComparer.OrdinalIgnoreCase);

        public StorageKind Detect(string path)
        {
            return Start(path).GetAwaiter().GetResult();
        }

        public async Task<int> LimitParallelismAsync(string path, int requested, TimeSpan timeout)
        {
            try
            {
                var kind = await Start(path).WaitAsync(timeout).ConfigureAwait(false);
                return kind == StorageKind.Rotational ? 1 : requested;
            }
            catch (TimeoutException)
            {
                return requested;
            }
        }

        private Task<StorageKind> Start(string path)
        {
            var letter = VolumeLetter(path);

            if (letter is null)
            {
                return None;
            }

            lock (_gate)
            {
                if (!_cache.TryGetValue(letter, out var task))
                {
                    task = Task.Run(() => query(letter));
                    _cache[letter] = task;
                }

                return task;
            }
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        ref StoragePropertyQuery lpInBuffer,
        int nInBufferSize,
        ref DeviceSeekPenaltyDescriptor lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceSeekPenaltyDescriptor
    {
        public uint Version;
        public uint Size;

        [MarshalAs(UnmanagedType.U1)]
        public bool IncursSeekPenalty;
    }
}
