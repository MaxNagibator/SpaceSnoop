using System.Runtime.CompilerServices;

namespace SpaceSnoop.Core.Domain;

internal sealed class ScanTreeStore
{
    public const int ChunkSize = 16_384;
    internal const int ChunkShift = 14;
    internal const int MaxRegularChunks = 1 << (31 - ChunkShift);
    private const int ChunkMask = ChunkSize - 1;

    private readonly Lock _slabLock = new();
    private readonly Lock _facadeLock = new();
    private readonly int _maxRegularChunks;
    private FileEntry[][] _chunkTable = new FileEntry[64][];
    private int _chunkCount;
    private FileEntry[][] _largeTable = new FileEntry[16][];
    private int _largeCount;
    private ConditionalWeakTable<DirectorySpace, Dictionary<int, FileSpace>>? _facades;

    public NameTable Names { get; }

    internal Lock FacadeLock => _facadeLock;

    public ScanTreeStore(int shardCount = 1, bool singleWriter = false, int initialNameSlotsPerShard = 0)
        : this(shardCount, singleWriter, MaxRegularChunks, initialNameSlotsPerShard)
    {
    }

    internal ScanTreeStore(int shardCount, bool singleWriter, int maxRegularChunks, int initialNameSlotsPerShard = 0)
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

    public bool RemoveFile(DirectorySpace dir, int fileStart, ref int fileCount, FileSpace file, out long removedSize)
    {
        lock (_facadeLock)
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
    }

    public void Freeze()
    {
        Names.Freeze();
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

    public void WriteFiles(ReadOnlySpan<FileEntry> source, out int fileStart)
    {
        if (source.IsEmpty)
        {
            fileStart = 0;
            return;
        }

        if (source.Length > ScanTreeStore.ChunkSize)
        {
            fileStart = store.WriteLarge(source);
            return;
        }

        if (_chunk is null || (_offset + source.Length) > ScanTreeStore.ChunkSize)
        {
            if (!store.TryAllocateChunk(out var nextChunkIndex, out var nextChunk))
            {
                fileStart = store.WriteLarge(source);
                return;
            }

            _chunk = nextChunk;
            _chunkIndex = nextChunkIndex;
            _offset = 0;
        }

        source.CopyTo(_chunk!.AsSpan(_offset, source.Length));
        fileStart = (_chunkIndex << ScanTreeStore.ChunkShift) | _offset;
        _offset += source.Length;
    }
}
