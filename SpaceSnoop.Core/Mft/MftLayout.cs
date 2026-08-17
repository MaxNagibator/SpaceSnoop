using System.Buffers.Binary;

namespace SpaceSnoop.Core.Mft;

internal static class MftLayout
{
    public const uint ReparseNameSurrogate = 0x20000000;

    public const int RootRecord = 5;
    public const int FirstUserRecord = 16;

    public const uint AttributeStandardInformation = 0x10;
    public const uint AttributeFileName = 0x30;
    public const uint AttributeData = 0x80;
    public const uint AttributeReparsePoint = 0xC0;
    public const uint AttributeEnd = 0xFFFFFFFF;

    public const ushort RecordInUse = 0x0001;
    public const ushort RecordIsDirectory = 0x0002;

    public const int RecordSequenceOffset = 0x10;
    public const int ReferenceMaskBits = 48;
    public const long ReferenceMask = (1L << ReferenceMaskBits) - 1;

    public const byte NamespaceDos = 2;

    public static bool IsNtfs(ReadOnlySpan<byte> boot)
    {
        return boot.Length >= 11 && "NTFS    "u8.SequenceEqual(boot.Slice(3, 8));
    }

    public static bool ApplyFixup(Span<byte> record, int bytesPerSector)
    {
        var offset = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        var count = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);

        if (count == 0 || offset + count * 2 > record.Length)
        {
            return false;
        }

        var signature = record.Slice(offset, 2);

        for (var index = 1; index < count; index++)
        {
            var end = index * bytesPerSector - 2;

            if (end < 0 || end + 2 > record.Length)
            {
                return false;
            }

            var tail = record.Slice(end, 2);

            if (!tail.SequenceEqual(signature))
            {
                return false;
            }

            record.Slice(offset + index * 2, 2).CopyTo(tail);
        }

        return true;
    }

    public static List<MftRun> DecodeRuns(ReadOnlySpan<byte> data)
    {
        return DecodeRuns(data, out _);
    }

    public static List<MftRun> DecodeRuns(ReadOnlySpan<byte> data, out bool truncated)
    {
        var runs = new List<MftRun>();
        var position = 0;
        long cluster = 0;
        truncated = false;

        while (position < data.Length && data[position] != 0)
        {
            var header = data[position++];
            var countSize = header & 0x0F;
            var offsetSize = (header >> 4) & 0x0F;

            if (countSize == 0 || position + countSize + offsetSize > data.Length)
            {
                truncated = true;
                break;
            }

            var count = ReadSigned(data.Slice(position, countSize));
            position += countSize;

            if (offsetSize == 0)
            {
                continue;
            }

            cluster += ReadSigned(data.Slice(position, offsetSize));
            position += offsetSize;

            if (cluster < 0 || count <= 0)
            {
                truncated = true;
                break;
            }

            runs.Add(new(cluster, count));
        }

        return runs;
    }

    public static DateTime ToDateTime(long fileTime)
    {
        if (fileTime <= 0)
        {
            return default;
        }

        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return default;
        }
    }

    private static long ReadSigned(ReadOnlySpan<byte> data)
    {
        long value = 0;

        for (var index = data.Length - 1; index >= 0; index--)
        {
            value = (value << 8) | data[index];
        }

        var bits = data.Length * 8;

        if (bits < 64 && (value & (1L << (bits - 1))) != 0)
        {
            value -= 1L << bits;
        }

        return value;
    }
}

internal readonly record struct MftRun(long Cluster, long Count);
