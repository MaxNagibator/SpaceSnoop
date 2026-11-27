using SpaceSnoop.Core.Domain;
using System.Diagnostics;

namespace SpaceSnoop.Core.Mft;

internal readonly record struct MftUsnRefreshSummary(
    int EventsRead,
    int UniqueRecords,
    int OpenDirtyCount,
    double ElapsedMs,
    MftUsnErrorKind ErrorKind,
    int Win32Error,
    string? FailureReason,
    int RemovedMarkedCount = 0);

internal sealed class MftUsnVolumeIndex
{
    public const int DefaultMaxDeltaRecords = 50_000;

    private const uint OpenWriteMask = 0x0000_0107u;
    private const uint UsnReasonFileDelete = 0x0000_0200u;
    private const uint UsnReasonClose = 0x8000_0000u;
    private const int MaxPostFreezeNameCache = 65_536;
    private const int MaxDepth = 512;
    private const int BatchFileIndexThreshold = 32;

    // TODO: файлы, открытые на запись до первого скана (до NextUsn), не попадают в _openDirtyFrns до CLOSE; при воспроизводимом расхождении – добавить проверку открытых дескрипторов.
    private readonly HashSet<ulong> _openDirtyFrns = [];
    private readonly Dictionary<int, HardlinkRecord> _hardlinks = [];
    private readonly List<MftUsnEvent> _eventsBuffer = [];
    private readonly int _maxDeltaRecords;
    private Dictionary<string, int>? _postFreezeNames;
    private int[] _frnLocation;
    private int[] _frnNameId;
    private ushort[] _frnSequence;

    public ScanTreeStore Store { get; }

    public ulong JournalId { get; }

    public uint VolumeSerialNumber { get; }

    public long CursorUsn { get; private set; }

    public int OpenDirtyCount => _openDirtyFrns.Count;

    public long ExtraNames { get; private set; }

    public long ExtraNameBytes { get; private set; }

    public MftUsnVolumeIndex(
        ScanTreeStore store,
        MftUsnJournalInfo journal,
        MftTable table,
        MftUsnMaterializeCapture capture,
        int maxDeltaRecords = DefaultMaxDeltaRecords)
    {
        Store = store;
        JournalId = journal.JournalId;
        VolumeSerialNumber = journal.VolumeSerialNumber;
        CursorUsn = journal.NextUsn;
        _maxDeltaRecords = maxDeltaRecords;
        _frnLocation = capture.FrnLocation;
        _frnNameId = capture.FrnNameId;
        _frnSequence = capture.FrnSequence;

        var entries = table.Entries;
        var alternates = table.Alternates;
        var limit = Math.Min(entries.Length, _frnLocation.Length);

        for (var rec = 0; rec < limit; rec++)
        {
            if (_frnLocation[rec] == int.MinValue)
            {
                continue;
            }

            ref readonly var entry = ref entries[rec];

            if (entry.Names <= 1)
            {
                continue;
            }

            var extra = entry.Names - 1;
            var size = entry.IsDirectory ? 0L : entry.Size;
            HardlinkTarget[] links;

            if (alternates.TryGetValue(rec, out var altList) && altList.Count > 0)
            {
                links = new HardlinkTarget[1 + altList.Count];
                links[0] = new(entry.Parent, entry.ParentSequence, entry.NameId);

                for (var i = 0; i < altList.Count; i++)
                {
                    var alt = altList[i];
                    links[i + 1] = new(alt.Parent, alt.ParentSequence, alt.NameId);
                }
            }
            else
            {
                links = [new(entry.Parent, entry.ParentSequence, entry.NameId)];
            }

            _hardlinks[rec] = new(extra, size, links);
            ExtraNames += extra;
            ExtraNameBytes += size * extra;
        }
    }

