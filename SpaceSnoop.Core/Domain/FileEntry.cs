using SpaceSnoop.Core.Mft;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Domain;

[StructLayout(LayoutKind.Sequential)]
internal struct FileEntry
{
    public const byte UtcFileTimeFlag = 1;

    public long Size;
    public long CreationRaw;
    public long LastAccessRaw;
    public int NameId;
    public SpaceState State;
    public byte Flags;

    public readonly bool IsDeleted => State == SpaceState.Deleted;

    public readonly DateTime GetCreationDate()
    {
        return TimestampEncoding.ToDateTime(CreationRaw, Flags);
    }

    public readonly DateTime GetLastAccessTime()
    {
        return TimestampEncoding.ToDateTime(LastAccessRaw, Flags);
    }
}

internal static class TimestampEncoding
{
    public const long FileTimeEpochTicks = 504_911_232_000_000_000L;
    private const long TicksMask = 0x3FFF_FFFF_FFFF_FFFFL;

    public static long FromDateTime(DateTime value)
    {
        return (value.Ticks & TicksMask) | ((long)value.Kind << 62);
    }

    public static long FromDateTimeOffset(DateTimeOffset utc, out byte flag)
    {
        if (utc.UtcTicks > FileTimeEpochTicks)
        {
            flag = FileEntry.UtcFileTimeFlag;
            return utc.UtcTicks - FileTimeEpochTicks;
        }

        flag = 0;
        return FromDateTime(utc.LocalDateTime);
    }

    public static void FromDateTimeOffsets(
        DateTimeOffset creationUtc,
        DateTimeOffset lastAccessUtc,
        out long creationRaw,
        out long lastAccessRaw,
        out byte flags)
    {
        if (creationUtc.UtcTicks > FileTimeEpochTicks && lastAccessUtc.UtcTicks > FileTimeEpochTicks)
        {
            creationRaw = creationUtc.UtcTicks - FileTimeEpochTicks;
            lastAccessRaw = lastAccessUtc.UtcTicks - FileTimeEpochTicks;
            flags = FileEntry.UtcFileTimeFlag;
            return;
        }

        creationRaw = FromDateTime(creationUtc.LocalDateTime);
        lastAccessRaw = FromDateTime(lastAccessUtc.LocalDateTime);
        flags = 0;
    }

    public static long FromMftFileTime(long fileTime)
    {
        return fileTime;
    }

    public static DateTime ToDateTime(long raw, byte flags)
    {
        if ((flags & FileEntry.UtcFileTimeFlag) != 0)
        {
            return MftLayout.ToDateTime(raw);
        }

        return new(raw & TicksMask, (DateTimeKind)((ulong)raw >> 62));
    }
}
