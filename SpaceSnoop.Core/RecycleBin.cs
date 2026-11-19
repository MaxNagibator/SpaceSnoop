using Microsoft.Win32.SafeHandles;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

internal readonly record struct RecycleBinContent(bool Ok, long Bytes, long Items);

public sealed class RecycleBinUnavailableException : IOException
{
    public string Path { get; }

    public RecycleBinUnavailableException(string path)
        : this(path, Describe(path))
    {
    }

    public RecycleBinUnavailableException(string path, string message)
        : base(message)
    {
        Path = path;
    }

    public RecycleBinUnavailableException(string path, string message, Exception innerException)
        : base(message, innerException)
    {
        Path = path;
    }

    public static string Describe(string path)
    {
        return $"Путь «{path}» не поддерживает корзину Windows – безопасное удаление в корзину невозможно.";
    }
}

public static class RecycleBin
{
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOFX_RECYCLEONDELETE = 0x0008_0000;
    private const uint FOFX_EARLYFAILURE = 0x0010_0000;
    private const uint RecycleOperationFlags = FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE | FOF_NOERRORUI | FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO;
    private const uint SHERB_NOCONFIRMATION = 0x0001;
    private const uint SHERB_NOPROGRESSUI = 0x0002;
    private const uint SHERB_NOSOUND = 0x0004;
    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x0200_0000;
    private const uint VolumeNameDos = 0;
    private const int InitialBuffer = 512;
    private const string LongUncPrefix = @"\\?\UNC\";
    private const string LongPrefix = @"\\?\";

    private static readonly Guid FileOperationClsid = new("3AD05575-8857-4850-9277-11B85BDB8E09");
    private static readonly Guid ShellItemIid = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");

    public static bool SupportsRecycleBin(string path)
    {
        return SupportsRecycleBin(path, ReadDriveType, ReadFinalPath);
    }

    public static void DeleteSilent(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Пустой путь на удаление.", nameof(path));
        }