    public bool TryRefresh(IMftUsnSource source, out MftUsnRefreshSummary summary, CancellationToken cancel)
    {
        var startTick = Stopwatch.GetTimestamp();
        cancel.ThrowIfCancellationRequested();

        if (!source.TryQueryJournal(out var journal, out var queryErr, out var queryWin32))
        {
            summary = FailSummary(startTick, queryErr, queryWin32, "ошибка запроса состояния USN-журнала");
            return false;
        }

        if (journal.VolumeSerialNumber != VolumeSerialNumber)
        {
            summary = FailSummary(startTick, MftUsnErrorKind.InvalidParameter, 0, "изменился серийный номер тома");
            return false;
        }

        if (journal.JournalId != JournalId)
        {
            summary = FailSummary(startTick, MftUsnErrorKind.InvalidParameter, 0, "изменился идентификатор USN-журнала");
            return false;
        }

        if (CursorUsn < journal.FirstUsn)
        {
            summary = FailSummary(startTick, MftUsnErrorKind.JournalEntryDeleted, 1181, "курсор USN вышел за нижнюю границу журнала");
            return false;
        }

        _eventsBuffer.Clear();
        var maxEvents = _maxDeltaRecords >= int.MaxValue / 8 ? int.MaxValue : Math.Max(1, _maxDeltaRecords * 8);

        if (!source.TryReadDelta(JournalId, CursorUsn, maxEvents, _eventsBuffer, out var nextUsn, out var readErr, out var readWin32, cancel))
        {
            summary = FailSummary(startTick, readErr, readWin32, "ошибка чтения дельты USN-журнала");
            return false;
        }

        var uniqueRecs = new HashSet<int>();
        Dictionary<int, ulong>? openByRec = _openDirtyFrns.Count > 0 ? new(_openDirtyFrns.Count) : null;

        if (openByRec is not null)
        {
            foreach (var openFrn in _openDirtyFrns)
            {
                var openRec = (int)(openFrn & (ulong)MftLayout.ReferenceMask);

                if (openRec >= MftLayout.FirstUserRecord)
                {
                    uniqueRecs.Add(openRec);
                    openByRec[openRec] = openFrn;
                }
            }
        }

        for (var i = 0; i < _eventsBuffer.Count; i++)
        {
            var ev = _eventsBuffer[i];
            var rec = (int)(ev.Frn & (ulong)MftLayout.ReferenceMask);

            if (rec >= MftLayout.FirstUserRecord)
            {
                uniqueRecs.Add(rec);
            }

            if ((ev.Reason & (UsnReasonClose | UsnReasonFileDelete)) != 0)
            {
                openByRec?.Remove(rec);
            }
            else if ((ev.Reason & OpenWriteMask) != 0 && rec >= MftLayout.FirstUserRecord)
            {
                openByRec ??= [];
                openByRec[rec] = ev.Frn;
            }
        }

        if (uniqueRecs.Count > _maxDeltaRecords)
        {
            summary = new(
                _eventsBuffer.Count,
                uniqueRecs.Count,
                _openDirtyFrns.Count,
                Stopwatch.GetElapsedTime(startTick).TotalMilliseconds,
                MftUsnErrorKind.DeltaLimitExceeded,
                0,
                $"превышен порог дельты USN ({uniqueRecs.Count:N0} > {_maxDeltaRecords:N0})");
            return false;
        }

        var removedMarkedCount = 0;

        if (uniqueRecs.Count > 0)
        {
            var recList = new int[uniqueRecs.Count];
            uniqueRecs.CopyTo(recList);

            if (!TryFetchRecords(
                    source,
                    recList,
                    openByRec,
                    cancel,
                    out var fetched,
                    out var staleOpenFrns,
                    out var fetchErr,
                    out var fetchWin32))
            {
                summary = FailSummary(startTick, fetchErr, fetchWin32, "ошибка чтения записи $MFT по USN-дельте");
                return false;
            }

            for (var i = 0; i < _eventsBuffer.Count; i++)
            {
                var ev = _eventsBuffer[i];

                if ((ev.Reason & (UsnReasonClose | UsnReasonFileDelete)) != 0)
                {
                    _openDirtyFrns.Remove(ev.Frn);
                }
                else if ((ev.Reason & OpenWriteMask) != 0)
                {
                    _openDirtyFrns.Add(ev.Frn);
                }
            }

            if (staleOpenFrns is not null)
            {
                for (var i = 0; i < staleOpenFrns.Count; i++)
                {
                    _openDirtyFrns.Remove(staleOpenFrns[i]);
                }
            }

            removedMarkedCount = ApplyBatch(recList, fetched!);
        }

        CursorUsn = nextUsn;

        summary = new(
            _eventsBuffer.Count,
            uniqueRecs.Count,
            _openDirtyFrns.Count,
            Stopwatch.GetElapsedTime(startTick).TotalMilliseconds,
            MftUsnErrorKind.None,
            0,
            null,
            removedMarkedCount);
        return true;
    }

