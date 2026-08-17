using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal interface IMftVolume : IDisposable
{
    IMftVolume Reopen();

    void ReadAt(long offset, Span<byte> buffer);
}

internal sealed class MftFileVolume : IMftVolume
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint NoBuffering = 0x20000000;
    private const int ErrorAccessDenied = 5;
    private const int InvalidParameterResult = unchecked((int)0x80070057);

    private readonly char _letter;

    private SafeFileHandle _handle;
    private bool _unbuffered;

    private MftFileVolume(SafeFileHandle handle, char letter, bool unbuffered)
    {
        _handle = handle;
        _letter = letter;
        _unbuffered = unbuffered;
    }

    public static MftFileVolume Open(char letter)
    {
        return Open(letter, false);
    }

    public IMftVolume Reopen()
    {
        return Open(_letter, true);
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            int more;

            try
            {
                more = RandomAccess.Read(_handle, buffer[read..], offset + read);
            }
            catch (IOException exception) when (_unbuffered && exception.HResult == InvalidParameterResult)
            {
                Downgrade();
                continue;
            }

            if (more == 0)
            {
                throw new EndOfStreamException($"Чтение тома оборвалось на смещении {offset + read}");
            }

            read += more;
        }
    }

    public void Dispose()
    {
        _handle.Dispose();
    }

    private static MftFileVolume Open(char letter, bool unbuffered)
    {
        var handle = OpenHandle(letter, unbuffered);

        if (!handle.IsInvalid)
        {
            return new(handle, letter, unbuffered);
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        throw error == ErrorAccessDenied
            ? new UnauthorizedAccessException($"Чтение $MFT тома {letter}: требует прав администратора")
            : new IOException($"Не удалось открыть том {letter}:, код {error}");
    }

    private static SafeFileHandle OpenHandle(char letter, bool unbuffered)
    {
        return CreateFileW(
            $@"\\.\{letter}:",
            GenericRead,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            unbuffered ? NoBuffering : 0,
            IntPtr.Zero);
    }

    private void Downgrade()
    {
        var handle = OpenHandle(_letter, false);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            handle.Dispose();

            throw new IOException($"Не удалось переоткрыть том {_letter}: буферизованным, код {error}");
        }

        _handle.Dispose();
        _handle = handle;
        _unbuffered = false;
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
