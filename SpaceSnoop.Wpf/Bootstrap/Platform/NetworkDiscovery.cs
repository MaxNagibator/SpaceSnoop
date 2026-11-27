using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public static class NetworkDiscovery
{
    private const uint ResourceConnected = 0x0000_0001;
    private const uint ResourceTypeDisk = 0x0000_0001;
    private const uint NoError = 0;
    private const uint ErrorMoreData = 234;
    private const uint ErrorNoMoreItems = 259;
    private const int InitialEnumBufferBytes = 16 * 1024;
    private const string MountPointsSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\MountPoints2";

    private static readonly Lock Gate = new();
    private static readonly HashSet<string> RememberedRoots = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<Action> PendingCallbacks = [];

    private static IReadOnlyList<string> _activeRoots = [];
    private static long _activeUpdatedMs = long.MinValue;
    private static bool _hasActiveCache;
    private static bool _activeRefreshing;
    private static Task _activeRefreshTask = Task.CompletedTask;

    internal static Func<IReadOnlyList<string>> PersistentReader { get; set; } = ReadPersistentRegistry;

    internal static Func<IReadOnlyList<string>> MountPointsReader { get; set; } = ReadMountPointsRegistry;

    internal static Func<IReadOnlyList<string>> ActiveConnectionsReader { get; set; } = ReadActiveConnections;

    internal static Func<long> ClockMs { get; set; } = static () => Environment.TickCount64;

    public static void Remember(string? path)
    {
        if (!UncPath.TryParseShareRoot(path, out var shareRoot))
        {
            return;
        }

        lock (Gate)
        {
            RememberedRoots.Add(shareRoot);
        }
    }

    public static void RememberAll(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        lock (Gate)
        {
            foreach (var path in paths)
            {
                if (UncPath.TryParseShareRoot(path, out var shareRoot))
                {
                    RememberedRoots.Add(shareRoot);
                }
            }
        }
    }

    public static IReadOnlyList<string> ListHosts(Action? onActiveRefreshed = null)
    {
        var roots = CollectAllRoots();
        TriggerActiveRefreshIfExpired(onActiveRefreshed);

        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (UncPath.GetHost(root) is { Length: > 0 } host)
            {
                hosts.Add(host);
            }
        }

        return SortDistinct(hosts);
    }

    public static IReadOnlyList<string> ListKnownShares(string server, Action? onActiveRefreshed = null)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            return [];
        }

        var normalizedServer = server.Trim().Trim('\\', '/');
        var roots = CollectAllRoots();
        TriggerActiveRefreshIfExpired(onActiveRefreshed);

        var shares = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            if (string.Equals(UncPath.GetHost(root), normalizedServer, StringComparison.OrdinalIgnoreCase)
                && UncPath.GetShare(root) is { Length: > 0 } share)
            {
                shares.Add(share);
            }
        }

        return SortDistinct(shares);
    }

    public static IReadOnlyList<string> MergeShares(string server, IEnumerable<string> enumeratedShares)
    {
        ArgumentNullException.ThrowIfNull(enumeratedShares);

        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var share in enumeratedShares.Where(static item => !string.IsNullOrWhiteSpace(item)))
        {
            merged.Add(share.Trim());
        }

        foreach (var known in ListKnownShares(server))
        {
            merged.Add(known);
        }

        return SortDistinct(merged);
    }

    internal static Task RefreshActiveAsync()
    {
        lock (Gate)
        {
            return StartActiveRefreshLocked(force: true, onActiveRefreshed: null);
        }
    }

    internal static string? ParseMountPointKey(string keyName)
    {
        if (string.IsNullOrEmpty(keyName) || !keyName.StartsWith("##", StringComparison.Ordinal))
        {
            return null;
        }

        var body = keyName[2..].Replace('#', '\\');
        return UncPath.TryParseShareRoot(@"\\" + body, out var root) ? root : null;
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            RememberedRoots.Clear();
            PendingCallbacks.Clear();
            _activeRoots = [];
            _activeUpdatedMs = long.MinValue;
            _hasActiveCache = false;
            _activeRefreshing = false;
            _activeRefreshTask = Task.CompletedTask;
        }

        PersistentReader = ReadPersistentRegistry;
        MountPointsReader = ReadMountPointsRegistry;
        ActiveConnectionsReader = ReadActiveConnections;
        ClockMs = static () => Environment.TickCount64;
    }

    private static List<string> CollectAllRoots()
    {
        var roots = new List<string>();

        lock (Gate)
        {
            roots.AddRange(RememberedRoots);
            roots.AddRange(_activeRoots);
        }

        AddNormalizedRoots(roots, SafeRead(PersistentReader));
        AddNormalizedRoots(roots, SafeRead(MountPointsReader));
        return roots;
    }

    private static void TriggerActiveRefreshIfExpired(Action? onActiveRefreshed)
    {
        lock (Gate)
        {
            _ = StartActiveRefreshLocked(force: false, onActiveRefreshed);
        }
    }

    private static Task StartActiveRefreshLocked(bool force, Action? onActiveRefreshed)
    {
        var ttlMs = AppDefaults.NetworkDiscoveryCacheSeconds * 1000L;
        var now = ClockMs();
        var fresh = !force && _hasActiveCache && (now - _activeUpdatedMs) < ttlMs;

        if (fresh)
        {
            return Task.CompletedTask;
        }

        if (onActiveRefreshed is not null)
        {
            PendingCallbacks.Add(onActiveRefreshed);
        }

        if (_activeRefreshing)
        {
            return _activeRefreshTask;
        }

        _activeRefreshing = true;
        var reader = ActiveConnectionsReader;
        var clock = ClockMs;
        _activeRefreshTask = Task.Run(() => RunActiveRefresh(reader, clock));
        return _activeRefreshTask;
    }

    private static void RunActiveRefresh(Func<IReadOnlyList<string>> reader, Func<long> clock)
    {
        var raw = SafeRead(reader);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in raw)
        {
            if (UncPath.TryParseShareRoot(path, out var shareRoot))
            {
                normalized.Add(shareRoot);
            }
        }

        var next = normalized.ToArray();
        Action[] callbacks;

        lock (Gate)
        {
            _activeRoots = next;
            _activeUpdatedMs = clock();
            _hasActiveCache = true;
            _activeRefreshing = false;
            callbacks = PendingCallbacks.ToArray();
            PendingCallbacks.Clear();
        }

        foreach (var callback in callbacks)
        {
            try
            {
                callback();
            }
            catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or OperationCanceledException)
            {
            }
        }
    }

    private static void AddNormalizedRoots(List<string> destination, IReadOnlyList<string> source)
    {
        foreach (var path in source)
        {
            if (UncPath.TryParseShareRoot(path, out var shareRoot))
            {
                destination.Add(shareRoot);
            }
        }
    }

    private static IReadOnlyList<string> SafeRead(Func<IReadOnlyList<string>> reader)
    {
        try
        {
            return reader();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ExternalException or ArgumentException)
        {
            return [];
        }
    }

    private static string[] SortDistinct(HashSet<string> values)
    {
        if (values.Count == 0)
        {
            return [];
        }

        var list = values.ToArray();
        Array.Sort(list, StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private static IReadOnlyList<string> ReadPersistentRegistry()
    {
        using var network = Registry.CurrentUser.OpenSubKey("Network");

        if (network is null)
        {
            return [];
        }

        var roots = new List<string>();

        foreach (var name in network.GetSubKeyNames())
        {
            if (name.Length != 1 || !char.IsAsciiLetter(name[0]))
            {
                continue;
            }

            using var key = network.OpenSubKey(name);

            if (key?.GetValue("RemotePath") is string remotePath && UncPath.TryParseShareRoot(remotePath, out var root))
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    private static IReadOnlyList<string> ReadMountPointsRegistry()
    {
        using var mountPoints = Registry.CurrentUser.OpenSubKey(MountPointsSubKey);

        if (mountPoints is null)
        {
            return [];
        }

        var roots = new List<string>();

        foreach (var name in mountPoints.GetSubKeyNames())
        {
            if (ParseMountPointKey(name) is { } root)
            {
                roots.Add(root);
            }
        }

        return roots;
    }

    private static IReadOnlyList<string> ReadActiveConnections()
    {
        if (WNetOpenEnumW(ResourceConnected, ResourceTypeDisk, 0, IntPtr.Zero, out var handle) != NoError)
        {
            return [];
        }

        var bufferSize = InitialEnumBufferBytes;
        var buffer = Marshal.AllocHGlobal(bufferSize);
        var entrySize = Marshal.SizeOf<NetResource>();
        var roots = new List<string>();

        try
        {
            while (true)
            {
                var count = -1;
                var result = WNetEnumResourceW(handle, ref count, buffer, ref bufferSize);

                if (result == ErrorMoreData)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = Marshal.AllocHGlobal(bufferSize);
                    continue;
                }

                if (result == NoError && count > 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        var entry = Marshal.PtrToStructure<NetResource>(buffer + (i * entrySize));
                        var remote = entry.lpRemoteName != IntPtr.Zero
                            ? Marshal.PtrToStringUni(entry.lpRemoteName)
                            : null;

                        if (UncPath.TryParseShareRoot(remote, out var root))
                        {
                            roots.Add(root);
                        }
                    }

                    continue;
                }

                if (result == ErrorNoMoreItems || result != NoError || count <= 0)
                {
                    break;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            _ = WNetCloseEnum(handle);
        }

        return roots;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern uint WNetOpenEnumW(
        uint dwScope,
        uint dwType,
        uint dwUsage,
        IntPtr lpNetResource,
        out IntPtr lphEnum);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern uint WNetEnumResourceW(
        IntPtr hEnum,
        ref int lpcCount,
        IntPtr lpBuffer,
        ref int lpBufferSize);

    [DllImport("mpr.dll")]
    private static extern uint WNetCloseEnum(IntPtr hEnum);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint dwScope;
        public uint dwType;
        public uint dwDisplayType;
        public uint dwUsage;
        public IntPtr lpLocalName;
        public IntPtr lpRemoteName;
        public IntPtr lpComment;
        public IntPtr lpProvider;
    }
}