    public bool TryResolveDirectory(DirectoryInfo directory, out int dirIndex)
    {
        var full = Path.GetFullPath(directory.FullName);
        var root = Path.GetPathRoot(full);

        if (string.IsNullOrEmpty(root))
        {
            dirIndex = -1;
            return false;
        }

        var relative = full[root.Length..].Trim(Path.DirectorySeparatorChar);
        var current = 0;

        if (relative.Length == 0)
        {
            dirIndex = 0;
            return true;
        }

        var caseSensitive = PathCase.IsCaseSensitive(root);

        lock (Store.FacadeLock)
        {
            foreach (var range in relative.AsSpan().Split(Path.DirectorySeparatorChar))
            {
                var segment = relative.AsSpan(range);

                if (segment.IsEmpty)
                {
                    continue;
                }

                var exact = -1;
                var found = -1;

                for (var child = Store.GetDirectoryRef(current).FirstChildDir; child >= 0;)
                {
                    ref readonly var childEntry = ref Store.GetDirectoryRef(child);

                    if (Store.Names.Equals(childEntry.NameId, segment, StringComparison.Ordinal))
                    {
                        exact = child;
                        break;
                    }

                    if (!caseSensitive
                        && found < 0
                        && Store.Names.Equals(childEntry.NameId, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        found = child;
                    }

                    child = childEntry.NextSiblingDir;
                }

                current = exact >= 0 ? exact : found;

                if (current < 0)
                {
                    dirIndex = -1;
                    return false;
                }
            }
        }

        dirIndex = current;
        return true;
    }

    public DirectorySpace ResolveSubtreeRoot(
        int dirIndex,
        DirectoryInfo directory,
        out long extraNames,
        out long extraNameBytes)
    {
        lock (Store.FacadeLock)
        {
            if (dirIndex == 0 || _hardlinks.Count == 0)
            {
                extraNames = ExtraNames;
                extraNameBytes = ExtraNameBytes;
                return Store.GetOrCreateDirectoryFacadeLocked(dirIndex);
            }

            List<RehomedLink>? rehomed = null;
            long subExtraNames = 0;
            long subExtraNameBytes = 0;

            foreach (var (rec, info) in _hardlinks)
            {
                var loc = _frnLocation[rec];

                if (loc == int.MinValue)
                {
                    continue;
                }

                var currentDirIdx = loc >= 0 ? loc : ~loc;

                if (IsDirInSubtreeLocked(currentDirIdx, dirIndex))
                {
                    subExtraNames += info.ExtraCount;
                    subExtraNameBytes += info.FileSize * info.ExtraCount;
                    continue;
                }

                var isDir = loc >= 0;

                for (var i = 0; i < info.Links.Length; i++)
                {
                    var link = info.Links[i];

                    if (!IsUsableParent(link.ParentRec, link.ParentSequence))
                    {
                        continue;
                    }

                    var targetParentDirIdx = _frnLocation[link.ParentRec];

                    if (!IsDirInSubtreeLocked(targetParentDirIdx, dirIndex))
                    {
                        continue;
                    }

                    if (isDir && IsDirInSubtreeLocked(targetParentDirIdx, loc))
                    {
                        continue;
                    }

                    rehomed ??= [];
                    rehomed.Add(new(rec, loc, targetParentDirIdx, link.NameId));
                    subExtraNames += info.ExtraCount;
                    subExtraNameBytes += info.FileSize * info.ExtraCount;
                    break;
                }
            }

            extraNames = subExtraNames;
            extraNameBytes = subExtraNameBytes;

            if (rehomed is null || rehomed.Count == 0)
            {
                return Store.GetOrCreateDirectoryFacadeLocked(dirIndex);
            }

            return MaterializeSubtreeWithRehomeLocked(dirIndex, directory, rehomed);
        }
    }

    private static bool TryFetchRecords(
        IMftUsnSource source,
        int[] recList,
        Dictionary<int, ulong>? openByRec,
        CancellationToken cancel,
        out MftUsnRecord[]? fetched,
        out List<ulong>? staleOpenFrns,
        out MftUsnErrorKind error,
        out int win32Error)
    {
        var records = new MftUsnRecord[recList.Length];
        staleOpenFrns = null;

        for (var i = 0; i < recList.Length; i++)
        {
            if ((i & 63) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            var rec = recList[i];

            if (!source.TryFetchFileRecord(rec, out var fresh, out error, out win32Error))
            {
                fetched = null;
                staleOpenFrns = null;
                return false;
            }

            if (fresh.IsIncomplete)
            {
                fetched = null;
                staleOpenFrns = null;
                error = MftUsnErrorKind.IoError;
                win32Error = 0;
                return false;
            }

            if (openByRec is not null && openByRec.TryGetValue(rec, out var openFrn))
            {
                var openSeq = (ushort)(openFrn >> MftLayout.ReferenceMaskBits);

                if (!fresh.Exists || !MftLayout.SameGeneration(fresh.Sequence, openSeq))
                {
                    staleOpenFrns ??= [];
                    staleOpenFrns.Add(openFrn);
                }
                else if (!fresh.IsDirectory && !fresh.IsLink && source.TryQueryLiveFileSize(openFrn, out var liveSize))
                {
                    fresh = fresh with { Size = liveSize, SizeKnown = true };
                }
            }
            else if (fresh.Exists && !fresh.IsDirectory && !fresh.IsLink && !fresh.SizeKnown)
            {
                var frn = ((ulong)fresh.Sequence << MftLayout.ReferenceMaskBits) | (uint)rec;

                if (source.TryQueryLiveFileSize(frn, out var liveSize))
                {
                    fresh = fresh with { Size = liveSize, SizeKnown = true };
                }
            }

            if (fresh.Exists && !fresh.IsDirectory && !fresh.IsLink && !fresh.SizeKnown)
            {
                fetched = null;
                staleOpenFrns = null;
                error = MftUsnErrorKind.IoError;
                win32Error = 0;
                return false;
            }

            records[i] = fresh;
        }

        fetched = records;
        error = MftUsnErrorKind.None;
        win32Error = 0;
        return true;
    }

    private int ApplyBatch(int[] recList, MftUsnRecord[] fetched)
    {
        var maxRec = 0;

        for (var i = 0; i < recList.Length; i++)
        {
            if (recList[i] > maxRec)
            {
                maxRec = recList[i];
            }
        }

        var removedMarkedCount = 0;

        lock (Store.FacadeLock)
        {
            EnsureRecordCapacity(maxRec);

            var dirBatch = new List<int>();
            var activeFreshDirPos = new Dictionary<int, int>();
            Dictionary<int, DirBatchFileIndex>? batchFileIndices = null;
            List<(int DirIndex, bool WasAttached)>? detachedDirs = null;
            Dictionary<int, bool>? detachedInBatch = null;

            for (var i = 0; i < recList.Length; i++)
            {
                var rec = recList[i];
                ref readonly var fresh = ref fetched[i];
                var freshDir = fresh.Exists && !fresh.IsLink && fresh.IsDirectory;

                if (freshDir || _frnLocation[rec] >= 0)
                {
                    dirBatch.Add(i);

                    if (freshDir)
                    {
                        activeFreshDirPos[rec] = i;
                    }
                }
            }

            for (var i = 0; i < dirBatch.Count; i++)
            {
                var idx = dirBatch[i];
                DetachDirBatchEntry(
                    recList[idx],
                    in fetched[idx],
                    activeFreshDirPos,
                    ref batchFileIndices,
                    ref removedMarkedCount,
                    ref detachedDirs,
                    ref detachedInBatch);
            }

            if (activeFreshDirPos.Count > 0)
            {
                AttachDirectoriesTopDown(fetched, activeFreshDirPos, detachedInBatch, ref detachedDirs);
            }

            for (var i = 0; i < recList.Length; i++)
            {
                var rec = recList[i];
                ref readonly var fresh = ref fetched[i];

                if (fresh.Exists && !fresh.IsLink && fresh.IsDirectory)
                {
                    continue;
                }

                DetachStaleFileRecord(rec, in fresh, ref batchFileIndices, ref removedMarkedCount);
            }

            for (var i = 0; i < recList.Length; i++)
            {
                var rec = recList[i];
                ref readonly var fresh = ref fetched[i];

                if (fresh.Exists && !fresh.IsLink && fresh.IsDirectory)
                {
                    continue;
                }

                ApplyFileRecord(rec, in fresh, ref batchFileIndices);
            }

            if (detachedDirs is not null)
            {
                for (var i = 0; i < detachedDirs.Count; i++)
                {
                    var (dirIndex, wasAttached) = detachedDirs[i];

                    if (!wasAttached)
                    {
                        continue;
                    }

                    var parentIdx = Store.GetDirectoryRef(dirIndex).ParentIndex;

                    if (parentIdx < 0 || !Store.GetDirectoryRef(parentIdx).IsDeleted)
                    {
                        removedMarkedCount += Store.CountTopLevelMarkedInSubtreeLocked(dirIndex);
                    }
                }

                for (var i = 0; i < detachedDirs.Count; i++)
                {
                    Store.TryRecycleDirectorySlotLocked(detachedDirs[i].DirIndex);
                }
            }
        }

        return removedMarkedCount;
    }

    private void DetachDirBatchEntry(
        int rec,
        in MftUsnRecord fresh,
        Dictionary<int, int> activeFreshDirPos,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices,
        ref int removedMarkedCount,
        ref List<(int DirIndex, bool WasAttached)>? detachedDirs,
        ref Dictionary<int, bool>? detachedInBatch)
    {
        var loc = _frnLocation[rec];

        if (loc < 0 && loc != int.MinValue)
        {
            RemoveFileFromDir(~loc, _frnNameId[rec], ref batchFileIndices, ref removedMarkedCount);
            _frnLocation[rec] = int.MinValue;
            RemoveHardlinkTracking(rec);
            return;
        }

        if (loc < 0)
        {
            return;
        }

        var dirIndex = loc;
        ref var dirEntry = ref Store.GetDirectoryRef(dirIndex);
        var freshActiveDir = fresh.Exists && !fresh.IsLink && fresh.IsDirectory;
        var sameGeneration = MftLayout.SameGeneration(_frnSequence[rec], fresh.Sequence);

        if (freshActiveDir && sameGeneration && !dirEntry.IsDetached)
        {
            var chosen = TrySelectParentForDir(in fresh, out var parentRec, out var chosenName);

            if (chosen
                && !activeFreshDirPos.ContainsKey(parentRec)
                && _frnLocation[parentRec] == dirEntry.ParentIndex)
            {
                var newNameId = InternName(chosenName, dirEntry.NameId);
                dirEntry.NameId = newNameId;
                dirEntry.CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime);
                dirEntry.LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime);
                _frnNameId[rec] = newNameId;
                _frnSequence[rec] = fresh.Sequence;
                UpdateHardlinkTracking(rec, in fresh);
                return;
            }
        }

        var wasAttached = !dirEntry.IsDetached;
        Store.DetachDirectoryInArena(dirIndex);

        if (freshActiveDir && sameGeneration)
        {
            detachedInBatch ??= [];
            detachedInBatch[dirIndex] = wasAttached;
        }
        else
        {
            _frnLocation[rec] = int.MinValue;
            RemoveHardlinkTracking(rec);
            detachedDirs ??= [];
            detachedDirs.Add((dirIndex, wasAttached));
        }
    }

