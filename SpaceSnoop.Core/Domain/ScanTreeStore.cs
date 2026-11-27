using System.Runtime.CompilerServices;

namespace SpaceSnoop.Core.Domain;

internal sealed class ScanTreeStore
{
    public const int ChunkSize = 16_384;
    public const int DirChunkSize = 16_384;
    internal const int ChunkShift = 14;
    internal const int DirChunkShift = 14;
    internal const int MaxRegularChunks = 1 << (31 - ChunkShift);
    private const int ChunkMask = ChunkSize - 1;
    private const int DirChunkMask = DirChunkSize - 1;
    private const int InitialDirFacadePruneThreshold = 4096;
    private const int MaxTreeDepth = 512;

    private readonly Lock _slabLock = new();
    private readonly Lock _facadeLock = new();
    private readonly int _maxRegularChunks;
    private FileEntry[][] _chunkTable = new FileEntry[64][];
    private int _chunkCount;
    private FileEntry[][] _largeTable = new FileEntry[16][];
    private int _largeCount;
    private DirectoryEntry[][] _dirChunks = new DirectoryEntry[16][];
    private int _dirChunkCount;
    private int _dirCount;
    private Stack<int>? _freeDirSlots;
    private ConditionalWeakTable<DirectorySpace, Dictionary<int, FileSpace>>? _facades;
    private Dictionary<int, WeakReference<DirectorySpace>>? _dirFacades;
    private int _dirFacadePruneThreshold = InitialDirFacadePruneThreshold;
    private Dictionary<int, SpaceBase>? _syntheticParents;

    public NameTable Names { get; }

    public int DirectoryCount => Volatile.Read(ref _dirCount);

    internal int MaterializedDirectoryFacadeCount
    {
        get
        {
            lock (_facadeLock)
            {
                return _dirFacades?.Count ?? 0;
            }
        }
    }

    internal Lock FacadeLock => _facadeLock;

    public ScanTreeStore(int shardCount = 1, bool singleWriter = false)
        : this(shardCount, singleWriter, MaxRegularChunks, initialNameSlotsPerShard: 0)
    {
    }

    internal ScanTreeStore(int shardCount, bool singleWriter, int maxRegularChunks)
        : this(shardCount, singleWriter, maxRegularChunks, initialNameSlotsPerShard: 0)
    {
    }

    private ScanTreeStore(int shardCount, bool singleWriter, int maxRegularChunks, int initialNameSlotsPerShard)
    {
        Names = new(shardCount, singleWriter, initialNameSlotsPerShard);
        _maxRegularChunks = maxRegularChunks;
    }

    public ReadOnlySpan<FileEntry> GetSpan(int fileStart, int fileCount)
    {
        if (fileCount == 0)
        {
            return ReadOnlySpan<FileEntry>.Empty;
        }

        if (fileStart >= 0)
        {
            var chunkIndex = fileStart >>> ChunkShift;
            var offset = fileStart & ChunkMask;
            return Volatile.Read(ref _chunkTable)[chunkIndex].AsSpan(offset, fileCount);
        }

        var largeIndex = ~fileStart;
        return Volatile.Read(ref _largeTable)[largeIndex].AsSpan(0, fileCount);
    }

    public Span<FileEntry> GetWritableSpan(int fileStart, int fileCount)
    {
        if (fileCount == 0)
        {
            return Span<FileEntry>.Empty;
        }

        if (fileStart >= 0)
        {
            var chunkIndex = fileStart >>> ChunkShift;
            var offset = fileStart & ChunkMask;
            return Volatile.Read(ref _chunkTable)[chunkIndex].AsSpan(offset, fileCount);
        }

        var largeIndex = ~fileStart;
        return Volatile.Read(ref _largeTable)[largeIndex].AsSpan(0, fileCount);
    }

