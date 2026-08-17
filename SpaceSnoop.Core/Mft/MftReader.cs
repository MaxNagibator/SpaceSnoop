using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal static class MftReader
{
    private const int BlockBytes = 4 * 1024 * 1024;
    private const int ReadThreadCeiling = 8;

    private const int ResidentHeaderBytes = 0x16;
    private const int NonResidentHeaderBytes = 0x38;
    private const int FileNameHeaderBytes = 0x42;

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
            using var volume = MftFileVolume.Open(letter.Value);
            var boot = new byte[512];
            volume.ReadAt(0, boot);
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

    // TODO: том читается на ходу, без теневой копии, поэтому запись, изменённая после прохода своего
    // участка, приезжает в устаревшем виде, а выросший за время чтения $MFT остаётся недочитанным.
    // Триггер апгрейда: расхождения начнут попадаться в отчётах – тогда снимать VSS-снимок (`IVssBackupComponents`)
    // и читать его, ценой прав и времени на создание снимка.
    // TODO: таблица читается через системный кэш, поэтому её объём вытесняет оттуда рабочий набор
    // пользователя, а число потоков упирается в путь тома, а не в счёт (замер – 1,6…1,8 ГБ/с при
    // любом их числе). Триггер апгрейда: чтение перестанет упираться в том – тогда переходить на
    // `FILE_FLAG_NO_BUFFERING` с выровненными буферами, в один поток он дал 1,75 против 1,60 ГБ/с.
    public static MftTable Read(char letter, ScanProgress? progress, CancellationToken cancel)
    {
        using var volume = MftFileVolume.Open(letter);

        return Read(volume, letter, ResolveThreads(letter), BlockBytes, progress, cancel);
    }

    public static int ResolveThreads(char letter)
    {
        return StorageMedia.LimitParallelism($@"{letter}:\", Math.Min(Environment.ProcessorCount, ReadThreadCeiling));
    }

    internal static MftTable Read(
        IMftVolume volume,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        var boot = new byte[512];
        volume.ReadAt(0, boot);

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

        if (recordSize < bytesPerSector || recordSize > BlockBytes)
        {
            throw new InvalidDataException($"Том {letter}: непригодный размер записи $MFT ({recordSize})");
        }

        if (mftCluster <= 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодное положение $MFT ({mftCluster})");
        }

        var self = new byte[recordSize];
        volume.ReadAt(mftCluster * bytesPerCluster, self);

        if (!"FILE"u8.SequenceEqual(self.AsSpan(0, 4)) || !MftLayout.ApplyFixup(self, bytesPerSector))
        {
            throw new InvalidDataException($"Том {letter}: собственная запись $MFT не читается как запись NTFS");
        }

        var (runs, mftBytes) = ReadSelfRuns(self, letter);

        if (mftBytes <= 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодный размер $MFT ({mftBytes})");
        }

        var total = mftBytes / recordSize;

        if (total > int.MaxValue)
        {
            throw new InvalidDataException($"Том {letter}: в $MFT {total:N0} записей, столько за один проход не читается");
        }

        var records = (int)total;
        var map = new MftMap(runs, bytesPerCluster);
        var needed = (long)records * recordSize;

        if (map.Length < needed)
        {
            throw new InvalidDataException(
                $"Том {letter}: отрезки $MFT покрывают {map.Length:N0} Б из {needed:N0} Б, разметка неполна");
        }

        var entries = new MftEntry[records];
        var blockRecords = Math.Max(1, blockBytes / recordSize);
        var blocks = (records + blockRecords - 1) / blockRecords;
        var parts = new MftPart[blocks];
        var completed = 0;

        var options = new ParallelOptions
        {
            CancellationToken = cancel,
            MaxDegreeOfParallelism = Math.Max(1, threads),
        };

        try
        {
            Parallel.For(
                0,
                blocks,
                options,
                () => new MftWorker(volume.Reopen(), blockRecords * recordSize),
                (block, _, worker) =>
                {
                    var start = block * blockRecords;
                    var count = Math.Min(blockRecords, records - start);
                    var part = new MftPart();
                    var buffer = worker.Buffer.AsSpan(0, count * recordSize);

                    map.Read(worker.Volume, (long)start * recordSize, buffer);

                    for (var offset = 0; offset < count; offset++)
                    {
                        Parse(
                            buffer.Slice(offset * recordSize, recordSize),
                            bytesPerSector,
                            start + offset,
                            entries,
                            part.Pending,
                            part.Statistics);
                    }

                    parts[block] = part;
                    progress?.Announce($"Чтение $MFT тома {letter}: {Interlocked.Increment(ref completed) * 100L / blocks} %");

                    return worker;
                },
                x => x.Dispose());
        }
        catch (AggregateException exception)
        {
            ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
        }

        var pending = new MftPending();
        var statistics = new MftStatistics();

        Merge(parts, pending, statistics);
        ApplyPending(entries, pending, statistics);

        return new(entries, statistics, pending.Alternates);
    }

    internal static void ApplyPending(MftEntry[] entries, MftPending pending, MftStatistics statistics)
    {
        foreach (var (index, size) in pending.Sizes)
        {
            if (index >= 0 && index < entries.Length && entries[index].Present && !entries[index].SizeKnown)
            {
                entries[index].Size = size;
                entries[index].SizeKnown = true;
            }
        }

        foreach (var (index, names) in pending.Alternates)
        {
            if (index < 0 || index >= entries.Length || !entries[index].Present || names.Count == 0)
            {
                continue;
            }

            ref var entry = ref entries[index];

            if (entry.Name is null)
            {
                var chosen = names[0];
                names.RemoveAt(0);
                entry.Parent = chosen.Parent;
                entry.ParentSequence = chosen.ParentSequence;
                entry.Name = chosen.Name;
            }

            entry.Names = names.Count + 1;

            if (entry.Names > 1)
            {
                statistics.HardLinkedFiles++;
            }
        }

        for (var index = MftLayout.FirstUserRecord; index < entries.Length; index++)
        {
            if (entries[index].Present && entries[index].Name is null)
            {
                statistics.Nameless++;
            }
        }
    }

    internal static void Parse(
        Span<byte> record,
        int bytesPerSector,
        int index,
        MftEntry[] entries,
        MftPending pending,
        MftStatistics statistics)
    {
        statistics.RecordsScanned++;

        if (!"FILE"u8.SequenceEqual(record[..4]))
        {
            if (record.IndexOfAnyExcept((byte)0) >= 0)
            {
                statistics.Damaged++;
            }

            return;
        }

        if (!MftLayout.ApplyFixup(record, bytesPerSector))
        {
            statistics.Damaged++;
            return;
        }

        var flags = BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);

        if ((flags & MftLayout.RecordInUse) == 0)
        {
            return;
        }

        statistics.RecordsInUse++;

        var reference = BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]);
        var baseReference = (long)(reference & MftLayout.ReferenceMask);

        if (baseReference != 0)
        {
            statistics.Extensions++;

            if (baseReference > int.MaxValue)
            {
                statistics.Damaged++;
                return;
            }

            ParseExtension(record, (int)baseReference, pending, statistics);
            return;
        }

        var entry = new MftEntry
        {
            IsDirectory = (flags & MftLayout.RecordIsDirectory) != 0,
            Parent = -1,
            Names = 1,
            Present = true,
            Sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[MftLayout.RecordSequenceOffset..]),
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

            if (length <= 0 || position + length > record.Length || length < ResidentHeaderBytes)
            {
                statistics.Damaged++;
                break;
            }

            var attribute = record.Slice(position, length);
            var nonResident = attribute[0x08] != 0;

            if (type == MftLayout.AttributeReparsePoint)
            {
                entry.ReparseUnknown = true;
            }

            if (!nonResident)
            {
                ReadResident(attribute, type, ref entry, ref bestNamespace, ref names);
            }

            if (type == MftLayout.AttributeData && attribute[0x09] == 0 && !entry.SizeKnown)
            {
                ReadDataSize(attribute, nonResident, ref entry, statistics);
            }

            position += length;
        }

        if (names > 1 || !entry.Exists)
        {
            CollectAlternates(record, index, entry, pending);
        }

        entries[index] = entry;
    }

    private static void Merge(MftPart[] parts, MftPending pending, MftStatistics statistics)
    {
        foreach (var part in parts)
        {
            statistics.Add(part.Statistics);

            foreach (var (index, size) in part.Pending.Sizes)
            {
                pending.Sizes[index] = size;
            }

            foreach (var (index, names) in part.Pending.Alternates)
            {
                if (pending.Alternates.TryGetValue(index, out var existing))
                {
                    existing.AddRange(names);
                }
                else
                {
                    pending.Alternates[index] = names;
                }
            }
        }
    }

    private static void ReadDataSize(Span<byte> attribute, bool nonResident, ref MftEntry entry, MftStatistics statistics)
    {
        long size;

        if (nonResident)
        {
            if (attribute.Length < NonResidentHeaderBytes)
            {
                statistics.Damaged++;
                return;
            }

            if (BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) != 0)
            {
                return;
            }

            size = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
        }
        else
        {
            size = BinaryPrimitives.ReadUInt32LittleEndian(attribute[0x10..]);
        }

        if (size < 0)
        {
            statistics.Damaged++;
            return;
        }

        entry.Size = size;
        entry.SizeKnown = true;
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

            case MftLayout.AttributeFileName when value.Length >= FileNameHeaderBytes:
                var candidateNamespace = value[0x41];
                var candidateLength = value[0x40];

                if (candidateNamespace == MftLayout.NamespaceDos || value.Length < FileNameHeaderBytes + candidateLength * 2)
                {
                    break;
                }

                names++;

                if (candidateNamespace < bestNamespace)
                {
                    bestNamespace = candidateNamespace;
                    var parent = BinaryPrimitives.ReadUInt64LittleEndian(value);
                    var record = (long)(parent & MftLayout.ReferenceMask);
                    entry.Parent = record <= int.MaxValue ? (int)record : -1;
                    entry.ParentSequence = (ushort)(parent >> MftLayout.ReferenceMaskBits);
                    entry.Name = MemoryMarshal.Cast<byte, char>(value.Slice(FileNameHeaderBytes, candidateLength * 2)).ToString();
                }

                break;

            case MftLayout.AttributeReparsePoint when value.Length >= 4:
                entry.ReparseTag = BinaryPrimitives.ReadUInt32LittleEndian(value);
                entry.ReparseUnknown = false;
                break;

            default:
                break;
        }
    }

    private static void ParseExtension(Span<byte> record, int baseIndex, MftPending pending, MftStatistics statistics)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                return;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || position + length > record.Length || length < ResidentHeaderBytes)
            {
                statistics.Damaged++;
                return;
            }

            var attribute = record.Slice(position, length);

            if (type == MftLayout.AttributeFileName && attribute[0x08] == 0)
            {
                AddName(attribute, baseIndex, pending);
            }
            else if (type == MftLayout.AttributeData
                     && attribute[0x09] == 0
                     && attribute[0x08] != 0
                     && attribute.Length >= NonResidentHeaderBytes
                     && BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) == 0)
            {
                var size = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);

                if (size < 0)
                {
                    statistics.Damaged++;
                }
                else
                {
                    pending.Sizes[baseIndex] = size;
                }
            }

            position += length;
        }
    }

    private static void CollectAlternates(Span<byte> record, int index, MftEntry entry, MftPending pending)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while (position + 8 <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                return;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || position + length > record.Length || length < ResidentHeaderBytes)
            {
                return;
            }

            var attribute = record.Slice(position, length);

            if (type == MftLayout.AttributeFileName && attribute[0x08] == 0)
            {
                AddName(attribute, index, pending, entry);
            }

            position += length;
        }
    }

    private static void AddName(Span<byte> attribute, int index, MftPending pending, MftEntry chosen = default)
    {
        var valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]);

        if (valueOffset >= attribute.Length)
        {
            return;
        }

        var value = attribute[valueOffset..];

        if (value.Length < FileNameHeaderBytes || value[0x41] == MftLayout.NamespaceDos)
        {
            return;
        }

        var nameLength = value[0x40];

        if (value.Length < FileNameHeaderBytes + nameLength * 2)
        {
            return;
        }

        var reference = BinaryPrimitives.ReadUInt64LittleEndian(value);
        var record = (long)(reference & MftLayout.ReferenceMask);

        if (record > int.MaxValue)
        {
            return;
        }

        var name = MemoryMarshal.Cast<byte, char>(value.Slice(FileNameHeaderBytes, nameLength * 2)).ToString();

        if (chosen.Exists && chosen.Parent == (int)record && string.Equals(chosen.Name, name, StringComparison.Ordinal))
        {
            return;
        }

        var alternates = pending.Alternates.TryGetValue(index, out var existing) ? existing : pending.Alternates[index] = [];
        alternates.Add(new((int)record, (ushort)(reference >> MftLayout.ReferenceMaskBits), name));
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
                if (length < NonResidentHeaderBytes)
                {
                    throw new InvalidDataException($"Том {letter}: заголовок $DATA записи $MFT обрезан");
                }

                var attribute = record.AsSpan(position, length);
                var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);

                if (runOffset >= length)
                {
                    throw new InvalidDataException($"Том {letter}: runlist записи $MFT лежит вне атрибута");
                }

                var realSize = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
                var runs = MftLayout.DecodeRuns(attribute[runOffset..], out var truncated);

                if (truncated || runs.Count == 0)
                {
                    throw new InvalidDataException($"Том {letter}: runlist записи $MFT неполон");
                }

                return (runs, realSize);
            }

            position += length;
        }

        throw new InvalidDataException($"Том {letter}: в записи $MFT нет нерезидентного $DATA");
    }

}

