using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Core;

internal readonly record struct RecycleBinContent(bool Ok, long Bytes, long Items);

public static class RecycleBin
{
    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const uint SHERB_NOCONFIRMATION = 0x0001;
    private const uint SHERB_NOPROGRESSUI = 0x0002;
    private const uint SHERB_NOSOUND = 0x0004;

    public static void DeleteSilent(string path)
    {
        var op = new ShFileOpStruct
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };

        var result = SHFileOperation(ref op);

        if (result != 0 || op.fAnyOperationsAborted != 0)
        {
            throw new IOException($"Не удалось удалить в корзину «{path}» (код {result}).");
        }
    }

    public static void DeleteSilent(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
        {
            return;
        }

        var builder = new StringBuilder();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Пустой путь в списке на удаление.", nameof(paths));
            }

            builder.Append(path).Append('\0');
        }

        builder.Append('\0');

        var op = new ShFileOpStruct
        {
            wFunc = FO_DELETE,
            pFrom = builder.ToString(),
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };

        var result = SHFileOperation(ref op);

        if (result != 0 || op.fAnyOperationsAborted != 0)
        {
            throw new IOException($"Не удалось удалить в корзину пачку из {paths.Count} объект(ов) (код {result}).");
        }
    }

    internal static RecycleBinContent Query()
    {
        var info = new ShQueryRbInfo { cbSize = Marshal.SizeOf<ShQueryRbInfo>() };

        return SHQueryRecycleBin(null, ref info) == 0 ? new(true, info.i64Size, info.i64NumItems) : new(false, 0, 0);
    }

    internal static void Empty()
    {
        var result = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

        if (result != 0)
        {
            throw new IOException($"Не удалось очистить корзину (код {result}).");
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct lpFileOp);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref ShQueryRbInfo pSHQueryRBInfo);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    [StructLayout(LayoutKind.Sequential)]
    private struct ShQueryRbInfo
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }
}
