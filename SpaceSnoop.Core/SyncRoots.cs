using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core;

public static class SyncRoots
{
    public const string OverlapMessage = "Каталоги совпадают или вложены друг в друга – синхронизация невозможна.";

    public const string UnresolvedLinkMessage =
        "Путь к каталогу проходит через ссылку, цель которой сейчас недоступна. Синхронизация отменена, пока ссылку нельзя проверить.";

    private const uint FileReadAttributes = 0x80;
    private const uint ShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FlagBackupSemantics = 0x02000000;
    private const uint VolumeNameDos = 0;
    private const uint VolumeNameGuid = 1;
    private const int ErrorPathNotFound = 3;
    private const int InitialBuffer = 512;
    private const string LongPrefix = @"\\?\";
    private const string LongUncPrefix = @"\\?\UNC\";

    private enum FinalKind
    {
        None = 0,
        Resolved = 1,
        NoVolumeName = 2,
        UnresolvedLink = 3,
    }

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool Overlap(string left, string right)
    {
        return Refusal(left, right) is not null;
    }

    // TODO: SMB-шара своей машины (\\localhost\share, \\имя-машины\c$) на тот же каталог окончательным путём к локальному не сводится,
    // перекрытие не видно; при первом профиле с такой шарой – сверять FILE_ID_INFO корня и его предков вместо путей
    public static string? Refusal(string left, string right)
    {
        if (Normalize(left) is not { } a || Normalize(right) is not { } b)
        {
            return null;
        }

        if (Nested(a, b))
        {
            return OverlapMessage;
        }

        var resolvedLeft = Resolve(a, VolumeNameDos);
        var resolvedRight = Resolve(b, VolumeNameDos);

        if (resolvedLeft.Kind == FinalKind.NoVolumeName || resolvedRight.Kind == FinalKind.NoVolumeName)
        {
            resolvedLeft = Resolve(a, VolumeNameGuid);
            resolvedRight = Resolve(b, VolumeNameGuid);
        }

        if (resolvedLeft.Kind == FinalKind.UnresolvedLink || resolvedRight.Kind == FinalKind.UnresolvedLink)
        {
            return UnresolvedLinkMessage;
        }

        // TODO: сторона без GUID-имени (сетевой путь или том, не известный диспетчеру монтирования) против тома без буквы перекрытием не считается;
        // при первом локальном томе без GUID-имени – сравнивать в пространстве имён NT (VOLUME_NAME_NT)
        if (resolvedLeft.Kind == FinalKind.NoVolumeName || resolvedRight.Kind == FinalKind.NoVolumeName)
        {
            return null;
        }

        return Nested(resolvedLeft.Path, resolvedRight.Path) ? OverlapMessage : null;
    }

    public static void EnsureDisjoint(string left, string right)
    {
        if (Refusal(left, right) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    internal static string StripLongPrefix(string final)
    {
        if (final.StartsWith(LongUncPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return @"\\" + final[LongUncPrefix.Length..];
        }

        return final.StartsWith(LongPrefix, StringComparison.Ordinal) ? final[LongPrefix.Length..] : final;
    }

    internal static string? Normalize(string path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path)
                ? null
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static bool Nested(string left, string right)
    {
        var a = Trim(left);
        var b = Trim(right);
        var (shorter, longer) = a.Length <= b.Length ? (a, b) : (b, a);

        return (shorter.Length == longer.Length
                || longer[shorter.Length] == Path.DirectorySeparatorChar
                || longer[shorter.Length] == Path.AltDirectorySeparatorChar)
            && SamePath(shorter, longer[..shorter.Length]);
    }

    internal static bool SamePath(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var start = Path.GetPathRoot(right)?.Length ?? 0;

        while (start < right.Length && (right[start] == Path.DirectorySeparatorChar || right[start] == Path.AltDirectorySeparatorChar))
        {
            start++;
        }

        while (start < right.Length)
        {
            var end = right.IndexOfAny(Separators, start);

            if (end < 0)
            {
                end = right.Length;
            }

            if (!left.AsSpan(start, end - start).SequenceEqual(right.AsSpan(start, end - start))
                && PathCase.IsCaseSensitive(right[..start]))
            {
                return false;
            }

            start = end + 1;
        }

        return true;
    }

    // TODO: компонент, который не открылся и атрибуты которого не читаются (сетевой сбой, спящий том), сравнивается по GetFullPath –
    // ссылку в нём не видно; при первом таком случае с перекрытием – отказывать, пока сторона не разрешилась
    private static Final Resolve(string fullPath, uint volumeName)
    {
        var current = fullPath;
        var tail = new Stack<string>();

        while (!string.IsNullOrEmpty(current))
        {
            var final = ReadFinalPath(current, volumeName);

            if (final.Kind == FinalKind.Resolved)
            {
                return tail.Count == 0
                    ? final
                    : final with
                    {
                        Path = Path.Combine([final.Path, .. tail]),
                    };
            }

            if (final.Kind == FinalKind.NoVolumeName)
            {
                return final;
            }

            if (IsLink(current))
            {
                return new(FinalKind.UnresolvedLink, current);
            }

            var parent = Path.GetDirectoryName(current);

            if (parent is null)
            {
                break;
            }

            tail.Push(Path.GetFileName(current));
            current = parent;
        }

        return new(FinalKind.Resolved, fullPath);
    }

    private static bool IsLink(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 && ReparsePoint.IsLink(path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string Trim(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static Final ReadFinalPath(string path, uint volumeName)
    {
        using var handle = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, FlagBackupSemantics, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return default;
        }

        var buffer = new char[InitialBuffer];
        var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, volumeName);

        if (length >= buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, volumeName);
        }

        if (length == 0)
        {
            return volumeName == VolumeNameGuid || Marshal.GetLastPInvokeError() == ErrorPathNotFound
                ? new(FinalKind.NoVolumeName, path)
                : default;
        }

        if (length >= buffer.Length)
        {
            return default;
        }

        var final = new string(buffer, 0, (int)length);
        return new(FinalKind.Resolved, volumeName == VolumeNameDos ? StripLongPrefix(final) : final);
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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, [Out] char[] buffer, uint size, uint flags);

    private readonly record struct Final(FinalKind Kind, string Path);
}