internal sealed class MftPending
{
    public Dictionary<int, long> Sizes { get; } = [];

    public Dictionary<int, List<MftName>> Alternates { get; } = [];
}

internal sealed class MftPart
{
    public MftPending Pending { get; } = new();

    public MftStatistics Statistics { get; } = new();
}

internal sealed class MftWorker(IMftVolume volume, int bytes) : IDisposable
{
    public IMftVolume Volume { get; } = volume;

    public byte[] Buffer { get; } = ArrayPool<byte>.Shared.Rent(bytes);

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(Buffer);
        Volume.Dispose();
    }
}

internal sealed class MftMap
{
    private readonly int _bytesPerCluster;
    private readonly MftRun[] _runs;
    private readonly long[] _starts;

    public long Length { get; }

    public MftMap(List<MftRun> runs, int bytesPerCluster)
    {
        _bytesPerCluster = bytesPerCluster;
        _runs = [.. runs];
        _starts = new long[_runs.Length];

        var total = 0L;

        for (var index = 0; index < _runs.Length; index++)
        {
            _starts[index] = total;
            total += _runs[index].Count * bytesPerCluster;
        }

        Length = total;
    }

    public void Read(IMftVolume volume, long logical, Span<byte> buffer)
    {
        if (logical < 0 || logical + buffer.Length > Length)
        {
            throw new EndOfStreamException($"Чтение $MFT вышло за разметку: {logical:N0} + {buffer.Length:N0} Б при длине {Length:N0} Б");
        }

        var written = 0;

        while (written < buffer.Length)
        {
            var position = logical + written;
            var index = Find(position);
            var inside = position - _starts[index];
            var take = (int)Math.Min(buffer.Length - written, _runs[index].Count * _bytesPerCluster - inside);

            volume.ReadAt(_runs[index].Cluster * _bytesPerCluster + inside, buffer.Slice(written, take));
            written += take;
        }
    }

    private int Find(long position)
    {
        var index = Array.BinarySearch(_starts, position);

        return index >= 0 ? index : ~index - 1;
    }
}
