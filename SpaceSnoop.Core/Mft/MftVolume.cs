using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal interface IMftVolume : IDisposable
{
    void ReadAt(long offset, Span<byte> buffer);
}

internal sealed class MftFileVolume : IMftVolume
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint NoBuffering = 0x20000000;
    private const uint Overlapped = 0x40000000;
    private const int Alignment = 4096;
    private const int ErrorAccessDenied = 5;
    private const int InvalidParameterResult = unchecked((int)0x80070057);

    private readonly Lock _gate = new();
    private readonly string _path;
    private readonly string _name;
    private readonly SafeFileHandle _unbuffered;

    private SafeFileHandle? _buffered;

    private MftFileVolume(SafeFileHandle unbuffered, string path, string name)
    {
        _unbuffered = unbuffered;
        _path = path;
        _name = name;
    }

    public static MftFileVolume Open(char letter)
    {
        return Open($@"\\.\{letter}:", $"{letter}:");
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        var buffered = Volatile.Read(ref _buffered);

        if (buffered is not null)
        {
            Read(buffered, offset, buffer, buffer.Length);
            return;
        }

        // TODO: прямое чтение ждёт кратности 4 КБ, а не сектору: на томе с кластером меньше 4 КБ блоки таблицы
        // идут через промежуточный буфер с копией. Триггер – замер такого тома, где фаза чтения $MFT заметно
        // медленнее, чем на кластере 4 КБ; лечение – кратность по размеру сектора из загрузочного сектора.
        if (offset % Alignment == 0 && buffer.Length % Alignment == 0 && TryReadDirect(offset, buffer))
        {
            return;
        }

        if (!TryReadAligned(offset, buffer))
        {
            Read(Downgrade(), offset, buffer, buffer.Length);
        }
    }

    public void Dispose()
    {
        _unbuffered.Dispose();
        _buffered?.Dispose();
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

    private static SafeFileHandle OpenHandle(string path, uint flags)
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

    private bool TryReadAligned(long offset, Span<byte> buffer)
    {
        var start = offset - (offset % Alignment);
        var skip = (int)(offset - start);
        var length = (skip + buffer.Length + Alignment - 1) / Alignment * Alignment;
        var bounce = GC.AllocateUninitializedArray<byte>(length + Alignment, true);
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
