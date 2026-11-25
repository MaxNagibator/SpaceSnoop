using SpaceSnoop.Core.Domain;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal static class MftReader
{
    private const int BlockBytes = 4 * 1024 * 1024;
    private const int BufferAlignment = 4096;
    private const int ReadThreadCeiling = 8;

    private const int ResidentHeaderBytes = 0x16;
    private const int NonResidentHeaderBytes = 0x38;
    private const int FileNameHeaderBytes = 0x42;
    private const int AttributeListEntryBytes = 0x1A;
    private const long AttributeListLimitBytes = 1 << 20;

    public static MftAvailability Probe(string path)
    {
        var availability = TryOpenVolume(path, out _, out var volume, out _);
        volume?.Dispose();
        return availability;
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
    public static MftTable Read(char letter, ScanProgress? progress, CancellationToken cancel)
    {
        using var volume = MftFileVolume.Open(letter);

        return Read(volume, letter, ResolveThreads(letter), BlockBytes, progress, cancel);
    }

    public static int ResolveThreads(char letter)
    {
        return StorageMedia.LimitParallelism($@"{letter}:\", Math.Min(Environment.ProcessorCount, ReadThreadCeiling));
    }

    internal static MftAvailability TryOpenVolume(
        string path,
        out char letter,
        out MftFileVolume? volume,
        out byte[]? boot)
    {
        letter = default;
        volume = null;
        boot = null;

        if (!OperatingSystem.IsWindows())
        {
            return MftAvailability.NotNtfs;
        }

        var candidate = VolumeLetter(path);

        if (candidate is null)
        {
            return MftAvailability.NotFixedVolume;
        }

        letter = candidate.Value;

        try
        {
            var drive = new DriveInfo(letter.ToString());

            if (drive.DriveType is DriveType.Network or DriveType.CDRom or DriveType.NoRootDirectory)
            {
                return MftAvailability.NotFixedVolume;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return MftAvailability.NotFixedVolume;
        }

        MftFileVolume? opened = null;

        try
        {
            opened = MftFileVolume.Open(letter);
            var sector = new byte[MftRawDiskGuard.BootSectorBytes];
            opened.ReadAt(0, sector);

            if (!MftLayout.IsNtfs(sector))
            {
                opened.Dispose();
                return MftAvailability.NotNtfs;
            }

            volume = opened;
            opened = null;
            boot = sector;
            return MftAvailability.Available;
        }
        catch (UnauthorizedAccessException)
        {
            opened?.Dispose();
            return MftAvailability.AccessDenied;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            opened?.Dispose();
            return MftAvailability.Failed;
        }
    }

    internal static MftBootInfo ParseBoot(ReadOnlySpan<byte> boot, char letter)
    {
        if (boot.Length < MftRawDiskGuard.BootSectorBytes || !MftLayout.IsNtfs(boot))
        {
            throw new InvalidDataException($"Том {letter}: не NTFS");
        }

        var bytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(boot[0x0B..]);
        var sectorsPerCluster = boot[0x0D];

        if (bytesPerSector == 0 || sectorsPerCluster == 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодный загрузочный сектор");
        }

        var bytesPerCluster = bytesPerSector * sectorsPerCluster;
        var mftCluster = BinaryPrimitives.ReadInt64LittleEndian(boot[0x30..]);
        var recordSizeRaw = (sbyte)boot[0x40];
        var recordSize = 0;

        if (recordSizeRaw > 0)
        {
            recordSize = recordSizeRaw * bytesPerCluster;
        }
        else if (recordSizeRaw >= -30)
        {
            recordSize = 1 << -recordSizeRaw;
        }

        if (recordSize < bytesPerSector || recordSize > BlockBytes || recordSize % bytesPerSector != 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодный размер записи $MFT ({recordSize})");
        }

        if (mftCluster <= 0)
        {
            throw new InvalidDataException($"Том {letter}: непригодное положение $MFT ({mftCluster})");
        }

        return new(bytesPerSector, bytesPerCluster, mftCluster, recordSize);
    }

    internal static MftTable Read(
        IMftVolume volume,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        return Read(volume, letter, threads, blockBytes, progress, prefetchedBoot: null, partitionExtentLength: null, cancel);
    }

    internal static MftTable Read(
        IMftVolume volume,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        byte[]? prefetchedBoot,
        long? partitionExtentLength,
        CancellationToken cancel,
        long? minMftBytes = null)
    {
        cancel.ThrowIfCancellationRequested();

        byte[] boot;

        if (prefetchedBoot is null)
        {
            boot = new byte[MftRawDiskGuard.BootSectorBytes];
            volume.ReadAt(0, boot);
        }
        else
        {
            boot = prefetchedBoot;
        }

        var info = ParseBoot(boot, letter);
        var bytesPerSector = info.BytesPerSector;
        var bytesPerCluster = info.BytesPerCluster;
        var mftCluster = info.MftCluster;
        var recordSize = info.RecordSize;

        volume.SetSectorSize(bytesPerSector);

        var self = new byte[recordSize];
        volume.ReadAt(mftCluster * bytesPerCluster, self);

        if (partitionExtentLength is null)
        {
            progress?.NotifyVolumeReady();
        }

        if (!"FILE"u8.SequenceEqual(self.AsSpan(0, 4)) || !MftLayout.ApplyFixup(self, bytesPerSector))
        {
            throw new InvalidDataException($"Том {letter}: собственная запись $MFT не читается как запись NTFS");
        }

        var (runs, mftBytes) = ReadSelfRuns(self, volume, bytesPerCluster, recordSize, bytesPerSector, letter);

        if (partitionExtentLength is { } extentLength)
        {
            if (minMftBytes is { } expectedBytes && mftBytes < expectedBytes)
            {
                throw new InvalidDataException($"Том {letter}: размер $MFT на диске ({mftBytes:N0} Б) меньше длины NTFS ({expectedBytes:N0} Б)");
            }

            if (!MftRawDiskGuard.ValidateRunsWithinPartition(runs, extentLength, bytesPerCluster, out var runReason))
            {
                throw new InvalidDataException($"Том {letter}: {runReason}");
            }

            progress?.NotifyVolumeReady();
        }

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

        if (records == 0)
        {
            return new([], new(), [], new(1));
        }

        var map = new MftMap(runs, bytesPerCluster);
        var needed = (long)records * recordSize;

        if (map.Length < needed)
        {
            throw new InvalidDataException($"Том {letter}: отрезки $MFT покрывают {map.Length:N0} Б из {needed:N0} Б, разметка неполна");
        }

        const int nameTableShards = 64;
        var initialNameSlotsPerShard = Math.Clamp(
            (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(256, records / nameTableShards)),
            256,
            1 << 17);
        var store = ScanTreeStore.CreateWithNameSlots(nameTableShards, singleWriter: false, initialNameSlotsPerShard);
        var entries = GC.AllocateUninitializedArray<MftEntry>(records);
        var blockRecords = Math.Max(1, blockBytes / recordSize);
        var blocks = (records + blockRecords - 1) / blockRecords;
        var alignment = Math.Max((int)bytesPerSector, BufferAlignment);
        MftPart[] parts;

        if (threads <= 1)
        {
            var singlePart = ReadSingleWorkerAsync(
                volume,
                map,
                letter,
                records,
                blockRecords,
                blocks,
                recordSize,
                bytesPerSector,
                alignment,
                entries,
                store.Names,
                progress,
                cancel).GetAwaiter().GetResult();
            parts = [singlePart];
        }
        else
        {
            var workerCount = Math.Min(blocks, threads);
            parts = new MftPart[workerCount];
            var nextBlock = -1;
            var completed = 0;
            var options = new ParallelOptions
            {
                CancellationToken = cancel,
                MaxDegreeOfParallelism = workerCount,
            };

            try
            {
                Parallel.For(
                    0,
                    workerCount,
                    options,
                    workerIndex =>
                    {
                        var part = new MftPart();
                        parts[workerIndex] = part;
                        var buffer = new MftAlignedBuffer(blockRecords * recordSize, alignment);
                        var nameCache = LocalNameCache.Create();
                        var target = new MftPendingTarget(part, null);

                        while (true)
                        {
                            cancel.ThrowIfCancellationRequested();
                            var block = Interlocked.Increment(ref nextBlock);

                            if (block >= blocks)
                            {
                                break;
                            }

                            var start = block * blockRecords;
                            var count = Math.Min(blockRecords, records - start);
                            entries.AsSpan(start, count).Clear();
                            var span = buffer.Span(count * recordSize);

                            map.Read(volume, (long)start * recordSize, span);

                            for (var offset = 0; offset < count; offset++)
                            {
                                ParseCore(
                                    span.Slice(offset * recordSize, recordSize),
                                    bytesPerSector,
                                    start + offset,
                                    entries,
                                    target,
                                    part.Statistics,
                                    store.Names,
                                    nameCache);
                            }

                            progress?.Announce($"Чтение $MFT тома {letter}: {Interlocked.Increment(ref completed) * 100L / blocks} %");
                        }
                    });
            }
            catch (AggregateException exception)
            {
                ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
            }
        }

        var pending = new MftPending();
        var statistics = new MftStatistics();

        Merge(parts, pending, statistics);
        ApplyPending(entries, pending, statistics);

        return new(entries, statistics, pending.Alternates, store);
    }

    private static async Task<MftPart> ReadSingleWorkerAsync(
        IMftVolume volume,
        MftMap map,
        char letter,
        int records,
        int blockRecords,
        int blocks,
        int recordSize,
        int bytesPerSector,
        int alignment,
        MftEntry[] entries,
        NameTable nameTable,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        var part = new MftPart();
        var nameCache = LocalNameCache.Create();
        var target = new MftPendingTarget(part, null);
        var bufferBytes = blockRecords * recordSize;
        var buffers = new MftAlignedBuffer?[2];
        buffers[0] = new MftAlignedBuffer(bufferBytes, alignment);
        buffers[1] = blocks > 1 ? new MftAlignedBuffer(bufferBytes, alignment) : null;

        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        Task? pendingRead = null;

        try
        {
            var currentSlot = 0;
            var initialCount = Math.Min(blockRecords, records);
            pendingRead = map.ReadAsync(volume, 0, buffers[0]!.Memory(initialCount * recordSize), readCts.Token).AsTask();

            for (var block = 0; block < blocks; block++)
            {
                cancel.ThrowIfCancellationRequested();

                var ready = pendingRead;
                pendingRead = null;
                await ready!.ConfigureAwait(false);

                var start = block * blockRecords;
                var count = Math.Min(blockRecords, records - start);
                var next = block + 1;
                var nextSlot = currentSlot ^ 1;

                if (next < blocks)
                {
                    var nextStart = next * blockRecords;
                    var nextCount = Math.Min(blockRecords, records - nextStart);
                    pendingRead = map.ReadAsync(
                        volume,
                        (long)nextStart * recordSize,
                        buffers[nextSlot]!.Memory(nextCount * recordSize),
                        readCts.Token).AsTask();
                }

                entries.AsSpan(start, count).Clear();
                var span = buffers[currentSlot]!.Span(count * recordSize);

                for (var offset = 0; offset < count; offset++)
                {
                    ParseCore(
                        span.Slice(offset * recordSize, recordSize),
                        bytesPerSector,
                        start + offset,
                        entries,
                        target,
                        part.Statistics,
                        nameTable,
                        nameCache);
                }

                progress?.Announce($"Чтение $MFT тома {letter}: {(block + 1L) * 100L / blocks} %");
                currentSlot = nextSlot;
            }

            return part;
        }
        finally
        {
            if (pendingRead is not null)
            {
                readCts.Cancel();
                await pendingRead.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    internal static void ApplyPending(MftEntry[] entries, MftPending pending, MftStatistics statistics)
    {
        foreach (var (index, size) in pending.Sizes)
        {
            if (index < 0 || index >= entries.Length || !entries[index].Present || entries[index].SizeKnown)
            {
                continue;
            }

            if (!MftLayout.SameGeneration(entries[index].Sequence, size.BaseSequence))
            {
                statistics.Partial++;
                continue;
            }

            entries[index].Size = size.Size;
            entries[index].SizeKnown = true;
        }

        foreach (var (index, names) in pending.Alternates)
        {
            if (index < 0 || index >= entries.Length || !entries[index].Present || names.Count == 0)
            {
                continue;
            }

            ref var entry = ref entries[index];

            if (DropStaleNames(names, entry.Sequence))
            {
                statistics.Partial++;
            }

            if (names.Count == 0)
            {
                continue;
            }

            if (!entry.HasName)
            {
                var chosen = names[0];
                names.RemoveAt(0);
                entry.Parent = chosen.Parent;
                entry.ParentSequence = chosen.ParentSequence;
                entry.SetName(chosen.NameId);
            }

            entry.Names = (ushort)Math.Min(ushort.MaxValue, names.Count + 1);

            if (entry.Names > 1)
            {
                statistics.HardLinkedFiles++;
            }
        }

        for (var index = MftLayout.FirstUserRecord; index < entries.Length; index++)
        {
            if (!entries[index].Present || entries[index].HasName)
            {
                continue;
            }

            statistics.Nameless++;

            if (!entries[index].IsDirectory)
            {
                statistics.NamelessBytes += entries[index].Size;
            }
        }
    }

    internal static void Parse(
        Span<byte> record,
        int bytesPerSector,
        int index,
        MftEntry[] entries,
        MftPending pending,
        MftStatistics statistics,
        NameTable names)
    {
        Parse(record, bytesPerSector, index, entries, pending, statistics, names, LocalNameCache.Create());
    }

    internal static void Parse(
        Span<byte> record,
        int bytesPerSector,
        int index,
        MftEntry[] entries,
        MftPending pending,
        MftStatistics statistics,
        NameTable nameTable,
        LocalNameCache cache)
    {
        ParseCore(record, bytesPerSector, index, entries, new(null, pending), statistics, nameTable, cache);
    }

    private static void ParseCore(
        Span<byte> record,
        int bytesPerSector,
        int index,
        MftEntry[] entries,
        MftPendingTarget target,
        MftStatistics statistics,
        NameTable nameTable,
        LocalNameCache cache)
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

        if (reference != 0)
        {
            statistics.Extensions++;

            if (baseReference > int.MaxValue)
            {
                statistics.Partial++;
                return;
            }

            ParseExtension(
                record,
                index,
                (int)baseReference,
                (ushort)(reference >> MftLayout.ReferenceMaskBits),
                target,
                statistics,
                nameTable,
                cache);

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

        var bestNamespace = int.MaxValue;
        var altStart = target.GetAlternateCount(index);
        var mainAltSlot = altStart;
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while ((position + 8) <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || length > (record.Length - position) || length < ResidentHeaderBytes)
            {
                statistics.Partial++;
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
                ReadResident(attribute, type, index, ref entry, ref bestNamespace, altStart, ref mainAltSlot, target, nameTable, cache);
            }

            if (type == MftLayout.AttributeData && attribute[0x09] == 0 && !entry.SizeKnown)
            {
                ReadDataSize(attribute, nonResident, ref entry, statistics);
            }

            position += length;
        }

        entries[index] = entry;
    }

    private static bool DropStaleNames(List<MftName> names, ushort sequence)
    {
        var write = 0;
        var dropped = false;

        for (var i = 0; i < names.Count; i++)
        {
            if (MftLayout.SameGeneration(sequence, names[i].BaseSequence))
            {
                names[write++] = names[i];
            }
            else
            {
                dropped = true;
            }
        }

        if (write < names.Count)
        {
            names.RemoveRange(write, names.Count - write);
        }

        return dropped;
    }

    private static void Merge(MftPart[] parts, MftPending pending, MftStatistics statistics)
    {
        for (var i = 0; i < parts.Length; i++)
        {
            statistics.Add(parts[i].Statistics);
        }

        if (parts.Length == 1)
        {
            var single = parts[0];

            for (var i = 0; i < single.Sizes.Count; i++)
            {
                var item = single.Sizes[i];
                pending.Sizes[item.TargetRecord] = item.Size;
            }

            for (var i = 0; i < single.Alternates.Count; i++)
            {
                var item = single.Alternates[i];
                AddMergedAlternate(pending, item.TargetRecord, item.Name);
            }

            return;
        }

        var sizeCursors = new int[parts.Length];

        while (true)
        {
            var bestPart = -1;
            var bestSource = 0;

            for (var i = 0; i < parts.Length; i++)
            {
                var cursor = sizeCursors[i];
                var list = parts[i].Sizes;

                if (cursor < list.Count && (bestPart < 0 || list[cursor].SourceRecord < bestSource))
                {
                    bestSource = list[cursor].SourceRecord;
                    bestPart = i;
                }
            }

            if (bestPart < 0)
            {
                break;
            }

            var item = parts[bestPart].Sizes[sizeCursors[bestPart]++];
            pending.Sizes[item.TargetRecord] = item.Size;
        }

        var altCursors = new int[parts.Length];

        while (true)
        {
            var bestPart = -1;
            var bestSource = 0;

            for (var i = 0; i < parts.Length; i++)
            {
                var cursor = altCursors[i];
                var list = parts[i].Alternates;

                if (cursor < list.Count && (bestPart < 0 || list[cursor].SourceRecord < bestSource))
                {
                    bestSource = list[cursor].SourceRecord;
                    bestPart = i;
                }
            }

            if (bestPart < 0)
            {
                break;
            }

            var item = parts[bestPart].Alternates[altCursors[bestPart]++];
            AddMergedAlternate(pending, item.TargetRecord, item.Name);
        }
    }

    private static void AddMergedAlternate(MftPending pending, int targetRecord, MftName name)
    {
        if (pending.Alternates.TryGetValue(targetRecord, out var existing))
        {
            existing.Add(name);
        }
        else
        {
            pending.Alternates[targetRecord] = [name];
        }
    }

    private static void ReadDataSize(Span<byte> attribute, bool nonResident, ref MftEntry entry, MftStatistics statistics)
    {
        long size;

        if (nonResident)
        {
            if (attribute.Length < NonResidentHeaderBytes)
            {
                statistics.Partial++;
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

            if ((BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]) + size) > attribute.Length)
            {
                statistics.Partial++;
                return;
            }
        }

        if (size < 0)
        {
            statistics.Partial++;
            return;
        }

        entry.Size = size;
        entry.SizeKnown = true;
    }

    private static void ReadResident(
        Span<byte> attribute,
        uint type,
        int index,
        ref MftEntry entry,
        ref int bestNamespace,
        int altStart,
        ref int mainAltSlot,
        MftPendingTarget target,
        NameTable nameTable,
        LocalNameCache cache)
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
                entry.CreationFileTime = BinaryPrimitives.ReadInt64LittleEndian(value);
                entry.LastAccessFileTime = BinaryPrimitives.ReadInt64LittleEndian(value[0x18..]);
                break;

            case MftLayout.AttributeFileName when value.Length >= FileNameHeaderBytes:
                var candidateNamespace = value[0x41];
                var candidateLength = value[0x40];

                if (candidateNamespace == MftLayout.NamespaceDos
                    || candidateLength == 0
                    || value.Length < (FileNameHeaderBytes + (candidateLength * 2)))
                {
                    break;
                }

                var parent = BinaryPrimitives.ReadUInt64LittleEndian(value);
                var record = (long)(parent & MftLayout.ReferenceMask);
                var nextParent = record <= int.MaxValue ? (int)record : -1;
                var parentSequence = (ushort)(parent >> MftLayout.ReferenceMaskBits);
                var chars = MemoryMarshal.Cast<byte, char>(value.Slice(FileNameHeaderBytes, candidateLength * 2));
                var nameId = cache.Intern(nameTable, chars);

                if (candidateNamespace < bestNamespace)
                {
                    if (entry.HasName
                        && entry.Parent >= 0
                        && (entry.Parent != nextParent || entry.NameId != nameId))
                    {
                        target.InsertAlternate(index, index, mainAltSlot, new(entry.Parent, entry.ParentSequence, entry.NameId, entry.Sequence));
                    }

                    target.RemoveAlternateMatches(index, altStart, nextParent, nameId);
                    bestNamespace = candidateNamespace;
                    entry.Parent = nextParent;
                    entry.ParentSequence = parentSequence;
                    entry.SetName(nameId);
                    mainAltSlot = target.GetAlternateCount(index);
                }
                else if (record <= int.MaxValue && (entry.Parent != (int)record || entry.NameId != nameId))
                {
                    target.AddAlternate(index, index, new((int)record, parentSequence, nameId, entry.Sequence));
                }

                break;

            case MftLayout.AttributeReparsePoint when value.Length >= 4:
                var reparseTag = BinaryPrimitives.ReadUInt32LittleEndian(value);
                entry.IsReparseLink = (reparseTag & MftLayout.ReparseNameSurrogate) != 0;
                entry.ReparseUnknown = false;
                break;
        }
    }

    private static void ParseExtension(
        Span<byte> record,
        int sourceRecord,
        int baseIndex,
        ushort baseSequence,
        MftPendingTarget target,
        MftStatistics statistics,
        NameTable nameTable,
        LocalNameCache cache)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record[0x14..]);

        while ((position + 8) <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record[position..]);

            if (type == MftLayout.AttributeEnd)
            {
                return;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record[(position + 4)..]);

            if (length <= 0 || length > (record.Length - position) || length < ResidentHeaderBytes)
            {
                statistics.Partial++;
                return;
            }

            var attribute = record.Slice(position, length);

            if (type == MftLayout.AttributeFileName && attribute[0x08] == 0)
            {
                AddName(attribute, baseIndex, sourceRecord, target, baseSequence, nameTable, cache);
            }
            else if (type == MftLayout.AttributeData && attribute[0x09] == 0)
            {
                var entry = new MftEntry();
                ReadDataSize(attribute, attribute[0x08] != 0, ref entry, statistics);

                if (entry.SizeKnown)
                {
                    target.AddSize(baseIndex, sourceRecord, new(entry.Size, baseSequence));
                }
            }

            position += length;
        }
    }

    private static void AddName(
        Span<byte> attribute,
        int index,
        int sourceRecord,
        MftPendingTarget target,
        ushort baseSequence,
        NameTable nameTable,
        LocalNameCache cache)
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

        if (nameLength == 0 || value.Length < (FileNameHeaderBytes + (nameLength * 2)))
        {
            return;
        }

        var reference = BinaryPrimitives.ReadUInt64LittleEndian(value);
        var record = (long)(reference & MftLayout.ReferenceMask);

        if (record > int.MaxValue)
        {
            return;
        }

        var chars = MemoryMarshal.Cast<byte, char>(value.Slice(FileNameHeaderBytes, nameLength * 2));
        var nameId = cache.Intern(nameTable, chars);

        target.AddAlternate(index, sourceRecord, new((int)record, (ushort)(reference >> MftLayout.ReferenceMaskBits), nameId, baseSequence));
    }

    private static MftSelfRuns ReadSelfRuns(
        byte[] record,
        IMftVolume volume,
        int bytesPerCluster,
        int recordSize,
        int bytesPerSector,
        char letter)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14));
        List<MftRun>? runs = null;
        byte[]? list = null;
        var bytes = 0L;

        while ((position + 8) <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position));

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position + 4));

            if (length <= 0 || length > (record.Length - position))
            {
                break;
            }

            var attribute = record.AsSpan(position, length);

            if (type == MftLayout.AttributeList)
            {
                list = ReadAttributeList(attribute, volume, bytesPerCluster, letter);
            }
            else if (type == MftLayout.AttributeData && attribute[0x08] != 0 && runs is null)
            {
                var first = ReadFirstExtent(attribute, letter);
                runs = first.Runs;
                bytes = first.Bytes;
            }

            position += length;
        }

        if (runs is null)
        {
            throw new InvalidDataException($"Том {letter}: в записи $MFT нет нерезидентного $DATA");
        }

        if (list is not null)
        {
            AppendExtents(runs, list, volume, bytesPerCluster, recordSize, bytesPerSector, letter);
        }

        return new(runs, bytes);
    }

    private static MftSelfRuns ReadFirstExtent(Span<byte> attribute, char letter)
    {
        if (attribute.Length < NonResidentHeaderBytes)
        {
            throw new InvalidDataException($"Том {letter}: заголовок $DATA записи $MFT обрезан");
        }

        if (BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) != 0)
        {
            throw new InvalidDataException($"Том {letter}: $DATA записи $MFT начинается не с первого экстента");
        }

        var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);

        if (runOffset >= attribute.Length)
        {
            throw new InvalidDataException($"Том {letter}: runlist записи $MFT лежит вне атрибута");
        }

        var realSize = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);
        var runs = MftLayout.DecodeRuns(attribute[runOffset..], out var truncated);

        if (truncated || runs.Count == 0)
        {
            throw new InvalidDataException($"Том {letter}: runlist записи $MFT неполон");
        }

        return new(runs, realSize);
    }

    private static byte[] ReadAttributeList(Span<byte> attribute, IMftVolume volume, int bytesPerCluster, char letter)
    {
        if (attribute[0x08] == 0)
        {
            var valueOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x14..]);
            var valueLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(attribute[0x10..]);

            if ((valueOffset + valueLength) > attribute.Length)
            {
                throw new InvalidDataException($"Том {letter}: $ATTRIBUTE_LIST записи $MFT обрезан");
            }

            return attribute.Slice(valueOffset, valueLength).ToArray();
        }

        if (attribute.Length < NonResidentHeaderBytes)
        {
            throw new InvalidDataException($"Том {letter}: заголовок $ATTRIBUTE_LIST записи $MFT обрезан");
        }

        var size = BinaryPrimitives.ReadInt64LittleEndian(attribute[0x30..]);

        if (size <= 0 || size > AttributeListLimitBytes)
        {
            throw new InvalidDataException($"Том {letter}: непригодный размер $ATTRIBUTE_LIST записи $MFT ({size:N0} Б)");
        }

        var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);

        if (runOffset >= attribute.Length)
        {
            throw new InvalidDataException($"Том {letter}: runlist $ATTRIBUTE_LIST записи $MFT лежит вне атрибута");
        }

        var runs = MftLayout.DecodeRuns(attribute[runOffset..], out var truncated);

        if (truncated || runs.Count == 0)
        {
            throw new InvalidDataException($"Том {letter}: runlist $ATTRIBUTE_LIST записи $MFT неполон");
        }

        var map = new MftMap(runs, bytesPerCluster);
        var aligned = (size + bytesPerCluster - 1) / bytesPerCluster * bytesPerCluster;

        if (map.Length < aligned)
        {
            throw new InvalidDataException($"Том {letter}: отрезки $ATTRIBUTE_LIST покрывают {map.Length:N0} Б из {size:N0} Б");
        }

        var value = new byte[aligned];
        map.Read(volume, 0, value);

        return value[..(int)size];
    }

    private static void AppendExtents(
        List<MftRun> runs,
        byte[] list,
        IMftVolume volume,
        int bytesPerCluster,
        int recordSize,
        int bytesPerSector,
        char letter)
    {
        var record = new byte[recordSize];

        foreach (var extent in CollectExtents(list))
        {
            var covered = Clusters(runs);

            if (extent.Vcn < covered)
            {
                continue;
            }

            if (extent.Vcn > covered)
            {
                throw new InvalidDataException($"Том {letter}: экстенты $MFT не сходятся, после {covered:N0} кластеров в списке идёт VCN {extent.Vcn:N0}");
            }

            var map = new MftMap(runs, bytesPerCluster);
            var offset = (long)extent.Record * recordSize;

            if ((offset + recordSize) > map.Length)
            {
                throw new InvalidDataException($"Том {letter}: запись-расширение $MFT {extent.Record:N0} лежит вне прочитанной части таблицы");
            }

            map.Read(volume, offset, record);

            if (!"FILE"u8.SequenceEqual(record.AsSpan(0, 4)) || !MftLayout.ApplyFixup(record, bytesPerSector))
            {
                throw new InvalidDataException($"Том {letter}: запись-расширение $MFT {extent.Record:N0} не читается как запись NTFS");
            }

            runs.AddRange(ReadExtentRuns(record, extent, letter));
        }
    }

    private static List<MftExtent> CollectExtents(byte[] list)
    {
        var extents = new List<MftExtent>();
        var position = 0;

        while ((position + AttributeListEntryBytes) <= list.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(position));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(list.AsSpan(position + 4));

            if (length < AttributeListEntryBytes || (position + length) > list.Length)
            {
                break;
            }

            var vcn = BinaryPrimitives.ReadInt64LittleEndian(list.AsSpan(position + 0x08));
            var reference = BinaryPrimitives.ReadUInt64LittleEndian(list.AsSpan(position + 0x10));
            var record = (long)(reference & MftLayout.ReferenceMask);

            if (type == MftLayout.AttributeData && list[position + 0x06] == 0 && vcn > 0 && record is > 0 and <= int.MaxValue)
            {
                extents.Add(new(vcn, (int)record));
            }

            position += length;
        }

        extents.Sort((left, right) => left.Vcn.CompareTo(right.Vcn));

        return extents;
    }

    private static List<MftRun> ReadExtentRuns(byte[] record, MftExtent extent, char letter)
    {
        var position = (int)BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(0x14));

        while ((position + 8) <= record.Length)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position));

            if (type == MftLayout.AttributeEnd)
            {
                break;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(position + 4));

            if (length <= 0 || length > (record.Length - position))
            {
                break;
            }

            var attribute = record.AsSpan(position, length);

            if (type == MftLayout.AttributeData
                && attribute[0x08] != 0
                && attribute[0x09] == 0
                && length >= NonResidentHeaderBytes
                && BinaryPrimitives.ReadInt64LittleEndian(attribute[0x10..]) == extent.Vcn)
            {
                var runOffset = BinaryPrimitives.ReadUInt16LittleEndian(attribute[0x20..]);

                if (runOffset >= length)
                {
                    break;
                }

                var runs = MftLayout.DecodeRuns(attribute[runOffset..], out var truncated);

                if (runs.Count > 0 && !truncated)
                {
                    return runs;
                }

                break;
            }

            position += length;
        }

        throw new InvalidDataException($"Том {letter}: в записи-расширении $MFT {extent.Record:N0} нет $DATA с VCN {extent.Vcn:N0}");
    }

    private static long Clusters(List<MftRun> runs)
    {
        var total = 0L;

        foreach (var run in runs)
        {
            total += run.Count;
        }

        return total;
    }
}