    private void AttachDirectoriesTopDown(
        MftUsnRecord[] fetched,
        Dictionary<int, int> activeFreshDirPos,
        Dictionary<int, bool>? detachedInBatch,
        ref List<(int DirIndex, bool WasAttached)>? detachedDirs)
    {
        var visitState = new Dictionary<int, byte>(activeFreshDirPos.Count);
        var stack = new Stack<int>();

        foreach (var startRec in activeFreshDirPos.Keys)
        {
            if (visitState.ContainsKey(startRec))
            {
                continue;
            }

            stack.Push(startRec);

            while (stack.Count > 0)
            {
                var curRec = stack.Peek();

                if (visitState.TryGetValue(curRec, out var st))
                {
                    stack.Pop();

                    if (st == 1)
                    {
                        AttachDirEntry(curRec, in fetched[activeFreshDirPos[curRec]], detachedInBatch, ref detachedDirs);
                        visitState[curRec] = 2;
                    }

                    continue;
                }

                visitState[curRec] = 1;
                ref readonly var fresh = ref fetched[activeFreshDirPos[curRec]];
                PushPendingParent(fresh.PrimaryParent, curRec, activeFreshDirPos, visitState, stack);

                if (fresh.Alternates is not null)
                {
                    for (var i = 0; i < fresh.Alternates.Count; i++)
                    {
                        PushPendingParent(fresh.Alternates[i].Parent, curRec, activeFreshDirPos, visitState, stack);
                    }
                }
            }
        }
    }

    private static void PushPendingParent(
        int parentRec,
        int curRec,
        Dictionary<int, int> activeFreshDirPos,
        Dictionary<int, byte> visitState,
        Stack<int> stack)
    {
        if (parentRec != curRec
            && activeFreshDirPos.ContainsKey(parentRec)
            && !visitState.ContainsKey(parentRec))
        {
            stack.Push(parentRec);
        }
    }

