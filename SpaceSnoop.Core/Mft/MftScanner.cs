using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal delegate bool MftUsnSourceOpener(
    char letter,
    out IMftUsnSource? source,
    out MftUsnErrorKind error,
    out int win32Error);

public sealed class MftScanner
{
    private const int MaterializeThreadCeiling = 8;
    private const int ParallelBfsLevelThreshold = 256;
    private const int MaxCachedUsnVolumes = 2;

    private readonly ILogger _log;
    private readonly MftUsnSourceOpener _usnSourceOpener;
    private readonly int _maxUsnDeltaRecords;
    private readonly Lock _usnCacheLock = new();
    private readonly List<CachedUsnSession> _usnCache = new(MaxCachedUsnVolumes);

    public MftScanner(ILogger<MftScanner>? logger = null)
        : this(logger, TryOpenNativeUsnSource, MftUsnVolumeIndex.DefaultMaxDeltaRecords)
    {
    }

    internal MftScanner(
        ILogger<MftScanner>? logger,
        MftUsnSourceOpener? usnSourceOpener,
        int maxUsnDeltaRecords = MftUsnVolumeIndex.DefaultMaxDeltaRecords)
    {
        _log = logger ?? NullLogger<MftScanner>.Instance;
        _usnSourceOpener = usnSourceOpener ?? TryOpenNativeUsnSource;
        _maxUsnDeltaRecords = maxUsnDeltaRecords;
    }

    public static MftAvailability Probe(string path)
    {
        return MftReader.Probe(path);
    }

    public void InvalidateUsnCache(string? path = null)
    {
        lock (_usnCacheLock)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _usnCache.Clear();
                return;
            }

            var letter = MftReader.VolumeLetter(path);

            if (letter is null)
            {
                return;
            }

