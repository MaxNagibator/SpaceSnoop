using Microsoft.Win32.SafeHandles;
using SpaceSnoop.Core.Duplicates;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security;

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
    private const uint VolumeNameNt = 2;
    private const uint ShareTypeMask = 0x0000_00FF;
    private const uint ShareTypeDiskTree = 0;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int InitialBuffer = 512;
    private const int MaxParentAscent = 64;
    private const int DnsResolveTimeoutMs = 500;
    private const string LongPrefix = @"\\?\";
    private const string LongUncPrefix = @"\\?\UNC\";

    private enum PhysicalIdentityOutcome
    {
        None = 0,
        Overlap = 1,
        Disjoint = 2,
        Unknown = 3,
    }

    internal enum FinalKind
    {
        None = 0,
        Resolved = 1,
        NoVolumeName = 2,
        UnresolvedLink = 3,
    }

    internal enum IdentityKind
    {
        None = 0,
        NotFound = 1,
        Found = 2,
        ZeroId = 3,
        Unavailable = 4,
    }

    internal readonly record struct Final(FinalKind Kind, string Path);

    // TODO: VolumeSerialNumber в FILE_ID_INFO 64-битный, здесь усечён до 32 бит – расширить до ulong при коллизиях
    internal readonly record struct DirIdentity(uint VolumeSerial32, ulong FileIdLow, ulong FileIdHigh);

    internal readonly record struct IdentityProbe(IdentityKind Kind, DirIdentity Id)
    {
        public static IdentityProbe Missing { get; } = new(IdentityKind.NotFound, default);
        public static IdentityProbe Zero { get; } = new(IdentityKind.ZeroId, default);
        public static IdentityProbe Failed { get; } = new(IdentityKind.Unavailable, default);

        public static IdentityProbe From(DirIdentity id)
        {
            return new(IdentityKind.Found, id);
        }
    }

    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    public static bool Overlap(string left, string right)
    {
        return Refusal(left, right) is not null;
    }

    public static string? Refusal(string left, string right)
    {
        return RefusalCore(left, right, ReadFinalPath, TryMapLocalShare, ReadDirectoryIdentity, ResolveHostAddresses);
    }

    public static void EnsureDisjoint(string left, string right)
    {
        if (Refusal(left, right) is { } refusal)
        {
            throw new InvalidOperationException(refusal);
        }
    }

    internal static string? RefusalCore(
        string left,
        string right,
        Func<string, uint, Final> readFinalPath,
        Func<string, string?> tryMapLocalShare,
        Func<string, IdentityProbe> readIdentity)
    {
        return RefusalCore(left, right, readFinalPath, tryMapLocalShare, readIdentity, ResolveHostAddresses);
    }

    internal static string? RefusalCore(
        string left,
        string right,
        Func<string, uint, Final> readFinalPath,
        Func<string, string?> tryMapLocalShare,
        Func<string, IdentityProbe> readIdentity,
        Func<string, IReadOnlyList<IPAddress>> resolveHostAddresses)
    {
        if (Normalize(left) is not { } a || Normalize(right) is not { } b)
        {
            return null;
        }

        if (Nested(a, b))
        {
            return OverlapMessage;
        }

        var hasUncInput = UncPath.IsUnc(a) || UncPath.IsUnc(b);
        var mappedLeft = a;
        var mappedRight = b;

        if (hasUncInput)
        {
            mappedLeft = (UncPath.IsUnc(a) ? tryMapLocalShare(a) : null) ?? a;
            mappedRight = (UncPath.IsUnc(b) ? tryMapLocalShare(b) : null) ?? b;

            if ((!string.Equals(mappedLeft, a, StringComparison.Ordinal) || !string.Equals(mappedRight, b, StringComparison.Ordinal))
                && Nested(mappedLeft, mappedRight))
            {
                return OverlapMessage;
            }
        }

        var dosLeft = Resolve(mappedLeft, VolumeNameDos, readFinalPath);
        var dosRight = Resolve(mappedRight, VolumeNameDos, readFinalPath);
        var resolvedLeft = dosLeft;
        var resolvedRight = dosRight;

        if (resolvedLeft.Kind == FinalKind.NoVolumeName || resolvedRight.Kind == FinalKind.NoVolumeName)
        {
            resolvedLeft = Resolve(mappedLeft, VolumeNameGuid, readFinalPath);
            resolvedRight = Resolve(mappedRight, VolumeNameGuid, readFinalPath);
        }

        if (resolvedLeft.Kind == FinalKind.NoVolumeName || resolvedRight.Kind == FinalKind.NoVolumeName)
        {
            resolvedLeft = Resolve(mappedLeft, VolumeNameNt, readFinalPath);
            resolvedRight = Resolve(mappedRight, VolumeNameNt, readFinalPath);
        }

        if (resolvedLeft.Kind == FinalKind.UnresolvedLink || resolvedRight.Kind == FinalKind.UnresolvedLink)
        {
            return UnresolvedLinkMessage;
        }

        if (resolvedLeft.Kind == FinalKind.Resolved
            && resolvedRight.Kind == FinalKind.Resolved
            && Nested(resolvedLeft.Path, resolvedRight.Path))
        {
            return OverlapMessage;
        }

        var identityLeft = dosLeft.Kind == FinalKind.Resolved ? dosLeft.Path : mappedLeft;
        var identityRight = dosRight.Kind == FinalKind.Resolved ? dosRight.Path : mappedRight;

        if (hasUncInput || UncPath.IsUnc(identityLeft) || UncPath.IsUnc(identityRight))
        {
            var identityOutcome = CheckPhysicalIdentityOverlap(identityLeft, identityRight, readIdentity);

            if (identityOutcome == PhysicalIdentityOutcome.Overlap)
            {
                return OverlapMessage;
            }

            if (identityOutcome == PhysicalIdentityOutcome.Unknown
                && HasUncShareOverlap(identityLeft, identityRight, resolveHostAddresses))
            {
                return OverlapMessage;
            }
        }

        return null;
    }

    internal static bool HasPhysicalIdentityOverlap(string left, string right, Func<string, IdentityProbe> readIdentity)
    {
        return CheckPhysicalIdentityOverlap(left, right, readIdentity) == PhysicalIdentityOutcome.Overlap;
    }

    private static PhysicalIdentityOutcome CheckPhysicalIdentityOverlap(
        string left,
        string right,
        Func<string, IdentityProbe> readIdentity)
    {
        var leftFound = TryLocateExistingAncestor(left, readIdentity, out var leftAncestor, out var leftId, out var leftTail, out var leftUnknown);
        var rightFound = TryLocateExistingAncestor(right, readIdentity, out var rightAncestor, out var rightId, out var rightTail, out var rightUnknown);

        if (!leftFound || !rightFound)
        {
            return leftUnknown || rightUnknown ? PhysicalIdentityOutcome.Unknown : PhysicalIdentityOutcome.Disjoint;
        }

        if (leftId == rightId)
        {
            return TailPrefixesOverlap(leftTail, rightTail) ? PhysicalIdentityOutcome.Overlap : PhysicalIdentityOutcome.Disjoint;
        }

        if (leftTail.Length == 0)
        {
            var match = AncestorMatches(rightAncestor, leftId, readIdentity);

            if (match != PhysicalIdentityOutcome.Disjoint)
            {
                return match;
            }
        }

        return rightTail.Length == 0
            ? AncestorMatches(leftAncestor, rightId, readIdentity)
            : PhysicalIdentityOutcome.Disjoint;
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
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or SecurityException)
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

    private static bool TryLocateExistingAncestor(
        string fullPath,
        Func<string, IdentityProbe> readIdentity,
        out string ancestor,
        out DirIdentity id,
        out string[] tail,
        out bool unknown)
    {
        var current = Path.TrimEndingDirectorySeparator(fullPath);
        var segments = new Stack<string>();
        unknown = false;

        while (!string.IsNullOrEmpty(current))
        {
            var probe = readIdentity(current);

            if (probe.Kind == IdentityKind.Found)
            {
                ancestor = current;
                id = probe.Id;
                tail = [.. segments];
                return true;
            }

            if (probe.Kind != IdentityKind.NotFound)
            {
                unknown = probe.Kind is IdentityKind.ZeroId or IdentityKind.Unavailable;
                break;
            }

            var parent = Path.GetDirectoryName(current);

            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            segments.Push(Path.GetFileName(current));
            current = parent;
        }

        ancestor = string.Empty;
        id = default;
        tail = [];
        return false;
    }

    private static PhysicalIdentityOutcome AncestorMatches(string startPath, DirIdentity targetId, Func<string, IdentityProbe> readIdentity)
    {
        var current = Path.GetDirectoryName(startPath);

        for (var depth = 0; depth < MaxParentAscent && !string.IsNullOrEmpty(current); depth++)
        {
            var probe = readIdentity(current);

            if (probe.Kind is IdentityKind.ZeroId or IdentityKind.Unavailable)
            {
                return PhysicalIdentityOutcome.Unknown;
            }

            if (probe.Kind == IdentityKind.Found && probe.Id == targetId)
            {
                return PhysicalIdentityOutcome.Overlap;
            }

            var next = Path.GetDirectoryName(current);

            if (string.IsNullOrEmpty(next) || string.Equals(next, current, StringComparison.Ordinal))
            {
                break;
            }

            current = next;
        }

        return PhysicalIdentityOutcome.Disjoint;
    }

    private static bool HasUncShareOverlap(
        string left,
        string right,
        Func<string, IReadOnlyList<IPAddress>> resolveHostAddresses)
    {
        if (!UncPath.TryParseShareRoot(left, out var leftShareRoot) || !UncPath.TryParseShareRoot(right, out var rightShareRoot))
        {
            return false;
        }

        var leftShare = UncPath.GetShare(leftShareRoot);
        var rightShare = UncPath.GetShare(rightShareRoot);

        if (string.IsNullOrEmpty(leftShare) || !string.Equals(leftShare, rightShare, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leftTail = ShareTailSegments(left, leftShareRoot);
        var rightTail = ShareTailSegments(right, rightShareRoot);

        if (!TailPrefixesOverlap(leftTail, rightTail))
        {
            return false;
        }

        var leftHost = UncPath.GetHost(leftShareRoot);
        var rightHost = UncPath.GetHost(rightShareRoot);

        if (string.IsNullOrEmpty(leftHost) || string.IsNullOrEmpty(rightHost))
        {
            return false;
        }

        if (string.Equals(leftHost, rightHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var leftLocal = IsLocalHost(leftHost);
        var rightLocal = IsLocalHost(rightHost);

        if (leftLocal && rightLocal)
        {
            return true;
        }

        var leftAddresses = ResolveNormalizedHostAddresses(leftHost, resolveHostAddresses);
        var rightAddresses = ResolveNormalizedHostAddresses(rightHost, resolveHostAddresses);

        if ((leftLocal && rightAddresses.Any(IsLocalAddress)) || (rightLocal && leftAddresses.Any(IsLocalAddress)))
        {
            return true;
        }

        if (leftAddresses.Count == 0 || rightAddresses.Count == 0)
        {
            return false;
        }

        foreach (var leftAddress in leftAddresses)
        {
            if (rightAddresses.Contains(leftAddress))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] ShareTailSegments(string path, string shareRoot)
    {
        var normalized = UncPath.NormalizeUnc(path);

        if (normalized.Length <= shareRoot.Length + 1)
        {
            return [];
        }

        return normalized[(shareRoot.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IReadOnlyList<IPAddress> ResolveNormalizedHostAddresses(
        string host,
        Func<string, IReadOnlyList<IPAddress>> resolveHostAddresses)
    {
        var resolved = resolveHostAddresses(host);

        if (resolved.Count == 0)
        {
            return [];
        }

        var normalized = new IPAddress[resolved.Count];

        for (var i = 0; i < resolved.Count; i++)
        {
            normalized[i] = NormalizeAddress(resolved[i]);
        }

        return normalized;
    }

    private static IReadOnlyList<IPAddress> ResolveHostAddresses(string host)
    {
        if (IPAddress.TryParse(host, out var parsed))
        {
            return [parsed];
        }

        try
        {
            using var cts = new CancellationTokenSource(DnsResolveTimeoutMs);
            return Task.Run(() => Dns.GetHostAddressesAsync(host, cts.Token))
                .WaitAsync(TimeSpan.FromMilliseconds(DnsResolveTimeoutMs))
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or TimeoutException or ArgumentException)
        {
            return [];
        }
    }

    private static bool TailPrefixesOverlap(string[] leftTail, string[] rightTail)
    {
        var common = Math.Min(leftTail.Length, rightTail.Length);

        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(leftTail[i], rightTail[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static string? TryMapLocalShare(string uncPath)
    {
        if (!UncPath.TryParseShareRoot(uncPath, out var shareRoot))
        {
            return null;
        }

        var host = UncPath.GetHost(uncPath);
        var share = UncPath.GetShare(uncPath);

        if (string.IsNullOrEmpty(share) || !IsLocalHost(host))
        {
            return null;
        }

        if (NetShareGetInfo(null, share, 2, out var buffer) != 0 || buffer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var info = Marshal.PtrToStructure<ShareInfo2>(buffer);

            if ((info.shi2_type & ShareTypeMask) != ShareTypeDiskTree)
            {
                return null;
            }

            var localBase = Marshal.PtrToStringUni(info.shi2_path);

            if (string.IsNullOrWhiteSpace(localBase) || !Path.IsPathRooted(localBase))
            {
                return null;
            }

            var normalized = UncPath.NormalizeUnc(uncPath);
            var combined = normalized.Length > shareRoot.Length + 1
                ? Path.Combine(localBase, normalized[(shareRoot.Length + 1)..])
                : localBase;

            return Normalize(combined);
        }
        finally
        {
            NetApiBufferFree(buffer);
        }
    }

    private static bool IsLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        return IsLocalAddress(NormalizeAddress(address));
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(static x => x.GetIPProperties().UnicastAddresses)
                .Any(x => NormalizeAddress(x.Address).Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    private static IPAddress NormalizeAddress(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static IdentityProbe ReadDirectoryIdentity(string path)
    {
        if (path.Length == 2 && path[1] == ':' && char.IsAsciiLetter(path[0]))
        {
            path += Path.DirectorySeparatorChar;
        }

        using var handle = CreateFileW(path, FileReadAttributes, ShareAll, IntPtr.Zero, OpenExisting, FlagBackupSemantics, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            return error is ErrorFileNotFound or ErrorPathNotFound ? IdentityProbe.Missing : IdentityProbe.Failed;
        }

        if (FileIdentity.Read(handle) is not { } identity)
        {
            return IdentityProbe.Failed;
        }

        if (identity.FileIdLow == 0 && identity.FileIdHigh == 0)
        {
            return IdentityProbe.Zero;
        }

        return IdentityProbe.From(new(unchecked((uint)identity.VolumeSerialNumber), identity.FileIdLow, identity.FileIdHigh));
    }

    // TODO: компонент, который не открылся и атрибуты которого не читаются (сетевой сбой, спящий том), сравнивается по GetFullPath –
    // ссылку в нём не видно; при первом таком случае с перекрытием – отказывать, пока сторона не разрешилась
    private static Final Resolve(string fullPath, uint volumeName, Func<string, uint, Final> readFinalPath)
    {
        var current = fullPath;
        var tail = new Stack<string>();

        while (!string.IsNullOrEmpty(current))
        {
            var final = readFinalPath(current, volumeName);

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

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareGetInfo(string? servername, string netname, int level, out IntPtr bufptr);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo2
    {
        public IntPtr shi2_netname;
        public uint shi2_type;
        public IntPtr shi2_remark;
        public uint shi2_permissions;
        public uint shi2_max_uses;
        public uint shi2_current_uses;
        public IntPtr shi2_path;
        public IntPtr shi2_passwd;
    }
}