    private void AttachDirEntry(
        int rec,
        in MftUsnRecord fresh,
        Dictionary<int, bool>? detachedInBatch,
        ref List<(int DirIndex, bool WasAttached)>? detachedDirs)
    {
        if (!TrySelectParent(in fresh, out var parentRec, out var chosenName))
        {
            var oldLoc = _frnLocation[rec];

            if (oldLoc >= 0)
            {
                _frnLocation[rec] = int.MinValue;
                var wasAttached = detachedInBatch is not null && detachedInBatch.TryGetValue(oldLoc, out var wa)
                    ? wa
                    : !Store.GetDirectoryRef(oldLoc).IsDetached;
                detachedDirs ??= [];
                detachedDirs.Add((oldLoc, wasAttached));
            }

            RemoveHardlinkTracking(rec);
            return;
        }

        var parentDirIndex = _frnLocation[parentRec];
        var existingLoc = _frnLocation[rec];

        if (existingLoc >= 0)
        {
            ref var existingEntry = ref Store.GetDirectoryRef(existingLoc);

            if (WouldCreateCycle(parentDirIndex, existingLoc))
            {
                _frnLocation[rec] = int.MinValue;
                RemoveHardlinkTracking(rec);
                var wasAttached = detachedInBatch is not null && detachedInBatch.TryGetValue(existingLoc, out var wa)
                    ? wa
                    : !existingEntry.IsDetached;
                detachedDirs ??= [];
                detachedDirs.Add((existingLoc, wasAttached));
                return;
            }

            var updatedNameId = InternName(chosenName, existingEntry.NameId);
            existingEntry.NameId = updatedNameId;
            existingEntry.CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime);
            existingEntry.LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime);
            _frnNameId[rec] = updatedNameId;
            _frnSequence[rec] = fresh.Sequence;
            UpdateHardlinkTracking(rec, in fresh);

            if (existingEntry.IsDetached)
            {
                Store.AttachDirectoryInArena(parentDirIndex, existingLoc);
            }

