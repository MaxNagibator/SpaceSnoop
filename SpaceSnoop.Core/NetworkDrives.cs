using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core;

public sealed record PersistentDrive(string LocalName, string RemotePath, string? UserName, string? ProviderName);

public sealed record DriveRestoreResult(PersistentDrive Drive, int ErrorCode)
{
    public bool Succeeded => ErrorCode == 0;
}

public static class NetworkDrives
{
    private const string NetworkKey = "Network";
    private const uint ResourceTypeDisk = 1;
    private const int ErrorDeviceAlreadyRemembered = 1202;

    public static Task<IReadOnlyList<DriveRestoreResult>> RestoreAsync(bool isElevated, ILogger logger)
    {
        return RestoreAsync(isElevated, ReadPersistent, PresentDriveNames, Connect, logger);
    }

    internal static async Task<IReadOnlyList<DriveRestoreResult>> RestoreAsync(
        bool isElevated,
        Func<IReadOnlyList<PersistentDrive>> readPersistent,
        Func<IEnumerable<string>> presentDrives,
        Func<PersistentDrive, int> connect,
        ILogger logger)
    {
        if (!isElevated)
        {
            return [];
        }

        var missing = await Task.Run(() => SelectMissing(readPersistent(), presentDrives())).ConfigureAwait(false);

        if (missing.Count == 0)
        {
            return [];
        }

        return await Task.WhenAll(missing.Select(drive => Task.Run(() => Restore(drive, connect, logger)))).ConfigureAwait(false);
    }

    internal static PersistentDrive? Parse(string keyName, Func<string, string?> readValue)
    {
        if (keyName.Length != 1 || !char.IsAsciiLetter(keyName[0]))
        {
            return null;
        }

        var remotePath = readValue("RemotePath");

        if (string.IsNullOrWhiteSpace(remotePath))
        {
            return null;
        }

        return new(
            $"{char.ToUpperInvariant(keyName[0])}:",
            remotePath,
            NullIfEmpty(readValue("UserName")),
            NullIfEmpty(readValue("ProviderName")));
    }

    internal static IReadOnlyList<PersistentDrive> SelectMissing(IEnumerable<PersistentDrive> persistent, IEnumerable<string> presentDrives)
    {
        var taken = presentDrives
            .Where(name => name.Length > 0)
            .Select(name => char.ToUpperInvariant(name[0]))
            .ToHashSet();

        return persistent.Where(drive => !taken.Contains(drive.LocalName[0])).ToList();
    }

    internal static DriveRestoreResult Restore(PersistentDrive drive, Func<PersistentDrive, int> connect, ILogger logger)
    {
        var code = connect(drive);

        if (code == 0)
        {
            logger.NetworkDriveRestored(drive.LocalName, drive.RemotePath);
        }
        else
        {
            logger.NetworkDriveRestoreFailed(drive.LocalName, drive.RemotePath, code, new Win32Exception(code).Message);
        }

        return new(drive, code);
    }

    internal static IReadOnlyList<PersistentDrive> ReadPersistent()
    {
        using var network = Registry.CurrentUser.OpenSubKey(NetworkKey);

        if (network is null)
        {
            return [];
        }

        var drives = new List<PersistentDrive>();

        foreach (var name in network.GetSubKeyNames())
        {
            using var key = network.OpenSubKey(name);

            if (key is not null && Parse(name, value => key.GetValue(value) as string) is { } drive)
            {
                drives.Add(drive);
            }
        }

        return drives;
    }

    internal static IEnumerable<string> PresentDriveNames()
    {
        return DriveInfo.GetDrives().Select(drive => drive.Name);
    }

    internal static int Connect(PersistentDrive drive)
    {
        var resource = new NetResource
        {
            dwType = ResourceTypeDisk,
            lpLocalName = drive.LocalName,
            lpRemoteName = drive.RemotePath,
            lpProvider = drive.ProviderName,
        };

        var code = WNetAddConnection2(ref resource, null, drive.UserName, 0);

        return code == ErrorDeviceAlreadyRemembered
            ? WNetRestoreSingleConnection(IntPtr.Zero, drive.LocalName, false)
            : code;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource lpNetResource, string? lpPassword, string? lpUserName, uint dwFlags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetRestoreSingleConnectionW")]
    private static extern int WNetRestoreSingleConnection(IntPtr hwndParent, string lpDevice, [MarshalAs(UnmanagedType.Bool)] bool fUseUI);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public uint dwScope;
        public uint dwType;
        public uint dwDisplayType;
        public uint dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }
}