internal readonly record struct MftExtent(long Vcn, int Record);

internal readonly record struct MftSelfRuns(List<MftRun> Runs, long Bytes);

internal sealed class MftPending
{
    public Dictionary<int, MftPendingSize> Sizes { get; } = [];

    public Dictionary<int, List<MftName>> Alternates { get; } = [];
}

internal readonly record struct MftPendingSizeEntry(int TargetRecord, int SourceRecord, MftPendingSize Size);

internal readonly record struct MftPendingAlternateEntry(int TargetRecord, int SourceRecord, MftName Name);

internal readonly struct MftPendingTarget(MftPart? part, MftPending? pending)
{
    public void AddSize(int targetRecord, int sourceRecord, MftPendingSize size)
    {
        if (part is not null)
        {
            part.Sizes.Add(new(targetRecord, sourceRecord, size));
            return;
        }

        pending!.Sizes[targetRecord] = size;
    }

    public int GetAlternateCount(int targetRecord)
    {
        if (part is not null)
        {
            return part.Alternates.Count;
        }

        return pending!.Alternates.TryGetValue(targetRecord, out var existing) ? existing.Count : 0;
    }

    public void AddAlternate(int targetRecord, int sourceRecord, MftName name)
    {
        if (part is not null)
        {
            part.Alternates.Add(new(targetRecord, sourceRecord, name));
            return;
        }

        var alternates = pending!.Alternates.TryGetValue(targetRecord, out var existing)
            ? existing
            : pending.Alternates[targetRecord] = [];
        alternates.Add(name);
    }

    public void InsertAlternate(int targetRecord, int sourceRecord, int position, MftName name)
    {
        if (part is not null)
        {
            part.Alternates.Insert(position, new(targetRecord, sourceRecord, name));
            return;
        }

        var alternates = pending!.Alternates.TryGetValue(targetRecord, out var existing)
            ? existing
            : pending.Alternates[targetRecord] = [];
        alternates.Insert(position, name);
    }

    public void RemoveAlternateMatches(int targetRecord, int startPosition, int parent, int nameId)
    {
        if (part is not null)
        {
            var list = part.Alternates;

            for (var i = list.Count - 1; i >= startPosition; i--)
            {
                var candidate = list[i].Name;

                if (candidate.Parent == parent && candidate.NameId == nameId)
                {
                    list.RemoveAt(i);
                }
            }

            return;
        }

        if (!pending!.Alternates.TryGetValue(targetRecord, out var alternates))
        {
            return;
        }

        for (var i = alternates.Count - 1; i >= startPosition; i--)
        {
            var candidate = alternates[i];

            if (candidate.Parent == parent && candidate.NameId == nameId)
            {
                alternates.RemoveAt(i);
            }
        }
    }
}