            return;
        }

        var dirIndex = Store.AllocateDirectorySlot();
        var nameId = InternName(chosenName);
        ref var dirEntry = ref Store.GetDirectoryRef(dirIndex);
        dirEntry = new()
        {
            NameId = nameId,
            ParentIndex = parentDirIndex,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime),
            LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime),
            TotalFileCount = 0,
            TotalDirCount = 0,
            State = SpaceState.Added,
            Flags = FileEntry.UtcFileTimeFlag | DirectoryEntry.DetachedFlag,
        };

        Store.AttachDirectoryInArena(parentDirIndex, dirIndex);
        _frnLocation[rec] = dirIndex;
        _frnNameId[rec] = nameId;
        _frnSequence[rec] = fresh.Sequence;
        UpdateHardlinkTracking(rec, in fresh);
    }

    private void DetachStaleFileRecord(
        int rec,
        in MftUsnRecord fresh,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices,
        ref int removedMarkedCount)
    {
        var oldLoc = _frnLocation[rec];

        if (oldLoc >= 0 || oldLoc == int.MinValue)
        {
            return;
        }

        var oldParentDirIndex = ~oldLoc;
        var sameInPlace = fresh.Exists
            && !fresh.IsLink
            && !fresh.IsDirectory
            && MftLayout.SameGeneration(_frnSequence[rec], fresh.Sequence)
            && TrySelectParent(in fresh, out var chosenParentRec, out var chosenName)
            && _frnLocation[chosenParentRec] == oldParentDirIndex
            && Store.Names.Equals(_frnNameId[rec], chosenName, StringComparison.Ordinal);

        if (sameInPlace)
        {
            return;
        }

        RemoveFileFromDir(oldParentDirIndex, _frnNameId[rec], ref batchFileIndices, ref removedMarkedCount);
        _frnLocation[rec] = int.MinValue;
        RemoveHardlinkTracking(rec);
    }

    private void ApplyFileRecord(int rec, in MftUsnRecord fresh, ref Dictionary<int, DirBatchFileIndex>? batchFileIndices)
    {
        var oldLoc = _frnLocation[rec];
        var wasCountedFile = oldLoc < 0 && oldLoc != int.MinValue;
        var oldParentDirIndex = wasCountedFile ? ~oldLoc : -1;

        var chosenParentRec = -1;
        var chosenName = string.Empty;
        var newActiveFile = fresh.Exists
            && !fresh.IsLink
            && !fresh.IsDirectory
            && TrySelectParent(in fresh, out chosenParentRec, out chosenName);
        var newParentDirIndex = newActiveFile ? _frnLocation[chosenParentRec] : -1;

        if (wasCountedFile)
        {
            ref var parentEntry = ref Store.GetDirectoryRef(oldParentDirIndex);
            var oldNameId = _frnNameId[rec];
            var fileIdx = FindSlotByNameId(oldParentDirIndex, in parentEntry, oldNameId, ref batchFileIndices, out _);

            if (fileIdx >= 0)
            {
                var span = Store.GetWritableSpan(parentEntry.FileStart, parentEntry.FileCount);
                ref var fileEntry = ref span[fileIdx];
                var oldSize = fileEntry.Size;
                fileEntry.Size = fresh.Size;
                fileEntry.CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime);
                fileEntry.LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime);
                fileEntry.Flags = FileEntry.UtcFileTimeFlag;
                _frnSequence[rec] = fresh.Sequence;
                UpdateHardlinkTracking(rec, in fresh);

                var delta = fresh.Size - oldSize;

                if (delta != 0)
                {
                    parentEntry.Size += delta;
                    Store.PropagateDirDelta(oldParentDirIndex, delta, 0, 0);
                }

                return;
            }

            _frnLocation[rec] = int.MinValue;
            RemoveHardlinkTracking(rec);
        }

        if (!newActiveFile)
        {
            RemoveHardlinkTracking(rec);
            return;
        }

        ref var targetParent = ref Store.GetDirectoryRef(newParentDirIndex);
        var existingIdx = FindSlotByName(newParentDirIndex, in targetParent, chosenName, ref batchFileIndices, out var targetIndexMap);

        if (existingIdx >= 0)
        {
            var span = Store.GetWritableSpan(targetParent.FileStart, targetParent.FileCount);
            ref var existing = ref span[existingIdx];
            var oldSize = existing.Size;
            existing.Size = fresh.Size;
            existing.CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime);
            existing.LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime);
            existing.Flags = FileEntry.UtcFileTimeFlag;
            _frnLocation[rec] = ~newParentDirIndex;
            _frnNameId[rec] = existing.NameId;
            _frnSequence[rec] = fresh.Sequence;
            UpdateHardlinkTracking(rec, in fresh);

            var delta = fresh.Size - oldSize;

            if (delta != 0)
            {
                targetParent.Size += delta;
                Store.PropagateDirDelta(newParentDirIndex, delta, 0, 0);
            }

            return;
        }

        var nameId = InternName(chosenName);
        var entry = new FileEntry
        {
            Size = fresh.Size,
            CreationRaw = TimestampEncoding.FromMftFileTime(fresh.CreationFileTime),
            LastAccessRaw = TimestampEncoding.FromMftFileTime(fresh.LastAccessFileTime),
            NameId = nameId,
            State = SpaceState.Added,
            Flags = FileEntry.UtcFileTimeFlag,
        };

        var newSlot = Store.AppendFileAtDirectory(newParentDirIndex, in entry);

        if (targetIndexMap is not null)
        {
            targetIndexMap.ByNameId[nameId] = newSlot;
            targetIndexMap.ByName[chosenName] = newSlot;
        }

        _frnLocation[rec] = ~newParentDirIndex;
        _frnNameId[rec] = nameId;
        _frnSequence[rec] = fresh.Sequence;
        UpdateHardlinkTracking(rec, in fresh);
    }

    private void RemoveFileFromDir(
        int dirIndex,
        int nameId,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices,
        ref int removedMarkedCount)
    {
        ref readonly var dirEntry = ref Store.GetDirectoryRef(dirIndex);
        var slotIndex = FindSlotByNameId(dirIndex, in dirEntry, nameId, ref batchFileIndices, out var dirIndexMap);

        if (slotIndex < 0)
        {
            return;
        }

        var wasMarked = !dirEntry.IsDeleted && Store.GetSpan(dirEntry.FileStart, dirEntry.FileCount)[slotIndex].IsDeleted;
        var removedName = dirIndexMap is null ? null : Store.Names.GetString(nameId);

        if (!Store.TryRemoveFileAtSlotSwap(dirIndex, slotIndex, out _, out var swappedNameId))
        {
            return;
        }

        if (wasMarked)
        {
            removedMarkedCount++;
        }

        if (dirIndexMap is null)
        {
            return;
        }

        dirIndexMap.ByNameId.Remove(nameId);
        dirIndexMap.ByName.Remove(removedName!);

        if (swappedNameId >= 0)
        {
            dirIndexMap.ByNameId[swappedNameId] = slotIndex;
            dirIndexMap.ByName[Store.Names.GetString(swappedNameId)] = slotIndex;
        }
    }

    private int FindSlotByNameId(
        int dirIndex,
        in DirectoryEntry dirEntry,
        int nameId,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices,
        out DirBatchFileIndex? dirIndexMap)
    {
        if (dirEntry.FileCount <= 0)
        {
            dirIndexMap = null;
            return -1;
        }

        dirIndexMap = GetOrBuildBatchFileIndex(dirIndex, in dirEntry, ref batchFileIndices);

        if (dirIndexMap is not null)
        {
            return dirIndexMap.ByNameId.TryGetValue(nameId, out var found) ? found : -1;
        }

        var span = Store.GetSpan(dirEntry.FileStart, dirEntry.FileCount);

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i].NameId == nameId)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSlotByName(
        int dirIndex,
        in DirectoryEntry dirEntry,
        string name,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices,
        out DirBatchFileIndex? dirIndexMap)
    {
        if (dirEntry.FileCount <= 0)
        {
            dirIndexMap = null;
            return -1;
        }

        dirIndexMap = GetOrBuildBatchFileIndex(dirIndex, in dirEntry, ref batchFileIndices);

        if (dirIndexMap is not null)
        {
            return dirIndexMap.ByName.TryGetValue(name, out var found) ? found : -1;
        }

        var span = Store.GetSpan(dirEntry.FileStart, dirEntry.FileCount);

        for (var i = 0; i < span.Length; i++)
        {
            if (Store.Names.Equals(span[i].NameId, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private DirBatchFileIndex? GetOrBuildBatchFileIndex(
        int dirIndex,
        in DirectoryEntry dirEntry,
        ref Dictionary<int, DirBatchFileIndex>? batchFileIndices)
    {
        if (batchFileIndices is not null && batchFileIndices.TryGetValue(dirIndex, out var existing))
        {
            return existing;
        }

        if (dirEntry.FileCount <= BatchFileIndexThreshold)
        {
            return null;
        }

        var span = Store.GetSpan(dirEntry.FileStart, dirEntry.FileCount);
        var byNameId = new Dictionary<int, int>(span.Length);
        var byName = new Dictionary<string, int>(span.Length, StringComparer.Ordinal);

        for (var i = 0; i < span.Length; i++)
        {
            var id = span[i].NameId;
            byNameId[id] = i;
            byName[Store.Names.GetString(id)] = i;
        }

        var created = new DirBatchFileIndex(byNameId, byName);
        batchFileIndices ??= [];
        batchFileIndices[dirIndex] = created;
        return created;
    }

    private bool TrySelectParentForDir(in MftUsnRecord fresh, out int parentRec, out string name)
    {
        return TrySelectParent(in fresh, out parentRec, out name);
    }

    private bool TrySelectParent(in MftUsnRecord fresh, out int parentRec, out string name)
    {
        if (IsUsableParent(fresh.PrimaryParent, fresh.PrimaryParentSequence))
        {
            parentRec = fresh.PrimaryParent;
            name = fresh.PrimaryName;
            return true;
        }

        if (fresh.Alternates is not null)
        {
            for (var i = 0; i < fresh.Alternates.Count; i++)
            {
                var alt = fresh.Alternates[i];

                if (IsUsableParent(alt.Parent, alt.ParentSequence))
                {
                    parentRec = alt.Parent;
                    name = alt.Name;
                    return true;
                }
            }
        }

        parentRec = -1;
        name = string.Empty;
        return false;
    }

    private bool IsUsableParent(int parentRec, ushort parentSeq)
    {
        if (parentRec < 0 || parentRec >= _frnLocation.Length)
        {
            return false;
        }

        var parentDirIndex = _frnLocation[parentRec];

        if (parentDirIndex < 0)
        {
            return false;
        }

        if (Store.GetDirectoryRef(parentDirIndex).IsDetached)
        {
            return false;
        }

        return MftLayout.SameGeneration(_frnSequence[parentRec], parentSeq);
    }

    private bool WouldCreateCycle(int candidateParentDirIndex, int dirIndex)
    {
        var cur = candidateParentDirIndex;

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (cur < 0)
            {
                return false;
            }

            if (cur == dirIndex)
            {
                return true;
            }

            cur = Store.GetDirectoryRef(cur).ParentIndex;
        }

        return true;
    }

    private int InternName(string name, int existingNameId = -1)
    {
        if (existingNameId >= 0 && Store.Names.Equals(existingNameId, name, StringComparison.Ordinal))
        {
            return existingNameId;
        }

        _postFreezeNames ??= new(StringComparer.Ordinal);

        if (_postFreezeNames.TryGetValue(name, out var cachedId))
        {
            return cachedId;
        }

        var id = Store.Names.Intern(name);

        if (_postFreezeNames.Count < MaxPostFreezeNameCache)
        {
            _postFreezeNames[name] = id;
        }

        return id;
    }

    private void UpdateHardlinkTracking(int rec, in MftUsnRecord fresh)
    {
        var extraCount = fresh.TotalNameCount > 1 ? fresh.TotalNameCount - 1 : 0;
        var fileSize = fresh.IsDirectory ? 0L : fresh.Size;

        if (_hardlinks.TryGetValue(rec, out var prev))
        {
            ExtraNames -= prev.ExtraCount;
            ExtraNameBytes -= prev.FileSize * prev.ExtraCount;

            if (extraCount <= 0)
            {
                _hardlinks.Remove(rec);
                return;
            }
        }
        else if (extraCount <= 0)
        {
            return;
        }

        HardlinkTarget[] links;

        if (fresh.Alternates is { Count: > 0 } alts)
        {
            links = new HardlinkTarget[1 + alts.Count];
            links[0] = new(fresh.PrimaryParent, fresh.PrimaryParentSequence, InternName(fresh.PrimaryName));

            for (var i = 0; i < alts.Count; i++)
            {
                var alt = alts[i];
                links[i + 1] = new(alt.Parent, alt.ParentSequence, InternName(alt.Name));
            }
        }
        else
        {
            links = [new(fresh.PrimaryParent, fresh.PrimaryParentSequence, InternName(fresh.PrimaryName))];
        }

        _hardlinks[rec] = new(extraCount, fileSize, links);
        ExtraNames += extraCount;
        ExtraNameBytes += fileSize * extraCount;
    }

    private void RemoveHardlinkTracking(int rec)
    {
        if (_hardlinks.Remove(rec, out var prev))
        {
            ExtraNames -= prev.ExtraCount;
            ExtraNameBytes -= prev.FileSize * prev.ExtraCount;
        }
    }

    private bool IsDirInSubtreeLocked(int candidateDirIndex, int subtreeRootDirIndex)
    {
        var cur = candidateDirIndex;

        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (cur == subtreeRootDirIndex)
            {
                return true;
            }

            if (cur <= 0)
            {
                return false;
            }

            ref readonly var d = ref Store.GetDirectoryRef(cur);

            if (d.IsDetached)
            {
                return false;
            }

            cur = d.ParentIndex;
        }

        return false;
    }

    private DirectorySpace MaterializeSubtreeWithRehomeLocked(
        int rootSrcDirIndex,
        DirectoryInfo directory,
        List<RehomedLink> rehomed)
    {
        var subStore = new ScanTreeStore(16, singleWriter: true);
        var nameCache = LocalNameCache.Create();
        var dirMap = new Dictionary<int, int>();
        var queue = new Queue<(int SrcDirIdx, int DstParentIdx, int OverrideNameId)>();

        var rootNameId = subStore.Names.Intern(directory.Name);
        queue.Enqueue((rootSrcDirIndex, -1, rootNameId));

        CloneQueuedDirectoriesLocked(subStore, nameCache, dirMap, queue);

        for (var i = 0; i < rehomed.Count; i++)
        {
            var item = rehomed[i];

            if (!dirMap.TryGetValue(item.TargetParentDirIndex, out var dstParentIdx))
            {
                continue;
            }

            if (item.CurrentLoc >= 0)
            {
                if (!dirMap.ContainsKey(item.CurrentLoc))
                {
                    var rehomedDirNameId = nameCache.Intern(subStore.Names, Store.Names.GetUtf8(item.NameId));
                    queue.Enqueue((item.CurrentLoc, dstParentIdx, rehomedDirNameId));
                    CloneQueuedDirectoriesLocked(subStore, nameCache, dirMap, queue);
                }

                continue;
            }

            var oldParentDirIdx = ~item.CurrentLoc;

            if (!TryFindFileEntryByNameIdLocked(oldParentDirIdx, _frnNameId[item.Record], out var srcFile))
            {
                continue;
            }

            var rehomedFile = srcFile;
            rehomedFile.NameId = nameCache.Intern(subStore.Names, Store.Names.GetUtf8(item.NameId));
            ref var dstParent = ref subStore.GetDirectoryRef(dstParentIdx);
            subStore.AppendFile(ref dstParent.FileStart, ref dstParent.FileCount, in rehomedFile);
            dstParent.Size += rehomedFile.Size;
        }

        for (var idx = subStore.DirectoryCount - 1; idx >= 0; idx--)
        {
            ref var d = ref subStore.GetDirectoryRef(idx);
            var totalSize = d.Size;
            var totalFiles = d.FileCount;
            var totalDirs = d.SubDirCount;

            for (var child = d.FirstChildDir; child >= 0;)
            {
                ref readonly var childEntry = ref subStore.GetDirectoryRef(child);
                totalSize += childEntry.TotalSize;
                totalFiles += childEntry.TotalFileCount;
                totalDirs += childEntry.TotalDirCount;
                child = childEntry.NextSiblingDir;
            }

            d.TotalSize = totalSize;
            d.TotalFileCount = totalFiles;
            d.TotalDirCount = totalDirs;
        }

        subStore.Names.Freeze();
        return subStore.GetOrCreateDirectoryFacade(0);
    }

    private void CloneQueuedDirectoriesLocked(
        ScanTreeStore subStore,
        LocalNameCache nameCache,
        Dictionary<int, int> dirMap,
        Queue<(int SrcDirIdx, int DstParentIdx, int OverrideNameId)> queue)
    {
        while (queue.Count > 0)
        {
            var (srcDirIdx, dstParentIdx, overrideNameId) = queue.Dequeue();

            if (dirMap.ContainsKey(srcDirIdx))
            {
                continue;
            }

            ref readonly var srcDir = ref Store.GetDirectoryRef(srcDirIdx);
            var dstDirIdx = subStore.AllocateDirectorySlot();
            dirMap[srcDirIdx] = dstDirIdx;

            var nameId = overrideNameId >= 0
                ? overrideNameId
                : nameCache.Intern(subStore.Names, Store.Names.GetUtf8(srcDir.NameId));

            ref var dstDir = ref subStore.GetDirectoryRef(dstDirIdx);
            dstDir = new()
            {
                NameId = nameId,
                ParentIndex = dstParentIdx,
                FirstChildDir = -1,
                NextSiblingDir = -1,
                CreationRaw = srcDir.CreationRaw,
                LastAccessRaw = srcDir.LastAccessRaw,
                TotalFileCount = -1,
                TotalDirCount = -1,
                State = srcDir.State,
                Flags = (byte)(srcDir.Flags & ~DirectoryEntry.DetachedFlag),
            };

            if (dstParentIdx >= 0)
            {
                subStore.LinkChildDirectory(dstParentIdx, dstDirIdx);
            }

            if (srcDir.FileCount > 0)
            {
                var srcFiles = Store.GetSpan(srcDir.FileStart, srcDir.FileCount);
                var copied = new FileEntry[srcFiles.Length];
                long ownSize = 0;

                for (var i = 0; i < srcFiles.Length; i++)
                {
                    var f = srcFiles[i];
                    f.NameId = nameCache.Intern(subStore.Names, Store.Names.GetUtf8(f.NameId));
                    copied[i] = f;
                    ownSize += f.Size;
                }

                var handle = subStore.WriteLarge(copied);
                ref var updatedDst = ref subStore.GetDirectoryRef(dstDirIdx);
                updatedDst.FileStart = handle;
                updatedDst.FileCount = copied.Length;
                updatedDst.Size = ownSize;
            }

            for (var child = srcDir.FirstChildDir; child >= 0;)
            {
                ref readonly var childEntry = ref Store.GetDirectoryRef(child);

                if (!childEntry.IsDetached)
                {
                    queue.Enqueue((child, dstDirIdx, -1));
                }

                child = childEntry.NextSiblingDir;
            }
        }
    }

    private bool TryFindFileEntryByNameIdLocked(int dirIndex, int nameId, out FileEntry entry)
    {
        ref readonly var dirEntry = ref Store.GetDirectoryRef(dirIndex);

        if (dirEntry.FileCount > 0)
        {
            var files = Store.GetSpan(dirEntry.FileStart, dirEntry.FileCount);

            for (var i = 0; i < files.Length; i++)
            {
                if (files[i].NameId == nameId)
                {
                    entry = files[i];
                    return true;
                }
            }
        }

        entry = default;
        return false;
    }

    private readonly record struct HardlinkTarget(int ParentRec, ushort ParentSequence, int NameId);

    private readonly record struct HardlinkRecord(int ExtraCount, long FileSize, HardlinkTarget[] Links);

    private readonly record struct RehomedLink(int Record, int CurrentLoc, int TargetParentDirIndex, int NameId);

    private void EnsureRecordCapacity(int maxRec)
    {
        if (maxRec < _frnLocation.Length)
        {
            return;
        }

        var oldLength = _frnLocation.Length;
        var nextLength = Math.Max(oldLength * 2, maxRec + 1);
        Array.Resize(ref _frnLocation, nextLength);
        Array.Fill(_frnLocation, int.MinValue, oldLength, nextLength - oldLength);
        Array.Resize(ref _frnNameId, nextLength);
        Array.Resize(ref _frnSequence, nextLength);
    }

    private MftUsnRefreshSummary FailSummary(long startTick, MftUsnErrorKind kind, int win32Error, string reason)
    {
        return new(
            _eventsBuffer.Count,
            0,
            _openDirtyFrns.Count,
            Stopwatch.GetElapsedTime(startTick).TotalMilliseconds,
            kind,
            win32Error,
            reason);
    }

    private sealed class DirBatchFileIndex(Dictionary<int, int> byNameId, Dictionary<string, int> byName)
    {
        public Dictionary<int, int> ByNameId { get; } = byNameId;

        public Dictionary<string, int> ByName { get; } = byName;
    }
}
