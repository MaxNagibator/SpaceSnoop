using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

public static class PathCase
{
    private const int ProbeLimit = 8;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileShareAll = 0x0007;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x0200_0000;
    private const int FileCaseSensitiveInfo = 23;
    private const uint CaseSensitiveDirFlag = 0x0001;

    public static bool PlatformDefaultIsCaseSensitive { get; } = !OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS();

    public static StringComparer ComparerFor(string path)
    {
        return IsCaseSensitive(path) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    public static StringComparer Stricter(StringComparer left, StringComparer right)
    {
        return IsSensitive(left) || IsSensitive(right) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    }

    public static bool IsSensitive(StringComparer comparer)
    {
        return ReferenceEquals(comparer, StringComparer.Ordinal);
    }

    public static bool IsCaseSensitive(string path)
    {
        if (ReadDirectoryFlag(path) is { } flag)
        {
            return flag;
        }

        return ProbeCaseSensitive(path);
    }

    private static bool? ReadDirectoryFlag(string path)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(path))
        {
            return null;
        }

        using var handle = CreateFile(path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            return null;
        }

        return GetFileInformationByHandleEx(handle, FileCaseSensitiveInfo, out var flags, sizeof(uint))
            ? (flags & CaseSensitiveDirFlag) != 0
            : null;
    }

    private static bool ProbeCaseSensitive(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return PlatformDefaultIsCaseSensitive;
            }

            var names = Directory.EnumerateFileSystemEntries(path)
                .Take(ProbeLimit)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();

            if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            {
                return true;
            }

            var flipped = names.Select(FlipCase).OfType<string>().FirstOrDefault();

            if (flipped is null)
            {
                return PlatformDefaultIsCaseSensitive;
            }

            var probe = Path.Combine(path, flipped);

            return !File.Exists(probe) && !Directory.Exists(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return PlatformDefaultIsCaseSensitive;
        }
    }

    private static string? FlipCase(string name)
    {
        var flipped = string.Create(
            name.Length,
            name,
            static (span, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                {
                    var symbol = source[i];
                    span[i] = char.IsUpper(symbol) ? char.ToLowerInvariant(symbol) : char.ToUpperInvariant(symbol);
                }
            });

        return string.Equals(flipped, name, StringComparison.Ordinal) ? null : flipped;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out uint lpFileInformation,
        uint dwBufferSize);
}