            RemoveCachedSessionLocked(char.ToUpperInvariant(letter.Value));
        }
    }

    public bool HasUsnCacheFor(string path)
    {
        var letter = MftReader.VolumeLetter(path);

        if (letter is null)
        {
            return false;
        }

        var upper = char.ToUpperInvariant(letter.Value);

        lock (_usnCacheLock)
        {
            return FindCachedSessionIndexLocked(upper) >= 0;
        }
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

        if (TryRefreshFromUsnCache(directory, letter, progress, cancel, out var cachedResult))
        {
            return cachedResult!;
        }

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
        var letterOnly = MftReader.VolumeLetter(directory.FullName);

        if (letterOnly is not null
            && TryRefreshFromUsnCache(directory, letterOnly.Value, progress, cancel, out var cachedResult))
        {
            availability = MftAvailability.Available;
            return cachedResult;
        }

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
            report += $" (примеры: {string.Join(", ", statistics.DetachedSamples.Select(static x => x.Text))})";
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

    internal static DirectorySpace Materialize(
        MftTable table,
        MftLinks links,
        int start,
        DirectoryInfo directory,
        MftSubtreeTotals totals,
        ulong[]? reachable,
        ScanProgress? progress,
        CancellationToken cancel,
        MftUsnMaterializeCapture? usnCapture = null)
    {
        cancel.ThrowIfCancellationRequested();

        var entries = table.Entries;
        var firstChild = links.FirstChild;
        var nextSibling = links.NextSibling;
        var reuseStore = start == MftLayout.RootRecord;
        var store = reuseStore ? table.Store : new ScanTreeStore(16, singleWriter: false);
        var rootNameId = store.Names.Intern(directory.Name);

        if (reuseStore)
        {
            store.Names.Freeze();
        }

        var rootIndex = store.AllocateDirectorySlot();
        store.GetDirectoryRef(rootIndex) = new()
        {
            CreationRaw = TimestampEncoding.FromMftFileTime(entries[start].CreationFileTime),
            LastAccessRaw = TimestampEncoding.FromMftFileTime(entries[start].LastAccessFileTime),
            NameId = rootNameId,
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalFileCount = -1,
            TotalDirCount = -1,
            State = SpaceState.Added,
            Flags = FileEntry.UtcFileTimeFlag,
        };

        if (usnCapture is not null)
        {
            usnCapture.FrnLocation[start] = rootIndex;
            usnCapture.FrnNameId[start] = rootNameId;
            usnCapture.FrnSequence[start] = entries[start].Sequence;
        }

        var estimatedDirs = reuseStore ? Math.Max(1024, (int)(table.Statistics.RecordsInUse / 5)) : 1024;
        var dirRecords = new int[estimatedDirs];
        dirRecords[0] = start;
        var dirCount = 1;
        store.EnsureDirectoryCapacity(1);

        var workerCount = Math.Min(MaterializeThreadCeiling, Math.Max(1, Environment.ProcessorCount));
        var head = 0;
        var topLevelReported = false;
        var rootCache = reuseStore ? default : LocalNameCache.Create();

        while (head < dirCount)
        {
            cancel.ThrowIfCancellationRequested();
            var levelEnd = dirCount;
            var levelSize = levelEnd - head;

            if (levelSize < ParallelBfsLevelThreshold || workerCount == 1)
            {
                for (var idx = head; idx < levelEnd; idx++)
                {
                    if ((idx & 1023) == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                    }

                    var rec = dirRecords[idx];
                    var firstSub = -1;
                    var lastSub = -1;
                    var subCount = 0;
                    var fileCount = 0;

                    for (var child = firstChild[rec]; child >= 0; child = nextSibling[child])
                    {
                        ref readonly var e = ref entries[child];

                        if (!e.IsDirectory)
                        {
                            fileCount++;
                            continue;
                        }

                        if (reachable is not null && !MftLinks.TryMarkReachable(reachable, child))
                        {
                            continue;
                        }

                        var childIdx = dirCount++;

                        if (childIdx >= dirRecords.Length)
                        {
                            Array.Resize(ref dirRecords, dirRecords.Length * 2);
                        }

                        store.EnsureDirectoryCapacity(dirCount);

                        dirRecords[childIdx] = child;
                        var nameId = reuseStore ? e.NameId : rootCache.Intern(store.Names, table.Names.GetUtf8(e.NameId));
                        store.GetDirectoryRef(childIdx) = new()
                        {
                            CreationRaw = TimestampEncoding.FromMftFileTime(e.CreationFileTime),
                            LastAccessRaw = TimestampEncoding.FromMftFileTime(e.LastAccessFileTime),
                            NameId = nameId,
                            ParentIndex = idx,
                            FirstChildDir = -1,
                            NextSiblingDir = -1,
                            TotalFileCount = -1,
                            TotalDirCount = -1,
                            State = SpaceState.Added,
                            Flags = FileEntry.UtcFileTimeFlag,
                        };

                        if (firstSub < 0)
                        {
                            firstSub = childIdx;
                        }
                        else
                        {
                            store.GetDirectoryRef(lastSub).NextSiblingDir = childIdx;
                        }

                        lastSub = childIdx;
                        subCount++;

                        var names = Math.Max(1, (int)e.Names);

                        if (names > 1)
                        {
                            totals.ExtraNames += names - 1;
                            totals.ExtraNameBytes += e.Size * (names - 1);
                        }

                        if (usnCapture is not null)
                        {
                            usnCapture.FrnLocation[child] = childIdx;
                            usnCapture.FrnNameId[child] = nameId;
                            usnCapture.FrnSequence[child] = e.Sequence;
                        }
                    }

                    ref var parentEntry = ref store.GetDirectoryRef(idx);
                    parentEntry.FirstChildDir = firstSub;
                    parentEntry.SubDirCount = subCount;
                    parentEntry.FileCount = fileCount;

                    if (!topLevelReported)
                    {
                        topLevelReported = true;
                        progress?.SetTopLevelTotal(subCount);
                    }
                }

                store.SetDirectoryCount(dirCount);
            }
            else
            {
                var subCounts = new int[levelSize];
                var fileCounts = new int[levelSize];
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
                        x =>
                        {
                            var s = (int)((long)levelSize * x / workerCount);
                            var eIdx = (int)((long)levelSize * (x + 1) / workerCount);

                            for (var i = s; i < eIdx; i++)
                            {
                                if ((i & 1023) == 0)
                                {
                                    cancel.ThrowIfCancellationRequested();
                                }

                                var rec = dirRecords[head + i];
                                var sc = 0;
                                var fc = 0;

                                for (var child = firstChild[rec]; child >= 0; child = nextSibling[child])
                                {
                                    if (entries[child].IsDirectory)
                                    {
                                        if (reachable is null || !MftLinks.IsReachable(reachable, child))
                                        {
                                            sc++;
                                        }
                                    }
                                    else
                                    {
                                        fc++;
                                    }
                                }

                                subCounts[i] = sc;
                                fileCounts[i] = fc;
                            }
                        });
                }
                catch (AggregateException exception)
                {
                    ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
                }

                var subOffsets = new int[levelSize];
                var nextDirBase = dirCount;

                for (var i = 0; i < levelSize; i++)
                {
                    subOffsets[i] = nextDirBase;
                    var sc = subCounts[i];
                    ref var d = ref store.GetDirectoryRef(head + i);
                    d.SubDirCount = sc;
                    d.FileCount = fileCounts[i];
                    nextDirBase += sc;
                }

                if (nextDirBase > dirRecords.Length)
                {
                    var nextCap = dirRecords.Length;

                    while (nextCap < nextDirBase)
                    {
                        nextCap *= 2;
                    }

                    Array.Resize(ref dirRecords, nextCap);
                }

                store.EnsureDirectoryCapacity(nextDirBase);

                store.SetDirectoryCount(nextDirBase);
                var workerTotals = new MftSubtreeTotals[workerCount];

                try
                {
                    Parallel.For(
                        0,
                        workerCount,
                        options,
                        x =>
                        {
                            var localTotals = new MftSubtreeTotals();
                            workerTotals[x] = localTotals;
                            var workerCache = reuseStore ? default : LocalNameCache.Create();
                            var s = (int)((long)levelSize * x / workerCount);
                            var eIdx = (int)((long)levelSize * (x + 1) / workerCount);

                            for (var i = s; i < eIdx; i++)
                            {
                                if ((i & 1023) == 0)
                                {
                                    cancel.ThrowIfCancellationRequested();
                                }

                                var parentIdx = head + i;
                                var sc = store.GetDirectoryRef(parentIdx).SubDirCount;

                                if (sc == 0)
                                {
                                    store.GetDirectoryRef(parentIdx).FirstChildDir = -1;
                                    continue;
                                }

                                var writePos = subOffsets[i];
                                var firstSub = -1;
                                var lastSub = -1;
                                var actualCount = 0;
                                var rec = dirRecords[parentIdx];

                                for (var child = firstChild[rec]; child >= 0; child = nextSibling[child])
                                {
                                    ref readonly var e = ref entries[child];

                                    if (!e.IsDirectory)
                                    {
                                        continue;
                                    }

                                    if (reachable is not null && !MftLinks.TryMarkReachable(reachable, child))
                                    {
                                        continue;
                                    }

                                    var childIdx = writePos++;
                                    dirRecords[childIdx] = child;
                                    var nameId = reuseStore
                                        ? e.NameId
                                        : workerCache.Intern(store.Names, table.Names.GetUtf8(e.NameId));

                                    store.GetDirectoryRef(childIdx) = new()
                                    {
                                        CreationRaw = TimestampEncoding.FromMftFileTime(e.CreationFileTime),
                                        LastAccessRaw = TimestampEncoding.FromMftFileTime(e.LastAccessFileTime),
                                        NameId = nameId,
                                        ParentIndex = parentIdx,
                                        FirstChildDir = -1,
                                        NextSiblingDir = -1,
                                        TotalFileCount = -1,
                                        TotalDirCount = -1,
                                        State = SpaceState.Added,
                                        Flags = FileEntry.UtcFileTimeFlag,
                                    };

                                    if (firstSub < 0)
                                    {
                                        firstSub = childIdx;
                                    }
                                    else
                                    {
                                        store.GetDirectoryRef(lastSub).NextSiblingDir = childIdx;
                                    }

                                    lastSub = childIdx;
                                    actualCount++;

                                    var names = Math.Max(1, (int)e.Names);

                                    if (names > 1)
                                    {
                                        localTotals.ExtraNames += names - 1;
                                        localTotals.ExtraNameBytes += e.Size * (names - 1);
                                    }

                                    if (usnCapture is not null)
                                    {
                                        usnCapture.FrnLocation[child] = childIdx;
                                        usnCapture.FrnNameId[child] = nameId;
                                        usnCapture.FrnSequence[child] = e.Sequence;
                                    }
                                }

                                ref var parentEntry = ref store.GetDirectoryRef(parentIdx);
                                parentEntry.FirstChildDir = firstSub;
                                parentEntry.SubDirCount = actualCount;
                            }
                        });
                }
                catch (AggregateException exception)
                {
                    ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
                }

                for (var w = 0; w < workerTotals.Length; w++)
                {
                    totals.Add(workerTotals[w]);
                }

                dirCount = nextDirBase;
            }

            head = levelEnd;
        }

        AssignPackedFileSlices(store, dirCount);

        if (dirCount < ParallelBfsLevelThreshold || workerCount == 1)
        {
            var paths = progress is null ? null : new MftProgressPath(store, directory.FullName);
            var batchDirs = 0;
            var batchFiles = 0;
            var batchBytes = 0L;
            var nextFlushTick = Environment.TickCount64 + 40;

            for (var idx = 0; idx < dirCount; idx++)
            {
                if ((idx & 255) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }

                var ownSize = FillDirectoryFiles(
                    table,
                    entries,
                    firstChild,
                    nextSibling,
                    dirRecords[idx],
                    idx,
                    store,
                    reuseStore,
                    rootCache,
                    totals,
                    reachable,
                    usnCapture,
                    out var writtenFiles);

                batchDirs++;
                batchFiles += writtenFiles;
                batchBytes += ownSize;

                if (paths is not null && (batchDirs >= 256 || Environment.TickCount64 >= nextFlushTick))
                {
                    progress!.AddBatch(batchDirs, batchFiles, batchBytes, paths.For(idx));
                    batchDirs = 0;
                    batchFiles = 0;
                    batchBytes = 0;
                    nextFlushTick = Environment.TickCount64 + 40;
                }
            }

            if (progress is not null && (batchDirs > 0 || batchFiles > 0 || batchBytes > 0))
            {
                progress.AddBatch(batchDirs, batchFiles, batchBytes, directory.FullName);
            }
        }
        else
        {
            var workerTotals = new MftSubtreeTotals[workerCount];
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
                    x =>
                    {
                        var workerCache = reuseStore ? default : LocalNameCache.Create();
                        var localTotals = new MftSubtreeTotals();
                        workerTotals[x] = localTotals;
                        var paths = progress is null ? null : new MftProgressPath(store, directory.FullName);
                        var batchDirs = 0;
                        var batchFiles = 0;
                        var batchBytes = 0L;
                        var nextFlushTick = Environment.TickCount64 + 40;

                        var s = (int)((long)dirCount * x / workerCount);
                        var eIdx = (int)((long)dirCount * (x + 1) / workerCount);

                        for (var idx = s; idx < eIdx; idx++)
                        {
                            if ((idx & 255) == 0)
                            {
                                cancel.ThrowIfCancellationRequested();
                            }

                            var ownSize = FillDirectoryFiles(
                                table,
                                entries,
                                firstChild,
                                nextSibling,
                                dirRecords[idx],
                                idx,
                                store,
                                reuseStore,
                                workerCache,
                                localTotals,
                                reachable,
                                usnCapture,
                                out var writtenFiles);

                            batchDirs++;
                            batchFiles += writtenFiles;
                            batchBytes += ownSize;

                            if (paths is not null && (batchDirs >= 256 || Environment.TickCount64 >= nextFlushTick))
                            {
                                progress!.AddBatch(batchDirs, batchFiles, batchBytes, paths.For(idx));
                                batchDirs = 0;
                                batchFiles = 0;
                                batchBytes = 0;
                                nextFlushTick = Environment.TickCount64 + 40;
                            }
                        }

                        if (progress is not null && (batchDirs > 0 || batchFiles > 0 || batchBytes > 0))
                        {
                            progress.AddBatch(batchDirs, batchFiles, batchBytes, null);
                        }
                    });
            }
            catch (AggregateException exception)
            {
                ExceptionDispatchInfo.Capture(exception.Flatten().InnerExceptions[0]).Throw();
            }

            for (var w = 0; w < workerTotals.Length; w++)
            {
                totals.Add(workerTotals[w]);
            }
        }

        for (var idx = dirCount - 1; idx >= 0; idx--)
        {
            if ((idx & 0xFFFF) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            ref var d = ref store.GetDirectoryRef(idx);
            var totalSize = d.Size;
            var totalFiles = d.FileCount;
            var totalDirs = d.SubDirCount;

            for (var child = d.FirstChildDir; child >= 0;)
            {
                ref readonly var childEntry = ref store.GetDirectoryRef(child);
                totalSize += childEntry.TotalSize;
                totalFiles += childEntry.TotalFileCount;
                totalDirs += childEntry.TotalDirCount;
                child = childEntry.NextSiblingDir;
            }

            d.TotalSize = totalSize;
            d.TotalFileCount = totalFiles;
            d.TotalDirCount = totalDirs;
        }

        if (progress is not null)
        {
            var topLevelCount = store.GetDirectoryRef(rootIndex).SubDirCount;

            for (var i = 0; i < topLevelCount; i++)
            {
                progress.CompleteTopLevel();
            }
        }

        if (!reuseStore)
        {
            store.Names.Freeze();
        }

        return store.GetOrCreateDirectoryFacade(rootIndex);
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
        if (TryRefreshFromUsnCache(directory, letter, progress, cancel, out var cachedResult))
        {
            return cachedResult!;
        }

        var requestedMode = mode == MftReadMode.RawDisk ? MftReadMode.RawDisk : MftReadMode.Volume;
        var actualMode = requestedMode;
        string? fallbackReason = null;
        MftTable? table = null;
        IMftUsnSource? usnSource = null;
        MftUsnJournalInfo? journalBefore = null;

        try
        {
            if (_usnSourceOpener(char.ToUpperInvariant(letter), out var openedUsn, out _, out _) && openedUsn is not null)
            {
                if (openedUsn.TryQueryJournal(out var journalInfo, out _, out _))
                {
                    usnSource = openedUsn;
                    journalBefore = journalInfo;
                }
                else
                {
                    openedUsn.Dispose();
                }
            }

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
                                attempt.ExtentLength > 0 ? attempt.ExtentLength : null,
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

            var captureUsn = start == MftLayout.RootRecord && usnSource is not null && journalBefore is not null;
            var usnCapture = captureUsn ? new MftUsnMaterializeCapture(table.Entries.Length) : null;

            var totals = new MftSubtreeTotals();
            var root = Materialize(table, links, start, directory, totals, reachable, progress, cancel, usnCapture);

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

            if (captureUsn)
            {
                var usnIndex = new MftUsnVolumeIndex(root.Store!, journalBefore!.Value, table, usnCapture!, _maxUsnDeltaRecords);

                if (usnIndex.TryRefresh(usnSource!, out var catchupSummary, cancel))
                {
                    result = result with
                    {
                        ExtraNames = usnIndex.ExtraNames,
                        ExtraNameBytes = usnIndex.ExtraNameBytes,
                    };
                    CacheUsnSession(char.ToUpperInvariant(letter), usnIndex, result);
                }
                else
                {
                    _log.MftUsnFallback(
                        char.ToUpperInvariant(letter),
                        catchupSummary.FailureReason ?? catchupSummary.ErrorKind.ToString(),
                        catchupSummary.Win32Error);
                }
            }

            _log.MftScanCompleted(directory.FullName, table.Entries.Length, result.ExtraNames, result.ExtraNameBytes, result.Report);

            return result;
        }
        finally
        {
            usnSource?.Dispose();
        }
    }

    private static bool TryOpenNativeUsnSource(
        char letter,
        out IMftUsnSource? source,
        out MftUsnErrorKind error,
        out int win32Error)
    {
        var opened = MftUsnNativeSource.TryOpen(letter, out var nativeSource, out error, out win32Error);
        source = nativeSource;
        return opened;
    }

    private bool TryRefreshFromUsnCache(
        DirectoryInfo directory,
        char letter,
        ScanProgress? progress,
        CancellationToken cancel,
        out MftScanResult? result)
    {
        var upper = char.ToUpperInvariant(letter);
        CachedUsnSession? session;

        lock (_usnCacheLock)
        {
            var idx = FindCachedSessionIndexLocked(upper);

            if (idx < 0)
            {
                result = null;
                return false;
            }

            session = _usnCache[idx];
        }

        lock (session.SyncLock)
        {
            if (!_usnSourceOpener(upper, out var source, out var openErr, out var openWin32) || source is null)
            {
                source?.Dispose();
                InvalidateUsnSession(upper, session);
                _log.MftUsnFallback(upper, $"не удалось открыть USN-источник ({openErr})", openWin32);
                result = null;
                return false;
            }

            MftUsnRefreshSummary summary;

            using (source)
            {
                if (!session.Index.TryRefresh(source, out summary, cancel))
                {
                    InvalidateUsnSession(upper, session);
                    _log.MftUsnFallback(upper, summary.FailureReason ?? summary.ErrorKind.ToString(), summary.Win32Error);
                    result = null;
                    return false;
                }
            }

            if (!session.Index.TryResolveDirectory(directory, out var dirIndex))
            {
                InvalidateUsnSession(upper, session);
                _log.MftUsnFallback(upper, $"каталог «{directory.FullName}» не найден в USN-индексе", 0);
                result = null;
                return false;
            }

            var root = session.Index.Store.GetOrCreateDirectoryFacade(dirIndex);
            root.FixAbsolutePath(directory);

            if (progress is not null)
            {
                progress.NotifyVolumeReady();
                var subCount = session.Index.Store.GetDirectoryRef(dirIndex).SubDirCount;
                progress.SetTopLevelTotal(subCount);
                progress.AddBatch(root.TotalDirectoryCount + 1, root.TotalFileCount, root.TotalSize, directory.FullName);

                for (var i = 0; i < subCount; i++)
                {
                    progress.CompleteTopLevel();
                }
            }

            PromoteCachedSession(upper, session);

            _log.MftUsnRefreshed(upper, summary.UniqueRecords, summary.EventsRead, summary.OpenDirtyCount, summary.ElapsedMs);

            result = session.BaseResult with
            {
                Root = root,
                ExtraNames = session.Index.ExtraNames,
                ExtraNameBytes = session.Index.ExtraNameBytes,
                ReadMode = session.BaseResult.ReadMode,
                FallbackReason = null,
                UsedUsn = true,
                UsnLostMarks = summary.RemovedMarkedCount,
            };
            return true;
        }
    }

    private void CacheUsnSession(char upperLetter, MftUsnVolumeIndex index, MftScanResult baseResult)
    {
        lock (_usnCacheLock)
        {
            RemoveCachedSessionLocked(upperLetter);

            if (_usnCache.Count >= MaxCachedUsnVolumes)
            {
                _usnCache.RemoveAt(_usnCache.Count - 1);
            }

            _usnCache.Insert(0, new CachedUsnSession(upperLetter, index, baseResult));
        }
    }

    private void PromoteCachedSession(char upperLetter, CachedUsnSession session)
    {
        lock (_usnCacheLock)
        {
            var idx = FindCachedSessionIndexLocked(upperLetter);

            if (idx > 0 && ReferenceEquals(_usnCache[idx], session))
            {
                _usnCache.RemoveAt(idx);
                _usnCache.Insert(0, session);
            }
        }
    }

    private void InvalidateUsnSession(char upperLetter, CachedUsnSession session)
    {
        lock (_usnCacheLock)
        {
            var idx = FindCachedSessionIndexLocked(upperLetter);

            if (idx >= 0 && ReferenceEquals(_usnCache[idx], session))
            {
                _usnCache.RemoveAt(idx);
            }
        }
    }

    private int FindCachedSessionIndexLocked(char upperLetter)
    {
        for (var i = 0; i < _usnCache.Count; i++)
        {
            if (_usnCache[i].Letter == upperLetter)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveCachedSessionLocked(char upperLetter)
    {
        var idx = FindCachedSessionIndexLocked(upperLetter);

        if (idx >= 0)
        {
            _usnCache.RemoveAt(idx);
        }
    }

    private static void AssignPackedFileSlices(ScanTreeStore store, int dirCount)
    {
        var curChunk = 0;
        var curOffset = 0;

        for (var idx = 0; idx < dirCount; idx++)
        {
            ref var d = ref store.GetDirectoryRef(idx);
            var fc = d.FileCount;

            if (fc == 0)
            {
                d.FileStart = 0;
                continue;
            }

            if (fc > ScanTreeStore.ChunkSize)
            {
                d.FileStart = store.AllocateLargeArray(fc, out _);
                continue;
            }

            if (curOffset + fc > ScanTreeStore.ChunkSize)
            {
                curChunk++;
                curOffset = 0;
            }

            if (curChunk >= ScanTreeStore.MaxRegularChunks)
            {
                d.FileStart = store.AllocateLargeArray(fc, out _);
                continue;
            }

            d.FileStart = (curChunk << ScanTreeStore.ChunkShift) | curOffset;
            curOffset += fc;
        }

        var regularChunks = curOffset > 0 ? curChunk + 1 : curChunk;
        store.PreallocateFileChunks(regularChunks);
    }

    private static long FillDirectoryFiles(
        MftTable table,
        MftEntry[] entries,
        int[] firstChild,
        int[] nextSibling,
        int record,
        int dirIndex,
        ScanTreeStore store,
        bool reuseStore,
        LocalNameCache nameCache,
        MftSubtreeTotals totals,
        ulong[]? reachable,
        MftUsnMaterializeCapture? usnCapture,
        out int writtenFiles)
    {
        ref var d = ref store.GetDirectoryRef(dirIndex);
        var expectedFiles = d.FileCount;

        if (expectedFiles == 0)
        {
            d.FileStart = 0;
            d.Size = 0;
            writtenFiles = 0;
            return 0L;
        }

        var span = store.GetWritableSpan(d.FileStart, expectedFiles);
        var written = 0;
        var ownSize = 0L;

        for (var child = firstChild[record]; child >= 0; child = nextSibling[child])
        {
            ref readonly var entry = ref entries[child];

            if (entry.IsDirectory)
            {
                continue;
            }

            if (reachable is not null && !MftLinks.TryMarkReachable(reachable, child))
            {
                continue;
            }

            var names = Math.Max(1, (int)entry.Names);

            if (names > 1)
            {
                totals.ExtraNames += names - 1;
                totals.ExtraNameBytes += entry.Size * (names - 1);
            }

            if (!entry.SizeKnown)
            {
                totals.UnknownSizeFiles++;
            }

            var nameId = reuseStore ? entry.NameId : nameCache.Intern(store.Names, table.Names.GetUtf8(entry.NameId));
            span[written++] = new()
            {
                Size = entry.Size,
                CreationRaw = TimestampEncoding.FromMftFileTime(entry.CreationFileTime),
                LastAccessRaw = TimestampEncoding.FromMftFileTime(entry.LastAccessFileTime),
                NameId = nameId,
                State = SpaceState.Added,
                Flags = FileEntry.UtcFileTimeFlag,
            };
            ownSize += entry.Size;

            if (usnCapture is not null)
            {
                usnCapture.FrnLocation[child] = ~dirIndex;
                usnCapture.FrnNameId[child] = nameId;
                usnCapture.FrnSequence[child] = entry.Sequence;
            }
        }

        d.FileCount = written;
        d.Size = ownSize;
        writtenFiles = written;
        return ownSize;
    }

    private sealed class CachedUsnSession(char letter, MftUsnVolumeIndex index, MftScanResult baseResult)
    {
        public Lock SyncLock { get; } = new();

        public char Letter { get; } = letter;

        public MftUsnVolumeIndex Index { get; } = index;

        public MftScanResult BaseResult { get; } = baseResult;
    }
}

internal sealed class MftUsnMaterializeCapture
{
    public int[] FrnLocation { get; }

    public int[] FrnNameId { get; }

    public ushort[] FrnSequence { get; }

    public MftUsnMaterializeCapture(int recordCount)
    {
        FrnLocation = GC.AllocateUninitializedArray<int>(recordCount);
        Array.Fill(FrnLocation, int.MinValue);
        FrnNameId = GC.AllocateUninitializedArray<int>(recordCount);
        FrnSequence = GC.AllocateUninitializedArray<ushort>(recordCount);
    }
}

internal sealed class MftProgressPath
{
    private const long RefreshMilliseconds = 50;

    private readonly DirectorySpace? _root;
    private readonly ScanTreeStore? _store;
    private readonly List<string?> _segments = [];
    private readonly string _rootPath;
    private string _current;
    private long _due;

    public MftProgressPath(DirectorySpace root, string rootPath)
    {
        _root = root;
        _rootPath = rootPath;
        _current = rootPath;
    }

    public MftProgressPath(ScanTreeStore store, string rootPath)
    {
        _store = store;
        _rootPath = rootPath;
        _current = rootPath;
    }

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

    public string For(int dirIndex)
    {
        var now = Environment.TickCount64;

        if (now < _due)
        {
            return _current;
        }

        _due = now + RefreshMilliseconds;
        _current = Build(dirIndex);
        return _current;
    }

    private string Build(DirectorySpace node)
    {
        _segments.Clear();

        for (SpaceBase? current = node; current is not null && !ReferenceEquals(current, _root); current = current.Parent)
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

    private string Build(int dirIndex)
    {
        var store = _store!;
        _segments.Clear();

        for (var cur = dirIndex; cur > 0; cur = store.GetDirectoryRef(cur).ParentIndex)
        {
            _segments.Add(store.Names.GetString(store.GetDirectoryRef(cur).NameId));
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
    string? FallbackReason = null,
    bool UsedUsn = false,
    int UsnLostMarks = 0)
{
    public long DroppedObjects => DetachedRecords + NamelessRecords + DamagedRecords;

    public long DroppedBytes => OrphanBytes + NamelessBytes;

    public bool IsComplete => DroppedObjects == 0 && PartialRecords == 0 && UnknownSizeFiles == 0;
}
