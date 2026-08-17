using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal interface IMftVolume
{
    void ReadAt(long offset, Span<byte> buffer);
}

internal sealed class MftFileVolume : IMftVolume, IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorAccessDenied = 5;

    private readonly SafeFileHandle _handle;

    private MftFileVolume(SafeFileHandle handle)
    {
        _handle = handle;
    }

    public static MftFileVolume Open(char letter)
    {
        var handle = CreateFileW($@"\\.\{letter}:", GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            return new(handle);
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        throw error == ErrorAccessDenied
            ? new UnauthorizedAccessException($"Чтение $MFT тома {letter}: требует прав администратора")
            : new IOException($"Не удалось открыть том {letter}:, код {error}");
    }

    public void ReadAt(long offset, Span<byte> buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var more = RandomAccess.Read(_handle, buffer[read..], offset + read);

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
