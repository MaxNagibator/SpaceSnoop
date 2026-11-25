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
    private const int Alignment = 4096;
    private const int MinSectorBytes = 512;
    private const int MaxPooledBounces = 8;
    private const int ErrorAccessDenied = 5;
    private const int InvalidParameterResult = unchecked((int)0x80070057);

    private readonly Lock _gate = new();
    private readonly Lock _bounceGate = new();
    private readonly Stack<byte[]> _bouncePool = new();
    private readonly string _path;
    private readonly string _name;
    private readonly SafeFileHandle _unbuffered;

    private SafeFileHandle? _buffered;
    private volatile int _sectorAlignment = Alignment;

    private MftFileVolume(SafeFileHandle unbuffered, string path, string name)
    {
        _unbuffered = unbuffered;
        _path = path;
        _name = name;
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

            Volatile.Write(ref _buffered, handle);
            return handle;
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
}
