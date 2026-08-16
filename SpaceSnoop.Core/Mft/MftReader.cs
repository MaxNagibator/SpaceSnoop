using Microsoft.Win32.SafeHandles;
using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal static class MftReader
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const int ErrorAccessDenied = 5;

    private const int ReadBufferBytes = 8 * 1024 * 1024;

    public static MftAvailability Probe(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return MftAvailability.NotNtfs;
        }

        var letter = VolumeLetter(path);

        if (letter is null)
        {
            return MftAvailability.NotFixedVolume;
        }

        try
        {
            var drive = new DriveInfo(letter.Value.ToString());

            if (drive.DriveType is DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory)
            {
                return MftAvailability.NotFixedVolume;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return MftAvailability.NotFixedVolume;
        }

        try
        {
            using var volume = OpenVolume(letter.Value);
            var boot = new byte[512];
            ReadAt(volume, 0, boot);
            return MftLayout.IsNtfs(boot) ? MftAvailability.Available : MftAvailability.NotNtfs;
        }
        catch (UnauthorizedAccessException)
        {
            return MftAvailability.AccessDenied;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            return MftAvailability.Failed;
        }
    }

    public static char? VolumeLetter(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));

        if (root is null || root.Length < 2 || root[1] != ':')
        {
            return null;
        }

        var letter = char.ToUpperInvariant(root[0]);
        return letter is >= 'A' and <= 'Z' ? letter : null;
    }

    public static MftTable Read(char letter, CancellationToken cancel)
    {
        using var volume = OpenVolume(letter);

        var boot = new byte[512];
        ReadAt(volume, 0, boot);

        if (!MftLayout.IsNtfs(boot))
        {
            throw new InvalidDataException($"Том {letter}: не NTFS");
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot.AsSpan(0x0B));
        var sectorsPerCluster = boot[0x0D];

        if (bytesPerSector == 0 || sectorsPerCluster == 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодный загрузочный сектор");
        }

        var bytesPerCluster = bytesPerSector * sectorsPerCluster;
        var mftCluster = BinaryPrimitives.ReadInt64LittleEndian(boot.AsSpan(0x30));
        var recordSizeRaw = (sbyte)boot[0x40];
        var recordSize = recordSizeRaw > 0 ? recordSizeRaw * bytesPerCluster : 1 << -recordSizeRaw;

        if (recordSize < bytesPerSector || recordSize > ReadBufferBytes)
        {
            throw new InvalidDataException($"Том {letter}: непригодный размер записи $MFT ({recordSize})");
        }

        var first = new byte[recordSize];
        ReadAt(volume, mftCluster * bytesPerCluster, first);
        MftLayout.ApplyFixup(first, bytesPerSector);

        var (runs, mftBytes) = ReadSelfRuns(first, letter);
        var records = (int)Math.Min(mftBytes / recordSize, int.MaxValue);

        var entries = new MftEntry[records];
        var pending = new Dictionary<int, long>();
        var pendingNames = new Dictionary<int, int>();
        var statistics = new MftStatistics();

        var buffer = ArrayPool<byte>.Shared.Rent(ReadBufferBytes / recordSize * recordSize);

        try
        {
            var index = 0;

            foreach (var run in runs)
            {
                var offset = run.Cluster * bytesPerCluster;
                var remaining = run.Count * (long)bytesPerCluster;

                while (remaining > 0 && index < records)
                {
                    cancel.ThrowIfCancellationRequested();

                    var take = (int)Math.Min(remaining, buffer.Length);
                    ReadAt(volume, offset, buffer.AsSpan(0, take));

                    for (var position = 0; position + recordSize <= take && index < records; position += recordSize, index++)
                    {
                        Parse(buffer.AsSpan(position, recordSize), bytesPerSector, index, entries, pending, pendingNames, statistics);
                    }

                    offset += take;
                    remaining -= take;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        foreach (var (index, size) in pending)
        {
            if (index >= 0 && index < entries.Length && entries[index].Exists && !entries[index].SizeKnown)
            {
                entries[index].Size = size;
                entries[index].SizeKnown = true;
            }
        }

        foreach (var (index, names) in pendingNames)
        {
            if (index < 0 || index >= entries.Length || !entries[index].Exists)
            {
                continue;
            }

            statistics.ExtraNames += names;
            statistics.ExtraNameBytes += entries[index].Size * names;
        }

        return new(entries, statistics);
    }

    private static void Parse(
        Span<byte> record,
        int bytesPerSector,
        int index,
        MftEntry[] entries,
        Dictionary<int, long> pending,
        Dictionary<int, int> pendingNames,
        MftStatistics statistics)
    {
        statistics.RecordsScanned++;

        if (!"FILE"u8.SequenceEqual(record[..4]) || !MftLayout.ApplyFixup(record, bytesPerSector))
        {
            return;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);

        if ((flags & MftLayout.RecordInUse) == 0)
        {
            return;
        }

        statistics.RecordsInUse++;

        var baseReference = (long)(BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]) & 0x0000FFFFFFFFFFFF);

        if (baseReference != 0)
        {
            statistics.Extensions++;

            if (baseReference > int.MaxValue)
            {
                return;
            }

            if (TryReadDataSize(record, out var extensionSize))
            {
                pending[(int)baseReference] = extensionSize;
            }

            var extensionNames = CountNames(record);

            if (extensionNames > 0)
            {
                pendingNames[(int)baseReference] = pendingNames.GetValueOrDefault((int)baseReference) + extensionNames;
            }

            return;
        }

        var entry = new MftEntry
        {
            IsDirectory = (flags & MftLayout.RecordIsDirectory) != 0,
            Parent = -1,
        };

        var bestNamespace = byte.MaxValue;
        var names = 0;
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || position + length > record.Length)
            {
                break;
            }

            var attribute = record.Slice(position, length);
            var nonResident = attribute[0x08] != 0;

            if (!nonResident)
            {
                ReadResident(attribute, type, ref entry, ref bestNamespace, ref names);
            }

            if (type == MftLayout.AttributeData && attribute[0x09] == 0 && !entry.SizeKnown)
            {
                entry.Size = nonResident
                    ? BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..])
                    : BinaryPrimitives.ReadUInt32LittleEndian(attribute[0x10..]);

                entry.SizeKnown = true;
            }

            position += length;
        }

        if (!entry.Exists)
        {
            statistics.Nameless++;
            return;
        }

        if (names > 1)
        {
            statistics.HardLinkedFiles++;
            statistics.ExtraNames += names - 1;
            statistics.ExtraNameBytes += entry.Size * (names - 1);
        }

        entries[index] = entry;
    }

    private static void ReadResident(Span<byte> attribute, uint type, ref MftEntry entry, ref byte bestNamespace, ref int names)
    {
        var valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]);

        if (valueOffset >= attribute.Length)
        {
            return;
        }

        var value = attribute[valueOffset..];

        switch (type)
        {
            case MftLayout.AttributeStandardInformation when value.Length >= 0x20:
                entry.CreationTime = MftLayout.ToDateTime(BinaryPrimitives.ReadInt64LittleEndian(value));
                entry.LastAccessTime = MftLayout.ToDateTime(BinaryPrimitives.ReadInt64LittleEndian(value[0x18..]));
                break;

            case MftLayout.AttributeFileName when value.Length >= 0x42:
                var candidateNamespace = value[0x41];
                var candidateLength = value[0x40];

                if (candidateNamespace == MftLayout.NamespaceDos || value.Length < 0x42 + candidateLength * 2)
                {
                    break;
                }

                names++;

                if (candidateNamespace < bestNamespace)
                {
                    bestNamespace = candidateNamespace;
                    var parent = BinaryPrimitives.ReadUInt64LittleEndian(value) & 0x0000FFFFFFFFFFFF;
                    entry.Parent = parent <= int.MaxValue ? (int)parent : -1;
                    entry.Name = MemoryMarshal.Cast<byte, char>(value.Slice(0x42, candidateLength * 2)).ToString();
                }

                break;

            case MftLayout.AttributeReparsePoint when value.Length >= 4:
                entry.ReparseTag = BinaryPrimitives.ReadUInt32LittleEndian(value);
                break;

            default:
                break;
        }
    }

    private static int CountNames(Span<byte> record)
    {
        var names = 0;
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || position + length > record.Length)
            {
                break;
            }

            var attribute = record.Slice(position, length);

            if (type == MftLayout.AttributeFileName && attribute[0x08] == 0)
            {
                var valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]);

                if (valueOffset + 0x42 <= attribute.Length && attribute[valueOffset + 0x41] != MftLayout.NamespaceDos)
                {
                    names++;
                }
            }

            position += length;
        }

        return names;
    }

    private static bool TryReadDataSize(Span<byte> record, out long size)
    {
        size = 0;
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                return false;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || position + length > record.Length)
            {
                return false;
            }

            var attribute = record.Slice(position, length);

            if (type == MftLayout.AttributeData
                && attribute[0x09] == 0
                && attribute[0x08] != 0
                && BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) == 0)
            {
                size = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
                return true;
            }

            position += length;
        }

        return false;
    }

    private static (List<MftRun> Runs, long Bytes) ReadSelfRuns(byte[] record, char letter)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14));

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position));

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position + 4));

            if (length <= 0 || position + length > record.Length)
            {
                break;
            }

            if (type == MftLayout.AttributeData && record[position + 0x08] != 0)
            {
                var attribute = record.AsSpan(position, length);
                var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);
                var realSize = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
                return (MftLayout.DecodeRuns(attribute[runOffset..]), realSize);
            }

            position += length;
        }

        throw new InvalidDataException($"Том {letter}: в записи $MFT нет нерезидентного $DATA");
    }

    private static SafeFileHandle OpenVolume(char letter)
    {
        var handle = CreateFileW($@"\\.\{letter}:", GenericRead, ShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        throw error == ErrorAccessDenied
            ? new UnauthorizedAccessException($"Чтение $MFT тома {letter}: требует прав администратора")
            : new IOException($"Не удалось открыть том {letter}:, код {error}");
    }

    private static void ReadAt(SafeFileHandle handle, long offset, Span<byte> buffer)
    {
        var read = 0;

        while (read < buffer.Length)
        {
            var more = RandomAccess.Read(handle, buffer[read..], offset + read);

            if (more == 0)
            {
                throw new EndOfStreamException($"Чтение тома оборвалось на смещении {offset + read}");
            }

            read += more;
        }
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
}
