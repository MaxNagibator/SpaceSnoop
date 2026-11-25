using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

public sealed class MftScanner(ILogger<MftScanner>? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger<MftScanner>.Instance;

    public static MftAvailability Probe(string path)
    {
        return MftReader.Probe(path);
    }

    // TODO: $MFT читается целиком независимо от цели скана, поэтому на подкаталоге движок проигрывает
    // обходу каталогами (`C:\Windows` – 2,77 с против 3,18 с); сам выбор отдан вызывающему (в WPF – настройкой),
    // здесь по цели не решают. Триггер апгрейда: понадобится автоматика – решать по доле цели в томе.
    public MftScanResult Calculate(DirectoryInfo directory, ScanProgress? progress, CancellationToken cancel = default)
    {
        return Calculate(directory, progress, MftReadMode.Volume, cancel);
    }

    public MftScanResult Calculate(
        DirectoryInfo directory,
        ScanProgress? progress,
        MftReadMode mode,
        CancellationToken cancel = default)
    {
        var letter = MftReader.VolumeLetter(directory.FullName)
            ?? throw new InvalidOperationException($"Путь {directory.FullName} не лежит на локальном томе с буквой");

        using var volume = MftFileVolume.Open(letter);
        return CalculateWithFallback(
            volume,
            prefetchedBoot: null,
            directory,
            letter,
            MftReader.ResolveThreads(letter),
            4 * 1024 * 1024,
            progress,
            mode,
            rawDiskOpener: null,
            cancel);
    }

    public MftScanResult? TryCalculate(
        DirectoryInfo directory,
        ScanProgress? progress,
        out MftAvailability availability,
        MftReadMode mode = MftReadMode.Volume,
        CancellationToken cancel = default)
    {
        availability = MftReader.TryOpenVolume(directory.FullName, out var letter, out var volume, out var boot);

        if (availability != MftAvailability.Available || volume is null || boot is null)
        {
            volume?.Dispose();
            return null;
        }

        using (volume)
        {
            return CalculateWithFallback(
                volume,
                boot,
                directory,
                letter,
                MftReader.ResolveThreads(letter),
                4 * 1024 * 1024,
                progress,
                mode,
                rawDiskOpener: null,
                cancel);
        }
    }

    internal MftScanResult Calculate(
        IMftVolume volume,
        DirectoryInfo directory,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        CancellationToken cancel = default)
    {
        return Calculate(volume, directory, letter, threads, blockBytes, progress, MftReadMode.Volume, rawDiskOpener: null, cancel);
    }

    internal MftScanResult Calculate(
        IMftVolume volume,
        DirectoryInfo directory,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        MftReadMode mode,
        Func<byte[], MftRawDiskAttempt>? rawDiskOpener,
        CancellationToken cancel = default)
    {
        return CalculateWithFallback(volume, prefetchedBoot: null, directory, letter, threads, blockBytes, progress, mode, rawDiskOpener, cancel);
    }

    private MftScanResult CalculateWithFallback(
        IMftVolume volume,
        byte[]? prefetchedBoot,
        DirectoryInfo directory,
        char letter,
        int threads,
        int blockBytes,
        ScanProgress? progress,
        MftReadMode mode,
        Func<byte[], MftRawDiskAttempt>? rawDiskOpener,
        CancellationToken cancel)
    {
        var requestedMode = mode == MftReadMode.RawDisk ? MftReadMode.RawDisk : MftReadMode.Volume;
        var actualMode = requestedMode;
        string? fallbackReason = null;
        MftTable? table = null;

        if (requestedMode == MftReadMode.RawDisk)
        {
            cancel.ThrowIfCancellationRequested();

            if (prefetchedBoot is null)
            {
                prefetchedBoot = new byte[MftRawDiskGuard.BootSectorBytes];
                volume.ReadAt(0, prefetchedBoot);
            }

            var attempt = rawDiskOpener is null
                ? MftRawDiskProbe.Open(letter, prefetchedBoot)
                : rawDiskOpener(prefetchedBoot);

            if (attempt.Volume is null)
            {
                fallbackReason = string.IsNullOrWhiteSpace(attempt.FailureReason)
                    ? "проверка физического диска не пройдена"
                    : attempt.FailureReason;
            }
            else
            {
                try
                {
                    using (attempt.Volume)
                    {
                        table = MftReader.Read(
                            attempt.Volume,
                            letter,
                            threads,
                            blockBytes,
                            progress,
                            prefetchedBoot,
                            attempt.ExtentLength,
                            cancel,
                            attempt.MftValidDataLength > 0 ? attempt.MftValidDataLength : null);
                    }

                    if (table.Statistics.Damaged > 0 || table.Statistics.Partial > 0)
                    {
                        fallbackReason = $"несогласованные записи $MFT на диске (повреждено {table.Statistics.Damaged:N0}, неполных {table.Statistics.Partial:N0})";
                        table = null;
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidDataException)
                {
                    fallbackReason = exception.Message;
                    table = null;
                }
            }

            if (table is null)
            {
                progress?.Reset();
                actualMode = MftReadMode.Volume;
            }
        }

        table ??= MftReader.Read(volume, letter, threads, blockBytes, progress, prefetchedBoot, partitionExtentLength: null, cancel);

        progress?.Announce($"Разбор связей тома {letter}:");

        var links = MftLinks.Build(table, cancel);
        var start = Locate(table, links, directory, letter);

        if (start < 0)
        {
            throw new DirectoryNotFoundException($"Каталог {directory.FullName} не найден в $MFT тома {letter}:");
        }

        links.Rehome(table, start, cancel);
        ulong[]? reachable = null;

        if (start == MftLayout.RootRecord)
        {
            reachable = new ulong[(table.Entries.Length + 63) >> 6];

            if (table.Entries.Length > MftLayout.RootRecord)
            {
                MftLinks.SetReachable(reachable, MftLayout.RootRecord);
            }
        }
        else
        {
            links.DropUnreachable(table, cancel);
        }

        var totals = new MftSubtreeTotals();
        var root = Materialize(table, links, start, directory, totals, reachable, progress, cancel);

        if (reachable is not null)
        {
            links.FinishUnreachable(table, reachable, cancel);
        }

        root.FixAbsolutePath(directory);

        var statistics = table.Statistics;

        var result = new MftScanResult(
            root,
            totals.ExtraNames,
            totals.ExtraNameBytes,
            totals.UnknownSizeFiles,
            statistics.SkippedLinks,
            statistics.Detached,
            statistics.OrphanFiles,
            statistics.OrphanBytes,
            statistics.Nameless,
            statistics.NamelessBytes,
            statistics.Damaged,
            statistics.Partial,
            statistics.StaleParents,
            statistics.Rehomed,
            threads,
            FormatReport(statistics, totals.UnknownSizeFiles),
            actualMode,
            fallbackReason);

        _log.MftScanCompleted(directory.FullName, table.Entries.Length, result.ExtraNames, result.ExtraNameBytes, result.Report);

        return result;
    }

    internal static string FormatReport(MftStatistics statistics, long unknownSizeFiles)
    {
        var report =
            $"записей {statistics.RecordsScanned:N0}, занято {statistics.RecordsInUse:N0}, расширений {statistics.Extensions:N0}, "
            + $"с несколькими именами {statistics.HardLinkedFiles:N0}, пропущено ссылок {statistics.SkippedLinks:N0}, "
            + $"безымянных {statistics.Nameless:N0}, оторванных {statistics.Detached:N0}, повреждённых {statistics.Damaged:N0}, "
            + $"неполных {statistics.Partial:N0}, без размера {unknownSizeFiles:N0}, "
            + $"устаревших ссылок на родителя {statistics.StaleParents:N0}, переподвешено {statistics.Rehomed:N0}";

        if (statistics.Detached > 0 || statistics.MissingParents > 0 || statistics.Unreachable > 0)
        {
            report += $", без записи родителя {statistics.MissingParents:N0}, в оторванных ветках {statistics.Unreachable:N0}";
        }

        if (statistics.DetachedSamples.Count > 0)
        {
            report += $" (примеры: {string.Join(", ", statistics.DetachedSamples.Select(static sample => sample.Text))})";
        }

        return report;
    }

    internal static int Locate(MftTable table, MftLinks links, DirectoryInfo directory, char letter)
    {
        var entries = table.Entries;

        if (entries.Length <= MftLayout.RootRecord
            || !entries[MftLayout.RootRecord].Exists
            || !entries[MftLayout.RootRecord].IsDirectory)
        {
            return -1;
        }

        var full = Path.GetFullPath(directory.FullName);
        var root = Path.GetPathRoot(full) ?? $"{letter}:\\";
        var relative = full[root.Length..].Trim(Path.DirectorySeparatorChar);
        var current = MftLayout.RootRecord;

        if (relative.Length == 0)
        {
            return current;
        }

        // TODO: чувствительность к регистру снимается один раз с корня тома, поэтому каталог с пофайловым
        // режимом NTFS (`fsutil file setCaseSensitiveInfo`) получает политику корня; точное совпадение имени
        // предпочитается нечувствительному, и этого хватает, пока обе формы имени существуют рядом.
        // Триггер апгрейда: понадобится отличать `foo` от `Foo`, когда лежит только одна из них.
        var caseSensitive = PathCase.IsCaseSensitive(root);

        foreach (var range in relative.AsSpan().Split(Path.DirectorySeparatorChar))
        {
            var segment = relative.AsSpan(range);

            if (segment.IsEmpty)
            {
                continue;
            }

            var found = -1;
            var exact = -1;

            for (var child = links.FirstChild[current]; child >= 0; child = links.NextSibling[child])
            {
                if (!entries[child].IsDirectory)
                {
                    continue;
                }

                if (table.Names.Equals(entries[child].NameId, segment, StringComparison.Ordinal))
                {
                    exact = child;
                    break;
                }

                if (!caseSensitive
                    && (found < 0 || child < found)
                    && table.Names.Equals(entries[child].NameId, segment, StringComparison.OrdinalIgnoreCase))
                {
                    found = child;
                }
            }

            current = exact >= 0 ? exact : found;

            if (current < 0)
            {
                return -1;
            }
        }

        return current;
    }

    private const int MaterializeThreadCeiling = 8;

    internal static DirectorySpace Materialize(
        MftTable table,
        MftLinks links,
        int start,
        DirectoryInfo directory,
        MftSubtreeTotals totals,
        ulong[]? reachable,
        ScanProgress? progress,
        CancellationToken cancel)
    {
        var entries = table.Entries;
        var reuseStore = start == MftLayout.RootRecord;
        var store = reuseStore ? table.Store : new ScanTreeStore(16, singleWriter: false);
        var rootCache = reuseStore ? default : LocalNameCache.Create();
        var rootWriter = new FileSlabWriter(store);
        var root = new DirectorySpace(
            store,
            store.Names.Intern(directory.Name),
            null,
            TimestampEncoding.FromMftFileTime(entries[start].CreationFileTime),
            TimestampEncoding.FromMftFileTime(entries[start].LastAccessFileTime),
            FileEntry.UtcFileTimeFlag);

        var files = new List<FileEntry>();
        var branches = new List<(int Record, DirectorySpace Node)>();
        FillDirectory(table, links, start, root, store, reuseStore, rootCache, rootWriter, totals, branches, files, reachable);

        progress?.SetTopLevelTotal(branches.Count);
        progress?.AddBatch(1, files.Count, root.Size, directory.FullName);

        if (branches.Count > 0)
        {
            var workerCount = Math.Min(MaterializeThreadCeiling, Math.Max(1, Environment.ProcessorCount));
            var targetQueue = workerCount * 2;
            var maxChunksPerSplit = workerCount * 2;
            var queue = new Queue<MaterializeChunk>(branches.Count);
            var gate = new object();

            for (var i = 0; i < branches.Count; i++)
            {
                queue.Enqueue(new([branches[i].Node], [branches[i].Record], 0, 1, null));
            }

            var queuedCount = branches.Count;
            var outstanding = branches.Count;
            var waitingWorkers = 0;
            var aborted = false;
            var workerTotals = new MftSubtreeTotals[workerCount];
            var options = new ParallelOptions
            {
                CancellationToken = cancel,
                MaxDegreeOfParallelism = workerCount,
            };

            using var cancelRegistration = cancel.Register(
                () =>
                {
                    lock (gate)
                    {
                        aborted = true;
                        Monitor.PulseAll(gate);
                    }
                });

            try
            {
                Parallel.For(
                    0,
                    workerCount,
                    options,
                    x =>
                    {
                        var writer = new FileSlabWriter(store);
                        var workerCache = reuseStore ? default : LocalNameCache.Create();
                        var localTotals = new MftSubtreeTotals();
                        workerTotals[x] = localTotals;
                        var stack = new List<MaterializeFrame>(64);
                        var children = new List<(int Record, DirectorySpace Node)>();
                        var workerFiles = new List<FileEntry>();
                        var newChunks = new List<MaterializeChunk>(maxChunksPerSplit + 8);
                        var paths = progress is null ? null : new MftProgressPath(root, directory.FullName);
                        var batchDirs = 0;
                        var batchFiles = 0;
                        var batchBytes = 0L;
                        var nextFlushTick = Environment.TickCount64 + 40;

                        try
                        {
                            var stop = false;

                            while (!stop)
                            {
                                MaterializeChunk chunk = default;

                                lock (gate)
                                {
                                    while (queue.Count == 0)
                                    {
                                        if (outstanding == 0 || aborted)
                                        {
                                            stop = true;
                                            break;
                                        }

                                        waitingWorkers++;
                                        Monitor.Wait(gate);
                                        waitingWorkers--;
                                    }

                                    if (stop || aborted)
                                    {
                                        stop = true;
                                        break;
                                    }

                                    chunk = queue.Dequeue();
                                    Volatile.Write(ref queuedCount, queue.Count);
                                }

                                var promoted = false;

                                for (var idx = 0; idx < chunk.Count; idx++)
                                {
                                    cancel.ThrowIfCancellationRequested();

                                    var itemPos = chunk.Start + idx;
                                    stack.Clear();
                                    stack.Add(new(chunk.Records[itemPos], chunk.Nodes[itemPos], false));
                                    var unvisitedOnStack = 1;

                                    while (stack.Count > 0)
                                    {
                                        cancel.ThrowIfCancellationRequested();

                                        var frame = stack[^1];
                                        stack.RemoveAt(stack.Count - 1);

                                        if (frame.Ascending)
                                        {
                                            RollupTotals(frame.Node);
                                            continue;
                                        }

                                        unvisitedOnStack--;
                                        FillDirectory(table, links, frame.Record, frame.Node, store, reuseStore, workerCache, writer, localTotals, children, workerFiles, reachable);
                                        batchDirs++;
                                        batchFiles += workerFiles.Count;
                                        batchBytes += frame.Node.Size;

                                        if (paths is not null && (batchDirs >= 256 || Environment.TickCount64 >= nextFlushTick))
                                        {
                                            progress!.AddBatch(batchDirs, batchFiles, batchBytes, paths.For(frame.Node));
                                            batchDirs = 0;
                                            batchFiles = 0;
                                            batchBytes = 0;
                                            nextFlushTick = Environment.TickCount64 + 40;
                                        }

                                        if (children.Count == 0)
                                        {
                                            frame.Node.SetAggregatedTotals(frame.Node.Size, workerFiles.Count, 0);
                                            continue;
                                        }

                                        var remainingInChunk = chunk.Count - (idx + 1);
                                        var canSplit = (children.Count >= 2 || unvisitedOnStack > 0 || remainingInChunk > 0)
                                            && (Volatile.Read(ref waitingWorkers) > 0 || Volatile.Read(ref queuedCount) < targetQueue);

                                        if (!canSplit)
                                        {
                                            stack.Add(new(frame.Record, frame.Node, true));

                                            for (var j = 0; j < children.Count; j++)
                                            {
                                                if ((j & 1023) == 0)
                                                {
                                                    cancel.ThrowIfCancellationRequested();
                                                }

                                                stack.Add(new(children[j].Record, children[j].Node, false));
                                            }

                                            unvisitedOnStack += children.Count;
                                            continue;
                                        }

                                        cancel.ThrowIfCancellationRequested();
                                        newChunks.Clear();
                                        var splitRemainder = remainingInChunk > 0 && chunk.ParentSplit is not null;

                                        if (splitRemainder)
                                        {
                                            newChunks.Add(new(chunk.Nodes, chunk.Records, itemPos + 1, remainingInChunk, chunk.ParentSplit));
                                        }

                                        var currentParent = chunk.ParentSplit;
                                        var pos = 0;

                                        while (pos < stack.Count)
                                        {
                                            cancel.ThrowIfCancellationRequested();
                                            var ascNode = stack[pos].Node;
                                            pos++;
                                            var sibStart = pos;

                                            while (pos < stack.Count && !stack[pos].Ascending)
                                            {
                                                pos++;
                                            }

                                            var sibCount = pos - sibStart;

                                            if (sibCount == 0)
                                            {
                                                currentParent = new(ascNode, currentParent, 1);
                                            }
                                            else
                                            {
                                                var sibNodes = new DirectorySpace[sibCount];
                                                var sibRecords = new int[sibCount];

                                                for (var k = 0; k < sibCount; k++)
                                                {
                                                    if ((k & 1023) == 0)
                                                    {
                                                        cancel.ThrowIfCancellationRequested();
                                                    }

                                                    sibNodes[k] = stack[sibStart + k].Node;
                                                    sibRecords[k] = stack[sibStart + k].Record;
                                                }

                                                var sibChunkCount = Math.Min(sibCount, maxChunksPerSplit);
                                                var split = new MaterializeSplit(ascNode, currentParent, sibChunkCount + 1);
                                                AddPartitionedChunks(newChunks, sibNodes, sibRecords, sibChunkCount, split);
                                                currentParent = split;
                                            }
                                        }

                                        var childNodes = frame.Node.SubDirectoriesArray;
                                        var childRecords = new int[children.Count];

                                        for (var k = 0; k < children.Count; k++)
                                        {
                                            if ((k & 1023) == 0)
                                            {
                                                cancel.ThrowIfCancellationRequested();
                                            }

                                            childRecords[k] = children[k].Record;
                                        }

                                        var childChunkCount = Math.Min(children.Count, maxChunksPerSplit);
                                        var nodeSplit = new MaterializeSplit(frame.Node, currentParent, childChunkCount);
                                        AddPartitionedChunks(newChunks, childNodes, childRecords, childChunkCount, nodeSplit);

                                        stack.Clear();

                                        lock (gate)
                                        {
                                            if (aborted || cancel.IsCancellationRequested)
                                            {
                                                aborted = true;
                                                Monitor.PulseAll(gate);
                                                stop = true;
                                            }
                                            else
                                            {
                                                if (splitRemainder)
                                                {
                                                    Interlocked.Increment(ref chunk.ParentSplit!.Remaining);
                                                }

                                                outstanding += newChunks.Count - 1;

                                                for (var k = 0; k < newChunks.Count; k++)
                                                {
                                                    queue.Enqueue(newChunks[k]);
                                                }

                                                Volatile.Write(ref queuedCount, queue.Count);

                                                if (waitingWorkers > 0)
                                                {
                                                    Monitor.PulseAll(gate);
                                                }
                                            }
                                        }

                                        promoted = true;
                                        break;
                                    }

                                    if (promoted || stop)
                                    {
                                        break;
                                    }
                                }

                                if (stop)
                                {
                                    break;
                                }

                                if (!promoted)
                                {
                                    CompleteSplitChain(chunk.ParentSplit, progress);

                                    lock (gate)
                                    {
                                        outstanding--;

                                        if (outstanding == 0)
                                        {
                                            Monitor.PulseAll(gate);
                                        }
                                    }
                                }
                            }

                            cancel.ThrowIfCancellationRequested();

                            if (progress is not null && (batchDirs > 0 || batchFiles > 0 || batchBytes > 0))
                            {
                                progress.AddBatch(batchDirs, batchFiles, batchBytes, null);
                            }
                        }
                        catch
                        {
                            lock (gate)
                            {
                                aborted = true;
                                Monitor.PulseAll(gate);
                            }

                            throw;
                        }
                    });
            }
            catch (AggregateException exception)
            {
                ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
            }

            for (var i = 0; i < workerTotals.Length; i++)
            {
                totals.Add(workerTotals[i]);
            }
        }

        RollupTotals(root);
        store.Freeze();
        return root;
    }

    private static void AddPartitionedChunks(
        List<MaterializeChunk> destination,
        DirectorySpace[] nodes,
        int[] records,
        int chunkCount,
        MaterializeSplit parent)
    {
        var total = nodes.Length;
        var offset = 0;

        for (var i = 0; i < chunkCount; i++)
        {
            var take = (total - offset + (chunkCount - i) - 1) / (chunkCount - i);
            destination.Add(new(nodes, records, offset, take, parent));
            offset += take;
        }
    }

    private static void CompleteSplitChain(MaterializeSplit? split, ScanProgress? progress)
    {
        if (split is null)
        {
            progress?.CompleteTopLevel();
            return;
        }

        for (var current = split; current is not null; current = current.Parent)
        {
            if (Interlocked.Decrement(ref current.Remaining) != 0)
            {
                return;
            }

            RollupTotals(current.Node);

            if (current.Parent is null)
            {
                progress?.CompleteTopLevel();
            }
        }
    }

    private static void RollupTotals(DirectorySpace node)
    {
        var subs = node.SubDirectoriesArray;
        var totalSize = node.Size;
        var totalFiles = node.FileCount;
        var totalDirs = subs.Length;

        for (var i = 0; i < subs.Length; i++)
        {
            var sub = subs[i];
            totalSize += sub.TotalSize;
            totalFiles += sub.TotalFileCount;
            totalDirs += sub.TotalDirectoryCount;
        }

        node.SetAggregatedTotals(totalSize, totalFiles, totalDirs);
    }

    private static void FillDirectory(
        MftTable table,
        MftLinks links,
        int record,
        DirectorySpace node,
        ScanTreeStore store,
        bool reuseStore,
        LocalNameCache nameCache,
        FileSlabWriter writer,
        MftSubtreeTotals totals,
        List<(int Record, DirectorySpace Node)> children,
        List<FileEntry> files,
        ulong[]? reachable)
    {
        var entries = table.Entries;
        children.Clear();
        files.Clear();
        var ownSize = 0L;

        for (var child = links.FirstChild[record]; child >= 0; child = links.NextSibling[child])
        {
            if (reachable is not null && !MftLinks.TryMarkReachable(reachable, child))
            {
                continue;
            }

            ref var entry = ref entries[child];
            var names = Math.Max(1, (int)entry.Names);

            if (names > 1)
            {
                totals.ExtraNames += names - 1;
                totals.ExtraNameBytes += entry.Size * (names - 1);
            }

            var nameId = reuseStore ? entry.NameId : nameCache.Intern(store.Names, table.Names.GetUtf8(entry.NameId));

            if (entry.IsDirectory)
            {
                children.Add(
                    (child,
                     new(
                         store,
                         nameId,
                         node,
                         TimestampEncoding.FromMftFileTime(entry.CreationFileTime),
                         TimestampEncoding.FromMftFileTime(entry.LastAccessFileTime),
                         FileEntry.UtcFileTimeFlag)));
                continue;
            }

            if (!entry.SizeKnown)
            {
                totals.UnknownSizeFiles++;
            }

            files.Add(
                new()
                {
                    Size = entry.Size,
                    CreationRaw = TimestampEncoding.FromMftFileTime(entry.CreationFileTime),
                    LastAccessRaw = TimestampEncoding.FromMftFileTime(entry.LastAccessFileTime),
                    NameId = nameId,
                    State = SpaceState.Added,
                    Flags = FileEntry.UtcFileTimeFlag,
                });
            ownSize += entry.Size;
        }

        var subDirectories = children.Count == 0 ? [] : new DirectorySpace[children.Count];

        for (var i = 0; i < subDirectories.Length; i++)
        {
            subDirectories[i] = children[i].Node;
        }

        writer.WriteFiles(CollectionsMarshal.AsSpan(files), out var fileStart);
        node.SetScanned(store, fileStart, files.Count, ownSize, subDirectories);
    }

    private readonly record struct MaterializeFrame(int Record, DirectorySpace Node, bool Ascending);

    private readonly record struct MaterializeChunk(
        DirectorySpace[] Nodes,
        int[] Records,
        int Start,
        int Count,
        MaterializeSplit? ParentSplit);

    private sealed class MaterializeSplit(DirectorySpace node, MaterializeSplit? parent, int remaining)
    {
        public int Remaining = remaining;

        public DirectorySpace Node { get; } = node;

        public MaterializeSplit? Parent { get; } = parent;
    }
}

internal sealed class MftProgressPath(DirectorySpace root, string rootPath)
{
    private const long RefreshMilliseconds = 50;

    private readonly List<string?> _segments = [];
    private readonly string _rootPath = rootPath;
    private string _current = rootPath;
    private long _due;

    public string For(DirectorySpace node)
    {
        var now = Environment.TickCount64;

        if (now < _due)
        {
            return _current;
        }

        _due = now + RefreshMilliseconds;
        _current = Build(node);
        return _current;
    }

    private string Build(DirectorySpace node)
    {
        _segments.Clear();

        for (SpaceBase? current = node; current is not null && !ReferenceEquals(current, root); current = current.Parent)
        {
            _segments.Add(current.Name);
        }

        if (_segments.Count == 0)
        {
            return _rootPath;
        }

        _segments.Add(_rootPath);
        _segments.Reverse();

        return Path.Join(CollectionsMarshal.AsSpan(_segments));
    }
}

internal sealed class MftSubtreeTotals
{
    public long ExtraNames { get; set; }

    public long ExtraNameBytes { get; set; }

    public long UnknownSizeFiles { get; set; }

    public void Add(MftSubtreeTotals other)
    {
        ExtraNames += other.ExtraNames;
        ExtraNameBytes += other.ExtraNameBytes;
        UnknownSizeFiles += other.UnknownSizeFiles;
    }
}

public sealed record MftScanResult(
    DirectorySpace Root,
    long ExtraNames,
    long ExtraNameBytes,
    long UnknownSizeFiles,
    long SkippedLinks,
    long DetachedRecords,
    long OrphanFiles,
    long OrphanBytes,
    long NamelessRecords,
    long NamelessBytes,
    long DamagedRecords,
    long PartialRecords,
    long StaleParents,
    long Rehomed,
    int Threads,
    string Report,
    MftReadMode ReadMode = MftReadMode.Volume,
    string? FallbackReason = null)
{
    public long DroppedObjects => DetachedRecords + NamelessRecords + DamagedRecords;

    public long DroppedBytes => OrphanBytes + NamelessBytes;

    public bool IsComplete => DroppedObjects == 0 && PartialRecords == 0 && UnknownSizeFiles == 0;
}
