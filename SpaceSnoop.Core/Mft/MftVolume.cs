using Microsoft.Win32.SafeHandles;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal interface IMftVolume : IDisposable
{
    void ReadAt(long offset, Span<byte> buffer);

    void SetSectorSize(int bytesPerSector)
    {
    }

    ValueTask ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();
        ReadAt(offset, buffer.Span);
        return ValueTask.CompletedTask;
    }
}

internal sealed class MftFileVolume : IMftVolume
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint NoBuffering = 0x20000000;
    private const uint Overlapped = 0x40000000;
    private const uint FsctlMarkHandle = 0x000900FCu;
    private const uint FsctlGetNtfsVolumeData = 0x00090064u;
    private const uint MarkHandleSuppressVolumeOpenFlush = 0x00008000u;
    private const int Alignment = 4096;
    private const int MinSectorBytes = 512;
    private const int MaxPooledBounces = 8;
    private const int ErrorAccessDenied = 5;
    private const int ErrorIoPending = 997;
    private const int InvalidParameterResult = unchecked((int)0x80070057);

    private readonly Lock _gate = new();
    private readonly Lock _bounceGate = new();
    private readonly Stack<byte[]> _bouncePool = new();
    private readonly string _path;
    private readonly string _name;
    private readonly SafeFileHandle _unbuffered;
    private readonly bool _suppressFlush;

    private SafeFileHandle? _buffered;
    private volatile int _sectorAlignment = Alignment;

    private MftFileVolume(SafeFileHandle unbuffered, string path, string name, bool suppressFlush = false)
    {
        _unbuffered = unbuffered;
        _path = path;
        _name = name;
        _suppressFlush = suppressFlush;
    }

    internal bool IsBuffered => Volatile.Read(ref _buffered) is not null;

    public static MftFileVolume Open(char letter)
    {
        return Open($@"\\.\{letter}:", $"{letter}:");
    }

    public void SetSectorSize(int bytesPerSector)
    {
        if (bytesPerSector is >= MinSectorBytes and <= Alignment && BitOperations.IsPow2(bytesPerSector))
        {
            _sectorAlignment = bytesPerSector;
        }
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        var buffered = Volatile.Read(ref _buffered);

        if (buffered is not null)
        {
            Read(buffered, offset, buffer, buffer.Length);
            return;
        }

        var alignment = _sectorAlignment;

        if (offset % alignment == 0 && buffer.Length % alignment == 0 && TryReadDirect(offset, buffer))
        {
            return;
        }

        if (!TryReadAligned(offset, buffer))
        {
            Read(Downgrade(), offset, buffer, buffer.Length);
        }
    }

    public async ValueTask ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        var buffered = Volatile.Read(ref _buffered);

        if (buffered is not null)
        {
            await ReadAsync(buffered, offset, buffer, buffer.Length, cancel).ConfigureAwait(false);
            return;
        }

        var alignment = _sectorAlignment;

        if (offset % alignment == 0 && buffer.Length % alignment == 0 && await TryReadDirectAsync(offset, buffer, cancel).ConfigureAwait(false))
        {
            return;
        }

        if (!await TryReadAlignedAsync(offset, buffer, cancel).ConfigureAwait(false))
        {
            await ReadAsync(Downgrade(), offset, buffer, buffer.Length, cancel).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _unbuffered.Dispose();
        _buffered?.Dispose();

        lock (_bounceGate)
        {
            _bouncePool.Clear();
        }
    }

    internal static MftFileVolume Open(string path, string name)
    {
        var handle = OpenHandle(path, NoBuffering | Overlapped);

        if (!handle.IsInvalid)
        {
            return new(handle, path, name);
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        throw error == ErrorAccessDenied
            ? new UnauthorizedAccessException($"Чтение $MFT тома {name} требует прав администратора")
            : new IOException($"Не удалось открыть том {name}, код {error}");
    }

    internal bool TryQueryNtfsVolumeData(byte[] buffer, out int bytesReturned)
    {
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);

        try
        {
            var ok = TryDeviceIoControlOverlapped(
                _unbuffered,
                FsctlGetNtfsVolumeData,
                IntPtr.Zero,
                0,
                pinned.AddrOfPinnedObject(),
                buffer.Length,
                out var returned);
            bytesReturned = ok ? (int)returned : 0;
            return ok;
        }
        finally
        {
            pinned.Free();
        }
    }

    internal static MftFileVolume? TryOpenNoFlush(char letter)
    {
        var path = $@"\\.\{letter}:";
        var handle = OpenHandle(path, NoBuffering | Overlapped);

        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        if (!TrySuppressVolumeOpenFlush(handle))
        {
            handle.Dispose();
            return null;
        }

        return new(handle, path, $"{letter}:", suppressFlush: true);
    }

    internal static SafeFileHandle OpenHandle(string path, uint flags)
    {
        return CreateFileW(
            path,
            GenericRead,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            flags,
            IntPtr.Zero);
    }

    private static void Read(SafeFileHandle handle, long offset, Span<byte> buffer, int required)
    {
        var read = 0;

        while (read < required)
        {
            var more = RandomAccess.Read(handle, buffer[read..], offset + read);

            if (more == 0)
            {
                throw new EndOfStreamException($"Чтение тома оборвалось на смещении {offset + read}");
            }

            read += more;
        }
    }

    private static async ValueTask ReadAsync(SafeFileHandle handle, long offset, Memory<byte> buffer, int required, CancellationToken cancel)
    {
        var read = 0;

        while (read < required)
        {
            var more = await RandomAccess.ReadAsync(handle, buffer[read..], offset + read, cancel).ConfigureAwait(false);

            if (more == 0)
            {
                throw new EndOfStreamException($"Чтение тома оборвалось на смещении {offset + read}");
            }

            read += more;
        }
    }

    private bool TryReadDirect(long offset, Span<byte> buffer)
    {
        try
        {
            Read(_unbuffered, offset, buffer, buffer.Length);
            return true;
        }
        catch (IOException exception) when (exception.HResult == InvalidParameterResult)
        {
            return false;
        }
    }

    private async ValueTask<bool> TryReadDirectAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
    {
        try
        {
            await ReadAsync(_unbuffered, offset, buffer, buffer.Length, cancel).ConfigureAwait(false);
            return true;
        }
        catch (IOException exception) when (exception.HResult == InvalidParameterResult)
        {
            return false;
        }
    }

    private bool TryReadAligned(long offset, Span<byte> buffer)
    {
        var start = offset - (offset % Alignment);
        var skip = (int)(offset - start);
        var length = (skip + buffer.Length + Alignment - 1) / Alignment * Alignment;
        var bounce = RentBounce(length + Alignment);

        try
        {
            var shift = (int)((Alignment - (Marshal.UnsafeAddrOfPinnedArrayElement(bounce, 0).ToInt64() % Alignment)) % Alignment);
            var aligned = bounce.AsSpan(shift, length);

            try
            {
                Read(_unbuffered, start, aligned, skip + buffer.Length);
            }
            catch (IOException exception) when (exception.HResult == InvalidParameterResult)
            {
                return false;
            }

            aligned.Slice(skip, buffer.Length).CopyTo(buffer);
            return true;
        }
        finally
        {
            ReturnBounce(bounce);
        }
    }

    private async ValueTask<bool> TryReadAlignedAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
    {
        var start = offset - (offset % Alignment);
        var skip = (int)(offset - start);
        var length = (skip + buffer.Length + Alignment - 1) / Alignment * Alignment;
        var bounce = RentBounce(length + Alignment);

        try
        {
            var shift = (int)((Alignment - (Marshal.UnsafeAddrOfPinnedArrayElement(bounce, 0).ToInt64() % Alignment)) % Alignment);
            var aligned = bounce.AsMemory(shift, length);

            try
            {
                await ReadAsync(_unbuffered, start, aligned, skip + buffer.Length, cancel).ConfigureAwait(false);
            }
            catch (IOException exception) when (exception.HResult == InvalidParameterResult)
            {
                return false;
            }

            bounce.AsSpan(shift + skip, buffer.Length).CopyTo(buffer.Span);
            return true;
        }
        finally
        {
            ReturnBounce(bounce);
        }
    }

    private byte[] RentBounce(int minimumBytes)
    {
        lock (_bounceGate)
        {
            while (_bouncePool.TryPop(out var candidate))
            {
                if (candidate.Length >= minimumBytes)
                {
                    return candidate;
                }
            }
        }

        return GC.AllocateUninitializedArray<byte>(minimumBytes, pinned: true);
    }

    private void ReturnBounce(byte[] bounce)
    {
        lock (_bounceGate)
        {
            if (_bouncePool.Count < MaxPooledBounces)
            {
                _bouncePool.Push(bounce);
            }
        }
    }

    private SafeFileHandle Downgrade()
    {
        lock (_gate)
        {
            if (_buffered is not null)
            {
                return _buffered;
            }

            var handle = OpenHandle(_path, Overlapped);

            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();

                throw new IOException($"Не удалось переоткрыть том {_name} буферизованным, код {error}");
            }

            if (_suppressFlush && !TrySuppressVolumeOpenFlush(handle))
            {
                handle.Dispose();
                throw new IOException($"Не удалось подавить сброс тома {_name} при буферизованном переоткрытии");
            }

            Volatile.Write(ref _buffered, handle);
            return handle;
        }
    }

    private static bool TrySuppressVolumeOpenFlush(SafeFileHandle handle)
    {
        var info = new MftMarkHandleInfo
        {
            UsnSourceInfo = 0,
            VolumeHandle = IntPtr.Zero,
            HandleInfo = MarkHandleSuppressVolumeOpenFlush,
        };

        var size = Marshal.SizeOf<MftMarkHandleInfo>();
        var buffer = Marshal.AllocHGlobal(size);

        try
        {
            Marshal.StructureToPtr(info, buffer, false);
            return TryDeviceIoControlOverlapped(handle, FsctlMarkHandle, buffer, size, IntPtr.Zero, 0, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryDeviceIoControlOverlapped(
        SafeFileHandle handle,
        uint ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned)
    {
        bytesReturned = 0;
        var eventHandle = CreateEventW(IntPtr.Zero, true, false, null);

        if (eventHandle == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var overlapped = new NativeOverlapped
            {
                EventHandle = eventHandle,
            };

            if (DeviceIoControl(
                    handle,
                    ioControlCode,
                    inBuffer,
                    inBufferSize,
                    outBuffer,
                    outBufferSize,
                    out bytesReturned,
                    ref overlapped))
            {
                return true;
            }

            if (Marshal.GetLastWin32Error() != ErrorIoPending)
            {
                return false;
            }

            return GetOverlappedResult(handle, ref overlapped, out bytesReturned, true);
        }
        finally
        {
            CloseHandle(eventHandle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint ioControlCode,
        IntPtr inBuffer,
        int inBufferSize,
        IntPtr outBuffer,
        int outBufferSize,
        out uint bytesReturned,
        ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOverlappedResult(
        SafeFileHandle file,
        ref NativeOverlapped overlapped,
        out uint bytesTransferred,
        [MarshalAs(UnmanagedType.Bool)] bool wait);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateEventW(
        IntPtr attributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}

[StructLayout(LayoutKind.Sequential)]
internal struct MftMarkHandleInfo
{
    public uint UsnSourceInfo;
    public IntPtr VolumeHandle;
    public uint HandleInfo;
}