internal sealed class MftPart
{
    public List<MftPendingSizeEntry> Sizes { get; } = [];

    public List<MftPendingAlternateEntry> Alternates { get; } = [];

    public MftStatistics Statistics { get; } = new();
}

internal sealed class MftAlignedBuffer
{
    private readonly byte[] _data;
    private readonly int _offset;

    public MftAlignedBuffer(int bytes, int alignment)
    {
        _data = GC.AllocateUninitializedArray<byte>(bytes + alignment, pinned: true);
        _offset = (int)((alignment - (Marshal.UnsafeAddrOfPinnedArrayElement(_data, 0).ToInt64() % alignment)) % alignment);
    }

    public Span<byte> Span(int bytes)
    {
        return _data.AsSpan(_offset, bytes);
    }

    public Memory<byte> Memory(int bytes)
    {
        return _data.AsMemory(_offset, bytes);
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
        ValidateBounds(logical, buffer.Length);

        var written = 0;

        while (written < buffer.Length)
        {
            var (physicalOffset, take) = NextChunk(logical, written, buffer.Length);
            volume.ReadAt(physicalOffset, buffer.Slice(written, take));
            written += take;
        }
    }

    public async ValueTask ReadAsync(IMftVolume volume, long logical, Memory<byte> buffer, CancellationToken cancel)
    {
        ValidateBounds(logical, buffer.Length);

        var written = 0;

        while (written < buffer.Length)
        {
            var (physicalOffset, take) = NextChunk(logical, written, buffer.Length);
            await volume.ReadAtAsync(physicalOffset, buffer.Slice(written, take), cancel).ConfigureAwait(false);
            written += take;
        }
    }

    private void ValidateBounds(long logical, int length)
    {
        if (logical < 0 || (logical + length) > Length)
        {
            throw new EndOfStreamException($"Чтение $MFT вышло за разметку: {logical:N0} + {length:N0} Б при длине {Length:N0} Б");
        }
    }

    private (long PhysicalOffset, int Take) NextChunk(long logical, int written, int totalLength)
    {
        var position = logical + written;
        var index = Find(position);
        var inside = position - _starts[index];
        var take = (int)Math.Min(totalLength - written, (_runs[index].Count * _bytesPerCluster) - inside);

        return ((_runs[index].Cluster * _bytesPerCluster) + inside, take);
    }

    private int Find(long position)
    {
        var index = Array.BinarySearch(_starts, position);

        return index >= 0 ? index : ~index - 1;
    }
}