        DeleteSilent([path], SupportsRecycleBin, RecycleWithFileOperation);
    }

    public static void DeleteSilent(IReadOnlyList<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        DeleteSilent(paths, SupportsRecycleBin, RecycleWithFileOperation);
    }

    internal static bool SupportsRecycleBin(string path, Func<string, DriveType> getDriveType, Func<string, string?> getFinalPath)
    {
        if (string.IsNullOrWhiteSpace(path) || UncPath.IsUnc(path))
        {
            return false;
        }

        string fullPath;

        try
        {
            fullPath = Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
        {
            return false;
        }

        if (UncPath.IsUnc(fullPath))
        {
            return false;
        }

        var root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrEmpty(root) && getDriveType(root) == DriveType.Network)
        {
            return false;
        }

        var finalPath = getFinalPath(fullPath);

        if (string.IsNullOrEmpty(finalPath))
        {
            return true;
        }

        if (finalPath.StartsWith(LongUncPrefix, StringComparison.OrdinalIgnoreCase) || UncPath.IsUnc(finalPath))
        {
            return false;
        }

        var normalizedFinal = finalPath.StartsWith(LongPrefix, StringComparison.Ordinal) ? finalPath[LongPrefix.Length..] : finalPath;
        var finalRoot = Path.GetPathRoot(normalizedFinal);

        return string.IsNullOrEmpty(finalRoot) || getDriveType(finalRoot) != DriveType.Network;
    }

    internal static void DeleteSilent(
        IReadOnlyList<string> paths,
        Func<string, bool> supportsRecycleBin,
        Action<IReadOnlyList<string>> recycleItems)
    {
        if (paths.Count == 0)
        {
            return;
        }

        var fullPaths = new string[paths.Count];

        for (var i = 0; i < paths.Count; i++)
        {
            var path = paths[i];

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Пустой путь в списке на удаление.", nameof(paths));
            }

            if (!supportsRecycleBin(path))
            {
                throw new RecycleBinUnavailableException(path);
            }

            fullPaths[i] = Path.GetFullPath(path.Trim());
        }

        recycleItems(fullPaths);
    }

    internal static void RecycleWithFileOperation(IReadOnlyList<string> paths)
    {
        try
        {
            RunOnSta(() => PerformFileOperation(paths));
        }
        catch (COMException exception)
        {
            var message = paths.Count == 1
                ? $"Не удалось удалить в корзину «{paths[0]}»."
                : $"Не удалось удалить в корзину пачку из {paths.Count} объект(ов).";
            throw new IOException(message, exception);
        }
    }

    internal static void DeleteFilePermanently(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReparsePoint) == 0 && (attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
        }

        File.Delete(path);
    }

    internal static void DeleteDirectoryPermanently(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var stack = new Stack<(DirectoryInfo Directory, bool Expanded)>();
        stack.Push((new(path), false));

        while (stack.Count > 0)
        {
            var (current, expanded) = stack.Pop();

            if (expanded)
            {
                if ((current.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    current.Attributes &= ~FileAttributes.ReadOnly;
                }

                Directory.Delete(current.FullName, false);
                continue;
            }

            if (IsReparseLink(current))
            {
                Directory.Delete(current.FullName, false);
                continue;
            }

            stack.Push((current, true));

            foreach (var file in current.EnumerateFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && (file.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    file.Attributes &= ~FileAttributes.ReadOnly;
                }

                File.Delete(file.FullName);
            }

            foreach (var sub in current.EnumerateDirectories())
            {
                if (IsReparseLink(sub))
                {
                    Directory.Delete(sub.FullName, false);
                    continue;
                }

                stack.Push((sub, false));
            }
        }
    }

    internal static RecycleBinContent Query()
    {
        var info = new ShQueryRbInfo
        {
            cbSize = Marshal.SizeOf<ShQueryRbInfo>(),
        };

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

    private static bool IsReparseLink(DirectoryInfo directory)
    {
        return (directory.Attributes & FileAttributes.ReparsePoint) != 0
            && ReparsePoint.IsLink(directory.FullName, true);
    }

    private static DriveType ReadDriveType(string root)
    {
        try
        {
            return new DriveInfo(root).DriveType;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or SecurityException)
        {
            return DriveType.Unknown;
        }
    }

    private static string? ReadFinalPath(string fullPath)
    {
        var current = Path.TrimEndingDirectorySeparator(fullPath);

        while (!string.IsNullOrEmpty(current))
        {
            using var handle = CreateFileW(current, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, FlagBackupSemantics, IntPtr.Zero);

            if (!handle.IsInvalid)
            {
                var buffer = new char[InitialBuffer];
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, VolumeNameDos);

                if (length >= buffer.Length)
                {
                    buffer = new char[length];
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, VolumeNameDos);
                }

                return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : null;
            }

            var parent = Path.GetDirectoryName(current);

            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    private static void RunOnSta(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }

        ExceptionDispatchInfo? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void PerformFileOperation(IReadOnlyList<string> paths)
    {
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(FileOperationClsid, true)!)!;
        var items = new List<IShellItem>(paths.Count);

        try
        {
            operation.SetOperationFlags(RecycleOperationFlags);
            var itemId = ShellItemIid;

            foreach (var path in paths)
            {
                SHCreateItemFromParsingName(path, nint.Zero, ref itemId, out var item);
                items.Add(item);
                operation.DeleteItem(item, nint.Zero);
            }

            operation.PerformOperations();
            operation.GetAnyOperationsAborted(out var aborted);

            if (aborted)
            {
                var failureMessage = paths.Count == 1
                    ? $"Не удалось удалить в корзину «{paths[0]}»."
                    : $"Не удалось удалить в корзину пачку из {paths.Count} объект(ов).";
                throw new IOException(failureMessage);
            }

            if (paths.FirstOrDefault(static x => File.Exists(x) || Directory.Exists(x)) is { } remaining)
            {
                throw new IOException($"Не удалось удалить в корзину «{remaining}».");
            }
        }
        finally
        {
            foreach (var item in items)
            {
                Marshal.FinalReleaseComObject(item);
            }

            Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path,
        nint context,
        ref Guid id,
        out IShellItem item);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint size, uint flags);

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

    [ComImport]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
    }

    [ComImport]
    [Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        void Advise(nint sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog(nint dialog);
        void SetProperties(nint properties);
        void SetOwnerWindow(nint owner);
        void ApplyPropertiesToItem(nint item);
        void ApplyPropertiesToItems(nint items);
        void RenameItem(nint item, nint name, nint sink);
        void RenameItems(nint items, nint name);
        void MoveItem(nint item, nint destination, nint name, nint sink);
        void MoveItems(nint items, nint destination);
        void CopyItem(nint item, nint destination, nint name, nint sink);
        void CopyItems(nint items, nint destination);
        void DeleteItem(IShellItem item, nint sink);
        void DeleteItems(nint items);
        void NewItem(nint destination, uint attributes, nint name, nint template, nint sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