    public void PreallocateFileChunks(int chunkCount)
    {
        if (chunkCount <= 0)
        {
            return;
        }

        lock (_slabLock)
        {
            if (chunkCount > _maxRegularChunks)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount));
            }

            if (chunkCount <= _chunkCount)
            {
                return;
            }

            if (chunkCount > _chunkTable.Length)
            {
                var nextCap = _chunkTable.Length;

                while (nextCap < chunkCount)
                {
                    nextCap *= 2;
                }

                var next = new FileEntry[nextCap][];
                Array.Copy(_chunkTable, next, _chunkCount);
                Volatile.Write(ref _chunkTable, next);
            }

            for (var i = _chunkCount; i < chunkCount; i++)
            {
                Volatile.Write(ref _chunkTable[i], new FileEntry[ChunkSize]);
            }

            _chunkCount = chunkCount;
        }
    }

    public bool TryAllocateChunk(out int chunkIndex, out FileEntry[]? chunk)
    {
        lock (_slabLock)
        {
            if (_chunkCount >= _maxRegularChunks)
            {
                chunkIndex = -1;
                chunk = null;
                return false;
            }

            chunkIndex = _chunkCount;
            chunk = new FileEntry[ChunkSize];

            if (chunkIndex == _chunkTable.Length)
            {
                var next = new FileEntry[_chunkTable.Length * 2][];
                Array.Copy(_chunkTable, next, _chunkTable.Length);
                Volatile.Write(ref _chunkTable, next);
            }

            Volatile.Write(ref _chunkTable[chunkIndex], chunk);
            _chunkCount = chunkIndex + 1;
            return true;
        }
    }

    public int AllocateLargeArray(int capacity, out FileEntry[] array)
    {
        array = new FileEntry[capacity];

        lock (_slabLock)
        {
            return RegisterLargeArrayLocked(array);
        }
    }

    public int WriteLarge(ReadOnlySpan<FileEntry> source)
    {
        var handle = AllocateLargeArray(source.Length, out var array);
        source.CopyTo(array);
        return handle;
    }

    public void AppendFile(ref int fileStart, ref int fileCount, in FileEntry entry)
    {
        lock (_slabLock)
        {
            if (fileStart < 0)
            {
                var largeIndex = ~fileStart;
                var existing = _largeTable[largeIndex];

                if (fileCount < existing.Length)
                {
                    existing[fileCount] = entry;
                    fileCount++;
                    return;
                }
            }

            var newCapacity = fileCount == 0 ? 4 : Math.Max(fileCount + 1, fileCount * 2);
            var target = new FileEntry[newCapacity];

            if (fileCount > 0)
            {
                GetSpan(fileStart, fileCount).CopyTo(target);
            }

            target[fileCount] = entry;

            if (fileStart < 0)
            {
                Volatile.Write(ref _largeTable[~fileStart], target);
            }
            else
            {
                fileStart = RegisterLargeArrayLocked(target);
            }

            fileCount++;
        }
    }

    public void AppendFiles(ref int fileStart, ref int fileCount, ReadOnlySpan<FileEntry> entries)
    {
        if (entries.IsEmpty)
        {
            return;
        }

        lock (_slabLock)
        {
            var required = fileCount + entries.Length;

            if (fileStart < 0)
            {
                var largeIndex = ~fileStart;
                var existing = _largeTable[largeIndex];

                if (required <= existing.Length)
                {
                    entries.CopyTo(existing.AsSpan(fileCount));
                    fileCount = required;
                    return;
                }

                var newCapacity = Math.Max(required, existing.Length * 2);
                var target = new FileEntry[newCapacity];

                if (fileCount > 0)
                {
                    existing.AsSpan(0, fileCount).CopyTo(target);
                }

                entries.CopyTo(target.AsSpan(fileCount));
                Volatile.Write(ref _largeTable[largeIndex], target);
                fileCount = required;
                return;
            }

            var initialTarget = new FileEntry[required];

            if (fileCount > 0)
            {
                GetSpan(fileStart, fileCount).CopyTo(initialTarget);
            }

            entries.CopyTo(initialTarget.AsSpan(fileCount));
            fileStart = RegisterLargeArrayLocked(initialTarget);
            fileCount = required;
        }
    }

    public FileSpace GetOrCreateFacade(DirectorySpace dir, int index)
    {
        lock (_facadeLock)
        {
            return GetOrCreateFacadeLocked(dir, index);
        }
    }

    public FileSpace GetOrCreateFacadeLocked(DirectorySpace dir, int index)
    {
        _facades ??= new();

        if (!_facades.TryGetValue(dir, out var map))
        {
            map = [];
            _facades.Add(dir, map);
        }

        if (!map.TryGetValue(index, out var facade))
        {
            facade = new(dir, index);
            map[index] = facade;
        }

        return facade;
    }

    public void RegisterFacade(DirectorySpace dir, int index, FileSpace existing)
    {
        lock (_facadeLock)
        {
            _facades ??= new();

            if (!_facades.TryGetValue(dir, out var map))
            {
                map = [];
                _facades.Add(dir, map);
            }

            map[index] = existing;
        }
    }

    public void EnsureDirectoryCapacity(int requiredCount)
    {
        if (requiredCount <= 0)
        {
            return;
        }

        var requiredChunks = (int)(((long)requiredCount + DirChunkMask) >> DirChunkShift);

        if (requiredChunks <= Volatile.Read(ref _dirChunkCount))
        {
            return;
        }

        lock (_facadeLock)
        {
            EnsureDirectoryCapacityLocked(requiredCount);
        }
    }

    public int AllocateDirectorySlot()
    {
        lock (_facadeLock)
        {
            int dirIndex;

            if (_freeDirSlots is { Count: > 0 })
            {
                dirIndex = _freeDirSlots.Pop();
            }
            else
            {
                dirIndex = _dirCount;
                var nextCount = dirIndex + 1;
                EnsureDirectoryCapacityLocked(nextCount);
                Volatile.Write(ref _dirCount, nextCount);
            }

            GetDirectoryRef(dirIndex) = new()
            {
                ParentIndex = -1,
                FirstChildDir = -1,
                NextSiblingDir = -1,
                TotalFileCount = -1,
                TotalDirCount = -1,
                State = SpaceState.Added,
            };
            return dirIndex;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref DirectoryEntry GetDirectoryRef(int dirIndex)
    {
        var chunkIndex = dirIndex >>> DirChunkShift;
        var offset = dirIndex & DirChunkMask;
        return ref Volatile.Read(ref _dirChunks)[chunkIndex][offset];
    }

    public DirectorySpace GetOrCreateDirectoryFacade(int dirIndex)
    {
        lock (_facadeLock)
        {
            return GetOrCreateDirectoryFacadeLocked(dirIndex);
        }
    }

    public DirectorySpace GetOrCreateDirectoryFacadeLocked(int dirIndex)
    {
        _dirFacades ??= new();

        if (_dirFacades.TryGetValue(dirIndex, out var weak) && weak.TryGetTarget(out var existing))
        {
            return existing;
        }

        var created = new DirectorySpace(this, dirIndex);

        if (weak is null)
        {
            _dirFacades[dirIndex] = new(created);

            if (_dirFacades.Count > _dirFacadePruneThreshold)
            {
                PruneDeadDirectoryFacadesLocked();
            }
        }
        else
        {
            weak.SetTarget(created);
        }

        return created;
    }

    public void SetSyntheticParent(int dirIndex, SpaceBase? parent)
    {
        lock (_facadeLock)
        {
            if (parent is not null)
            {
                if (_syntheticParents is null)
                {
                    var created = new Dictionary<int, SpaceBase> { [dirIndex] = parent };
                    Volatile.Write(ref _syntheticParents, created);
                    return;
                }

                _syntheticParents[dirIndex] = parent;
                return;
            }

            _syntheticParents?.Remove(dirIndex);
        }
    }

    public SpaceBase? GetSyntheticParent(int dirIndex)
    {
        if (Volatile.Read(ref _syntheticParents) is null)
        {
            return null;
        }

        lock (_facadeLock)
        {
            return GetSyntheticParentLocked(dirIndex);
        }
    }

    public void LinkChildDirectory(int parentIndex, int childIndex)
    {
        lock (_facadeLock)
        {
            LinkChildDirectoryLocked(parentIndex, childIndex);
        }
    }

    public bool UnlinkChildDirectory(int parentIndex, int childIndex)
    {
        lock (_facadeLock)
        {
            return UnlinkChildDirectoryLocked(parentIndex, childIndex);
        }
    }

    public bool RemoveDirectory(
        int parentIndex,
        DirectorySpace subDir,
        out long removedTotalSize,
        out int removedTotalFiles,
        out int removedTotalDirs)
    {
        lock (_facadeLock)
        {
            if (!subDir.IsAttachedToLocked(this, parentIndex) || !UnlinkChildDirectoryLocked(parentIndex, subDir.DirIndex))
            {
                removedTotalSize = 0;
                removedTotalFiles = 0;
                removedTotalDirs = 0;
                return false;
            }

            ref var subEntry = ref GetDirectoryRef(subDir.DirIndex);
            subEntry.SetDetached(true);
            removedTotalSize = subEntry.TotalSize;

            if (subEntry.TotalFileCount >= 0 && subEntry.TotalDirCount >= 0)
            {
                removedTotalFiles = subEntry.TotalFileCount;
                removedTotalDirs = subEntry.TotalDirCount;
            }
            else
            {
                ComputeSubtreeCountsLocked(subDir.DirIndex, out var computedFiles, out var computedDirs);
                removedTotalFiles = subEntry.TotalFileCount >= 0 ? subEntry.TotalFileCount : computedFiles;
                removedTotalDirs = subEntry.TotalDirCount >= 0 ? subEntry.TotalDirCount : computedDirs;
            }

            PropagateDirDeltaLocked(parentIndex, -removedTotalSize, -removedTotalFiles, -(removedTotalDirs + 1));
            return true;
        }
    }

    public bool RemoveFileAtDirectory(DirectorySpace dir, int dirIndex, FileSpace file, out long removedSize)
    {
        lock (_facadeLock)
        {
            ref var entry = ref GetDirectoryRef(dirIndex);

            if (!RemoveFileLocked(dir, entry.FileStart, ref entry.FileCount, file, out removedSize))
            {
                return false;
            }

            entry.Size -= removedSize;
            PropagateDirDeltaLocked(dirIndex, -removedSize, fileDelta: -1, dirDelta: 0);
            return true;
        }
    }

    public FileSpace AppendFileAtDirectory(DirectorySpace dir, int dirIndex, in FileEntry fileEntry)
    {
        lock (_facadeLock)
        {
            ref var dirEntry = ref GetDirectoryRef(dirIndex);
            var newIndex = dirEntry.FileCount;
            AppendFile(ref dirEntry.FileStart, ref dirEntry.FileCount, in fileEntry);
            dirEntry.Size += fileEntry.Size;
            PropagateDirDeltaLocked(dirIndex, fileEntry.Size, fileDelta: 1, dirDelta: 0);
            return GetOrCreateFacadeLocked(dir, newIndex);
        }
    }

    public void AppendFilesAtDirectory(int dirIndex, ReadOnlySpan<FileEntry> entries, long addedSize)
    {
        if (entries.IsEmpty)
        {
            return;
        }

        lock (_facadeLock)
        {
            ref var dirEntry = ref GetDirectoryRef(dirIndex);
            AppendFiles(ref dirEntry.FileStart, ref dirEntry.FileCount, entries);
            dirEntry.Size += addedSize;
            PropagateDirDeltaLocked(dirIndex, addedSize, fileDelta: entries.Length, dirDelta: 0);
        }
    }

    public void PropagateDirDelta(int startDirIndex, long sizeDelta, int fileDelta, int dirDelta)
    {
        lock (_facadeLock)
        {
            PropagateDirDeltaLocked(startDirIndex, sizeDelta, fileDelta, dirDelta);
        }
    }

    public void DeleteDirectorySubtree(int dirIndex)
    {
        lock (_facadeLock)
        {
            var stack = new Stack<int>();
            stack.Push(dirIndex);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                ref var entry = ref GetDirectoryRef(cur);

                if (entry.FileCount > 0)
                {
                    var files = GetWritableSpan(entry.FileStart, entry.FileCount);

                    for (var i = 0; i < files.Length; i++)
                    {
                        ref var file = ref files[i];

                        if (file.State != SpaceState.Deleted && file.State != SpaceState.Error)
                        {
                            file.State = SpaceState.Deleted;
                        }
                    }
                }

                for (var child = entry.FirstChildDir; child >= 0;)
                {
                    ref var childEntry = ref GetDirectoryRef(child);
                    var next = childEntry.NextSiblingDir;

                    if (childEntry.State != SpaceState.Deleted && childEntry.State != SpaceState.Error)
                    {
                        childEntry.State = SpaceState.Deleted;
                        stack.Push(child);
                    }

                    child = next;
                }
            }
        }
    }

    public void RestoreDirectorySubtree(int dirIndex)
    {
        lock (_facadeLock)
        {
            var stack = new Stack<int>();
            stack.Push(dirIndex);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                ref var entry = ref GetDirectoryRef(cur);
                var allDeleted = true;

                for (var child = entry.FirstChildDir; child >= 0;)
                {
                    ref readonly var childEntry = ref GetDirectoryRef(child);

                    if (!childEntry.IsDeleted)
                    {
                        allDeleted = false;
                        break;
                    }

                    child = childEntry.NextSiblingDir;
                }

                if (!allDeleted)
                {
                    continue;
                }

                var files = entry.FileCount > 0
                    ? GetWritableSpan(entry.FileStart, entry.FileCount)
                    : Span<FileEntry>.Empty;

                for (var i = 0; i < files.Length; i++)
                {
                    if (!files[i].IsDeleted)
                    {
                        allDeleted = false;
                        break;
                    }
                }

                if (!allDeleted)
                {
                    continue;
                }

                for (var i = 0; i < files.Length; i++)
                {
                    ref var file = ref files[i];

                    if (file.State == SpaceState.Deleted)
                    {
                        file.State = SpaceState.Added;
                    }
                }

                for (var child = entry.FirstChildDir; child >= 0;)
                {
                    ref var childEntry = ref GetDirectoryRef(child);

                    if (childEntry.State == SpaceState.Deleted)
                    {
                        childEntry.State = SpaceState.Added;
                    }

                    stack.Push(child);
                    child = childEntry.NextSiblingDir;
                }
            }
        }
    }

    public void RestoreDirectoryRecursive(int dirIndex)
    {
        lock (_facadeLock)
        {
            var stack = new Stack<int>();
            stack.Push(dirIndex);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                ref var entry = ref GetDirectoryRef(cur);

                if (entry.FileCount > 0)
                {
                    var files = GetWritableSpan(entry.FileStart, entry.FileCount);

                    for (var i = 0; i < files.Length; i++)
                    {
                        ref var file = ref files[i];

                        if (file.State == SpaceState.Deleted)
                        {
                            file.State = SpaceState.Added;
                        }
                    }
                }

                for (var child = entry.FirstChildDir; child >= 0;)
                {
                    ref var childEntry = ref GetDirectoryRef(child);

                    if (childEntry.State == SpaceState.Deleted)
                    {
                        childEntry.State = SpaceState.Added;
                    }

                    stack.Push(child);
                    child = childEntry.NextSiblingDir;
                }
            }
        }
    }

    public bool HasMarkedInSubtree(int dirIndex)
    {
        lock (_facadeLock)
        {
            return HasMarkedInSubtreeLocked(dirIndex);
        }
    }

    public void CollectMarkedInSubtree(int dirIndex, List<SpaceBase> destination)
    {
        lock (_facadeLock)
        {
            if (GetDirectoryRef(dirIndex).IsDeleted)
            {
                destination.Add(GetOrCreateDirectoryFacadeLocked(dirIndex));
                return;
            }

            var stack = new Stack<int>();
            stack.Push(dirIndex);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                ref readonly var entry = ref GetDirectoryRef(cur);

                for (var child = entry.FirstChildDir; child >= 0;)
                {
                    ref readonly var childEntry = ref GetDirectoryRef(child);

                    if (childEntry.IsDeleted)
                    {
                        destination.Add(GetOrCreateDirectoryFacadeLocked(child));
                    }
                    else
                    {
                        stack.Push(child);
                    }

                    child = childEntry.NextSiblingDir;
                }

                if (entry.FileCount > 0)
                {
                    var files = GetSpan(entry.FileStart, entry.FileCount);
                    DirectorySpace? dirFacade = null;

                    for (var i = 0; i < files.Length; i++)
                    {
                        if (!files[i].IsDeleted)
                        {
                            continue;
                        }

                        dirFacade ??= GetOrCreateDirectoryFacadeLocked(cur);
                        destination.Add(GetOrCreateFacadeLocked(dirFacade, i));
                    }
                }
            }
        }
    }

    public bool HasAddedChildInDirectory(int dirIndex)
    {
        lock (_facadeLock)
        {
            ref readonly var entry = ref GetDirectoryRef(dirIndex);

            for (var child = entry.FirstChildDir; child >= 0;)
            {
                ref readonly var childEntry = ref GetDirectoryRef(child);

                if (childEntry.State == SpaceState.Added)
                {
                    return true;
                }

                child = childEntry.NextSiblingDir;
            }

            if (entry.FileCount > 0)
            {
                var files = GetSpan(entry.FileStart, entry.FileCount);

                for (var i = 0; i < files.Length; i++)
                {
                    if (files[i].State == SpaceState.Added)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    public bool HasMarkedChildInDirectory(int dirIndex)
    {
        lock (_facadeLock)
        {
            ref readonly var entry = ref GetDirectoryRef(dirIndex);

            if (entry.FileCount > 0)
            {
                var files = GetSpan(entry.FileStart, entry.FileCount);

                for (var i = 0; i < files.Length; i++)
                {
                    if (files[i].IsDeleted)
                    {
                        return true;
                    }
                }
            }

            for (var child = entry.FirstChildDir; child >= 0;)
            {
                if (HasMarkedInSubtreeLocked(child))
                {
                    return true;
                }

                child = GetDirectoryRef(child).NextSiblingDir;
            }

            return false;
        }
    }

    public bool RemoveFile(DirectorySpace dir, int fileStart, ref int fileCount, FileSpace file, out long removedSize)
    {
        lock (_facadeLock)
        {
            return RemoveFileLocked(dir, fileStart, ref fileCount, file, out removedSize);
        }
    }

    public void Freeze()
    {
        Names.Freeze();
    }

    internal static ScanTreeStore CreateWithNameSlots(int shardCount, bool singleWriter, int initialNameSlotsPerShard)
    {
        return new(shardCount, singleWriter, MaxRegularChunks, initialNameSlotsPerShard);
    }

    internal SpaceBase? GetSyntheticParentLocked(int dirIndex)
    {
        return _syntheticParents is not null && _syntheticParents.TryGetValue(dirIndex, out var parent)
            ? parent
            : null;
    }

    internal int AllocateDirectoryRange(int count)
    {
        if (count <= 0)
        {
            return Volatile.Read(ref _dirCount);
        }

        lock (_facadeLock)
        {
            var start = _dirCount;
            var nextCount = start + count;
            EnsureDirectoryCapacityLocked(nextCount);
            Volatile.Write(ref _dirCount, nextCount);
            return start;
        }
    }

    internal void SetDirectoryCount(int count)
    {
        Volatile.Write(ref _dirCount, count);
    }

    internal int AppendFileAtDirectory(int dirIndex, in FileEntry fileEntry)
    {
        lock (_facadeLock)
        {
            ref var dirEntry = ref GetDirectoryRef(dirIndex);
            var newIndex = dirEntry.FileCount;
            AppendFile(ref dirEntry.FileStart, ref dirEntry.FileCount, in fileEntry);
            dirEntry.Size += fileEntry.Size;
            PropagateDirDeltaLocked(dirIndex, fileEntry.Size, fileDelta: 1, dirDelta: 0);
            return newIndex;
        }
    }

    internal bool TryRemoveFileByNameId(int dirIndex, int nameId, out long removedSize)
    {
        lock (_facadeLock)
        {
            ref var dirEntry = ref GetDirectoryRef(dirIndex);

            if (dirEntry.FileCount <= 0)
            {
                removedSize = 0;
                return false;
            }

            var span = GetSpan(dirEntry.FileStart, dirEntry.FileCount);

            for (var i = 0; i < span.Length; i++)
            {
                if (span[i].NameId == nameId)
                {
                    return TryRemoveFileAtSlotSwapLocked(dirIndex, i, out removedSize, out _);
                }
            }

            removedSize = 0;
            return false;
        }
    }

    internal bool TryRemoveFileAtSlotSwap(int dirIndex, int slotIndex, out long removedSize, out int swappedNameId)
    {
        lock (_facadeLock)
        {
            return TryRemoveFileAtSlotSwapLocked(dirIndex, slotIndex, out removedSize, out swappedNameId);
        }
    }

    internal void DetachDirectoryInArena(int dirIndex)
    {
        lock (_facadeLock)
        {
            ref var entry = ref GetDirectoryRef(dirIndex);

            if (entry.IsDetached || entry.ParentIndex < 0)
            {
                return;
            }

            var parentIndex = entry.ParentIndex;

            if (!UnlinkChildDirectoryLocked(parentIndex, dirIndex))
            {
                return;
            }

            entry.SetDetached(true);
            int removedFiles;
            int removedDirs;

            if (entry.TotalFileCount >= 0 && entry.TotalDirCount >= 0)
            {
                removedFiles = entry.TotalFileCount;
                removedDirs = entry.TotalDirCount;
            }
            else
            {
                ComputeSubtreeCountsLocked(dirIndex, out var computedFiles, out var computedDirs);
                removedFiles = entry.TotalFileCount >= 0 ? entry.TotalFileCount : computedFiles;
                removedDirs = entry.TotalDirCount >= 0 ? entry.TotalDirCount : computedDirs;
            }

            PropagateDirDeltaLocked(parentIndex, -entry.TotalSize, -removedFiles, -(removedDirs + 1));
        }
    }

    internal void AttachDirectoryInArena(int parentIndex, int dirIndex)
    {
        lock (_facadeLock)
        {
            ref var entry = ref GetDirectoryRef(dirIndex);

            if (!entry.IsDetached)
            {
                return;
            }

            LinkChildDirectoryLocked(parentIndex, dirIndex);
            int addedFiles;
            int addedDirs;

            if (entry.TotalFileCount >= 0 && entry.TotalDirCount >= 0)
            {
                addedFiles = entry.TotalFileCount;
                addedDirs = entry.TotalDirCount;
            }
            else
            {
                ComputeSubtreeCountsLocked(dirIndex, out var computedFiles, out var computedDirs);
                addedFiles = entry.TotalFileCount >= 0 ? entry.TotalFileCount : computedFiles;
                addedDirs = entry.TotalDirCount >= 0 ? entry.TotalDirCount : computedDirs;
            }

            PropagateDirDeltaLocked(parentIndex, entry.TotalSize, addedFiles, addedDirs + 1);
        }
    }

    internal void ComputeSubtreeCounts(int dirIndex, out int totalFiles, out int totalDirs)
    {
        lock (_facadeLock)
        {
            ComputeSubtreeCountsLocked(dirIndex, out totalFiles, out totalDirs);
        }
    }

    internal int CountTopLevelMarkedInSubtreeLocked(int dirIndex)
    {
        if (GetDirectoryRef(dirIndex).IsDeleted)
        {
            return 1;
        }

        var count = 0;
        var stack = new Stack<int>();
        stack.Push(dirIndex);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            ref readonly var entry = ref GetDirectoryRef(cur);

            for (var child = entry.FirstChildDir; child >= 0;)
            {
                ref readonly var childEntry = ref GetDirectoryRef(child);

                if (childEntry.IsDeleted)
                {
                    count++;
                }
                else
                {
                    stack.Push(child);
                }

                child = childEntry.NextSiblingDir;
            }

            if (entry.FileCount > 0)
            {
                var files = GetSpan(entry.FileStart, entry.FileCount);

                for (var i = 0; i < files.Length; i++)
                {
                    if (files[i].IsDeleted)
                    {
                        count++;
                    }
                }
            }
        }

        return count;
    }

    internal bool TryRecycleDirectorySlotLocked(int dirIndex)
    {
        if (dirIndex <= 0 || dirIndex >= _dirCount)
        {
            return false;
        }

        ref var entry = ref GetDirectoryRef(dirIndex);

        if ((!entry.IsDetached && entry.ParentIndex >= 0) || entry.FirstChildDir >= 0 || entry.FileCount != 0)
        {
            return false;
        }

        if (_dirFacades is not null && _dirFacades.TryGetValue(dirIndex, out var weak))
        {
            if (weak.TryGetTarget(out _))
            {
                return false;
            }

            _dirFacades.Remove(dirIndex);
        }

        _syntheticParents?.Remove(dirIndex);
        entry = new()
        {
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalFileCount = -1,
            TotalDirCount = -1,
            Flags = DirectoryEntry.DetachedFlag,
        };
        _freeDirSlots ??= new();
        _freeDirSlots.Push(dirIndex);
        return true;
    }

    private bool TryRemoveFileAtSlotSwapLocked(int dirIndex, int slotIndex, out long removedSize, out int swappedNameId)
    {
        ref var dirEntry = ref GetDirectoryRef(dirIndex);

        if (slotIndex < 0 || slotIndex >= dirEntry.FileCount)
        {
            removedSize = 0;
            swappedNameId = -1;
            return false;
        }

        var span = GetWritableSpan(dirEntry.FileStart, dirEntry.FileCount);
        var lastIndex = dirEntry.FileCount - 1;
        var removedEntry = span[slotIndex];
        var removedName = Names.GetString(removedEntry.NameId);

        if (slotIndex == lastIndex)
        {
            swappedNameId = -1;
        }
        else
        {
            var swappedEntry = span[lastIndex];
            swappedNameId = swappedEntry.NameId;
            span[slotIndex] = swappedEntry;
        }

        span[lastIndex] = default;
        dirEntry.FileCount = lastIndex;

        if (_dirFacades is not null
            && _dirFacades.TryGetValue(dirIndex, out var weakDir)
            && weakDir.TryGetTarget(out var dirFacade)
            && _facades is not null
            && _facades.TryGetValue(dirFacade, out var map))
        {
            if (map.Remove(slotIndex, out var removedFacade))
            {
                removedFacade.Detach(removedName, in removedEntry);
            }

            if (slotIndex != lastIndex && map.Remove(lastIndex, out var swappedFacade))
            {
                map[slotIndex] = swappedFacade;
                swappedFacade.UpdateSlabIndex(slotIndex);
            }

            if (map.Count == 0)
            {
                _facades.Remove(dirFacade);
            }
        }

        removedSize = removedEntry.Size;
        dirEntry.Size -= removedSize;
        PropagateDirDeltaLocked(dirIndex, -removedSize, fileDelta: -1, dirDelta: 0);
        return true;
    }

    private bool RemoveFileLocked(DirectorySpace dir, int fileStart, ref int fileCount, FileSpace file, out long removedSize)
    {
        if (!file.IsAttachedToLocked(dir))
        {
            removedSize = 0;
            return false;
        }

        var removedIndex = file.SlabIndex;
        var span = GetWritableSpan(fileStart, fileCount);
        var removedEntry = span[removedIndex];
        var removedName = Names.GetString(removedEntry.NameId);

        if (removedIndex < (fileCount - 1))
        {
            span[(removedIndex + 1)..].CopyTo(span[removedIndex..]);
        }

        span[fileCount - 1] = default;
        fileCount--;
        file.Detach(removedName, in removedEntry);

        if (_facades is not null && _facades.TryGetValue(dir, out var map))
        {
            map.Remove(removedIndex);

            if (map.Count == 0)
            {
                _facades.Remove(dir);
            }
            else
            {
                ShiftFacadesAfterRemoval(map, removedIndex);
            }
        }

        removedSize = removedEntry.Size;
        return true;
    }

    private void EnsureDirectoryCapacityLocked(int requiredCount)
    {
        var requiredChunks = (int)(((long)requiredCount + DirChunkMask) >> DirChunkShift);

        while (_dirChunkCount < requiredChunks)
        {
            if (_dirChunkCount == _dirChunks.Length)
            {
                var next = new DirectoryEntry[_dirChunks.Length * 2][];
                Array.Copy(_dirChunks, next, _dirChunkCount);
                Volatile.Write(ref _dirChunks, next);
            }

            Volatile.Write(ref _dirChunks[_dirChunkCount], new DirectoryEntry[DirChunkSize]);
            Volatile.Write(ref _dirChunkCount, _dirChunkCount + 1);
        }
    }

    private void PruneDeadDirectoryFacadesLocked()
    {
        if (_dirFacades is null)
        {
            return;
        }

        List<int>? dead = null;

        foreach (var pair in _dirFacades)
        {
            if (!pair.Value.TryGetTarget(out _))
            {
                dead ??= [];
                dead.Add(pair.Key);
            }
        }

        if (dead is not null)
        {
            for (var i = 0; i < dead.Count; i++)
            {
                _dirFacades.Remove(dead[i]);
            }
        }

        _dirFacadePruneThreshold = Math.Max(InitialDirFacadePruneThreshold, _dirFacades.Count * 2);
    }

    private void LinkChildDirectoryLocked(int parentIndex, int childIndex)
    {
        ref var parent = ref GetDirectoryRef(parentIndex);
        ref var child = ref GetDirectoryRef(childIndex);
        child.ParentIndex = parentIndex;
        child.SetDetached(false);
        child.NextSiblingDir = parent.FirstChildDir;
        parent.FirstChildDir = childIndex;
        parent.SubDirCount++;
    }

    private bool UnlinkChildDirectoryLocked(int parentIndex, int childIndex)
    {
        ref var parent = ref GetDirectoryRef(parentIndex);
        var prev = -1;

        for (var cur = parent.FirstChildDir; cur >= 0;)
        {
            ref var curEntry = ref GetDirectoryRef(cur);
            var next = curEntry.NextSiblingDir;

            if (cur == childIndex)
            {
                if (prev < 0)
                {
                    parent.FirstChildDir = next;
                }
                else
                {
                    GetDirectoryRef(prev).NextSiblingDir = next;
                }

                curEntry.NextSiblingDir = -1;
                parent.SubDirCount--;
                return true;
            }

            prev = cur;
            cur = next;
        }

        return false;
    }

    private void PropagateDirDeltaLocked(int startDirIndex, long sizeDelta, int fileDelta, int dirDelta)
    {
        var cur = startDirIndex;

        for (var depth = 0; depth < MaxTreeDepth && cur >= 0; depth++)
        {
            ref var entry = ref GetDirectoryRef(cur);
            entry.TotalSize += sizeDelta;

            if (entry.TotalFileCount >= 0)
            {
                entry.TotalFileCount += fileDelta;
            }

            if (entry.TotalDirCount >= 0)
            {
                entry.TotalDirCount += dirDelta;
            }

            if (entry.IsDetached)
            {
                break;
            }

            cur = entry.ParentIndex;
        }
    }

    private bool HasMarkedInSubtreeLocked(int dirIndex)
    {
        var stack = new Stack<int>();
        stack.Push(dirIndex);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            ref readonly var entry = ref GetDirectoryRef(cur);

            if (entry.IsDeleted)
            {
                return true;
            }

            if (entry.FileCount > 0)
            {
                var files = GetSpan(entry.FileStart, entry.FileCount);

                for (var i = 0; i < files.Length; i++)
                {
                    if (files[i].IsDeleted)
                    {
                        return true;
                    }
                }
            }

            for (var child = entry.FirstChildDir; child >= 0;)
            {
                stack.Push(child);
                child = GetDirectoryRef(child).NextSiblingDir;
            }
        }

        return false;
    }

    private void ComputeSubtreeCountsLocked(int dirIndex, out int totalFiles, out int totalDirs)
    {
        totalFiles = 0;
        totalDirs = 0;
        var stack = new Stack<int>();
        stack.Push(dirIndex);

        while (stack.Count > 0)
        {
            ref readonly var entry = ref GetDirectoryRef(stack.Pop());
            totalFiles += entry.FileCount;
            totalDirs += entry.SubDirCount;

            for (var child = entry.FirstChildDir; child >= 0;)
            {
                stack.Push(child);
                child = GetDirectoryRef(child).NextSiblingDir;
            }
        }
    }

    private int RegisterLargeArrayLocked(FileEntry[] array)
    {
        var largeIndex = _largeCount;

        if (largeIndex == _largeTable.Length)
        {
            var next = new FileEntry[_largeTable.Length * 2][];
            Array.Copy(_largeTable, next, _largeTable.Length);
            Volatile.Write(ref _largeTable, next);
        }

        Volatile.Write(ref _largeTable[largeIndex], array);
        _largeCount = largeIndex + 1;
        return ~largeIndex;
    }

    private static void ShiftFacadesAfterRemoval(Dictionary<int, FileSpace> map, int removedIndex)
    {
        List<int>? toShift = null;

        foreach (var key in map.Keys)
        {
            if (key <= removedIndex)
            {
                continue;
            }

            toShift ??= [];
            toShift.Add(key);
        }

        if (toShift is null)
        {
            return;
        }

        toShift.Sort();

        for (var i = 0; i < toShift.Count; i++)
        {
            var oldIndex = toShift[i];
            var facade = map[oldIndex];
            map.Remove(oldIndex);
            var newIndex = oldIndex - 1;
            map[newIndex] = facade;
            facade.UpdateSlabIndex(newIndex);
        }
    }
}

internal sealed class FileSlabWriter(ScanTreeStore store)
{
    private FileEntry[]? _chunk;
    private int _chunkIndex;
    private int _offset;

    public Span<FileEntry> AllocateSlice(int count, out int fileStart)
    {
        if (count <= 0)
        {
            fileStart = 0;
            return Span<FileEntry>.Empty;
        }

        if (count > ScanTreeStore.ChunkSize)
        {
            fileStart = store.AllocateLargeArray(count, out var large);
            return large;
        }

        if (_chunk is null || (_offset + count) > ScanTreeStore.ChunkSize)
        {
            if (!store.TryAllocateChunk(out var nextChunkIndex, out var nextChunk))
            {
                fileStart = store.AllocateLargeArray(count, out var fallback);
                return fallback;
            }

            _chunk = nextChunk;
            _chunkIndex = nextChunkIndex;
            _offset = 0;
        }

        var slice = _chunk!.AsSpan(_offset, count);
        fileStart = (_chunkIndex << ScanTreeStore.ChunkShift) | _offset;
        _offset += count;
        return slice;
    }

    public void WriteFiles(ReadOnlySpan<FileEntry> source, out int fileStart)
    {
        if (source.IsEmpty)
        {
            fileStart = 0;
            return;
        }

        source.CopyTo(AllocateSlice(source.Length, out fileStart));
    }
}
