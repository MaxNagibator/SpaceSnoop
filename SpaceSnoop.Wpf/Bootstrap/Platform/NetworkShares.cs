using System.Runtime.InteropServices;

namespace SpaceSnoop.Wpf.Bootstrap.Platform;

public static class NetworkShares
{
    private const int MaxPreferredLength = -1;
    private const uint StypeDiskTree = 0;
    private const uint StypeMask = 0x0FFFFFFF;
    private const uint StypeSpecial = 0x80000000;

    public static IReadOnlyList<string> List(string server)
    {
        var buffer = IntPtr.Zero;
        var resume = 0;

        try
        {
            if (NetShareEnum($@"\\{server}", 1, out buffer, MaxPreferredLength, out var read, out _, ref resume) != 0)
            {
                return [];
            }

            var shares = new List<string>(read);
            var size = Marshal.SizeOf<ShareInfo1>();

            for (var index = 0; index < read; index++)
            {
                var entry = Marshal.PtrToStructure<ShareInfo1>(buffer + (index * size));

                if ((entry.Type & StypeMask) == StypeDiskTree && (entry.Type & StypeSpecial) == 0)
                {
                    shares.Add(entry.NetName);
                }
            }

            return shares;
        }
        catch (Exception exception) when (exception is ExternalException or ArgumentException)
        {
            return [];
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                NetApiBufferFree(buffer);
            }
        }
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetShareEnum(
        string serverName,
        int level,
        out IntPtr buffer,
        int preferredMaxLength,
        out int entriesRead,
        out int totalEntries,
        ref int resumeHandle);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShareInfo1
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string NetName;

        public uint Type;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string Remark;
    }
}
