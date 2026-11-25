using System.Buffers;
using System.Numerics;

namespace SpaceSnoop.Core.Domain;

internal sealed class NameTable
{
    internal const int MaxRegularChunks = 1 << (31 - ChunkShift);
    private const int ChunkShift = 20;
    private const int MaxStackUtf8Chars = 341;

    private readonly Shard[] _shards;
    private readonly int _shardMask;
    private readonly int _shardShift;

    public NameTable(int shardCount = 1, bool singleWriter = false, int initialSlotCapacity = 0)
    {
        if (shardCount <= 0 || (shardCount & (shardCount - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shardCount));
        }

        _shardMask = shardCount - 1;
        _shardShift = BitOperations.TrailingZeroCount(shardCount);
        _shards = new Shard[shardCount];

        for (var i = 0; i < shardCount; i++)
        {
            _shards[i] = new(_shardMask != 0, _shardShift, singleWriter, initialSlotCapacity);
        }
    }

    public int Intern(ReadOnlySpan<char> chars)
    {
        if (chars.Length <= MaxStackUtf8Chars)
        {
            Span<byte> buffer = stackalloc byte[1024];
            var written = Wtf8.FromUtf16(chars, buffer);
            return Intern(buffer[..written]);
        }

        var maxBytes = Wtf8.GetMaxByteCount(chars.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);

        try
        {
            var written = Wtf8.FromUtf16(chars, rented);
            return Intern(rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public int Intern(ReadOnlySpan<byte> utf8)
    {
        return Intern(utf8, HashUtf8(utf8));
    }

    public void Freeze()
    {
        for (var i = 0; i < _shards.Length; i++)
        {
            _shards[i].Freeze();
        }
    }

    public ReadOnlySpan<byte> GetUtf8(int nameId)
    {
        var shardIndex = nameId & _shardMask;
        var localId = nameId >>> _shardShift;
        return _shards[shardIndex].GetUtf8(localId);
    }

    public string GetString(int nameId)
    {
        return Wtf8.GetString(GetUtf8(nameId));
    }

    public bool Equals(int nameId, ReadOnlySpan<char> value, StringComparison comparison)
    {
        return Wtf8.Equals(GetUtf8(nameId), value, comparison);
    }

    internal static int ComputeResizeThreshold(int capacity)
    {
        return (int)(((long)capacity * 65L) / 100L);
    }

    internal static bool NeedsOverflow(int totalBytes, int maxChunkSize, int chunkCount, int chunkOffset, int currentChunkLength)
    {
        if (totalBytes > maxChunkSize)
        {
            return true;
        }

        var needsNewChunk = chunkCount == 0 || (chunkOffset + totalBytes) > currentChunkLength;
        return needsNewChunk && chunkCount >= MaxRegularChunks;
    }

    internal int Intern(ReadOnlySpan<byte> utf8, uint precomputedHash)
    {
        var shardIndex = (int)(precomputedHash & (uint)_shardMask);
        var localId = _shards[shardIndex].Intern(utf8, precomputedHash);
        return (localId << _shardShift) | shardIndex;
    }

    internal static uint HashUtf8(ReadOnlySpan<byte> utf8)
    {
        var hash = 2166136261u;

        for (var i = 0; i < utf8.Length; i++)
        {
            hash = (hash ^ utf8[i]) * 16777619u;
        }

        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        return hash;
    }

    private sealed class Shard
    {
        private const int ChunkMask = (1 << ChunkShift) - 1;
        private const int SingleShardInitialChunkSize = 256;
        private const int MultiShardInitialChunkSize = 4096;
        private const int SingleShardMaxChunkSize = 1 << ChunkShift;
        private const int MultiShardMaxChunkSize = 256 * 1024;
        private const int OffsetBlockSize = 65_536;
        private const int OffsetBlockShift = 16;
        private const int OffsetBlockMask = OffsetBlockSize - 1;
        private const int SingleShardInitialOffsetCapacity = 16;
        private const int MultiShardInitialOffsetCapacity = 256;
        private const int SingleShardInitialSlotCapacity = 16;
        private const int MultiShardInitialSlotCapacity = 256;
        private const int FreezeTrimChunkThreshold = 4096;
        private const int FreezeTrimOffsetThreshold = 1024;
        private const int FrozenInitialChunkSize = 512;
        private const int FrozenOffsetGrowStep = 256;

        private readonly Lock _lock = new();
        private readonly int _shardShift;
        private readonly int _maxChunkSize;
        private readonly int _initialOffsetCapacity;
        private byte[][] _chunks = new byte[4][];
        private int _chunkCount;
        private int[][] _offsets = new int[4][];
        private int _offsetBlockCount;
        private byte[][]? _overflow;
        private int _overflowCount;
        private int _nextChunkSize;
        private int _chunkOffset;
        private int _count;
        private int[]? _slots;
        private int _resizeThreshold;
        private bool _singleWriter;

        public Shard(bool multiShard, int shardShift, bool singleWriter, int initialSlotCapacity = 0)
        {
            _singleWriter = singleWriter && !multiShard;
            _shardShift = shardShift;
            _nextChunkSize = multiShard ? MultiShardInitialChunkSize : SingleShardInitialChunkSize;
            _maxChunkSize = multiShard ? MultiShardMaxChunkSize : SingleShardMaxChunkSize;
            _initialOffsetCapacity = multiShard ? MultiShardInitialOffsetCapacity : SingleShardInitialOffsetCapacity;
            var defaultSlotCapacity = multiShard ? MultiShardInitialSlotCapacity : SingleShardInitialSlotCapacity;
            var slotCapacity = initialSlotCapacity > 0
                ? Math.Max(defaultSlotCapacity, (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(initialSlotCapacity, defaultSlotCapacity, 1 << 24)))
                : defaultSlotCapacity;
            _slots = new int[slotCapacity];
            _resizeThreshold = ComputeResizeThreshold(slotCapacity);
        }

        public int Intern(ReadOnlySpan<byte> utf8, uint hash)
        {
            if (!_singleWriter)
            {
                lock (_lock)
                {
                    return InternCore(utf8, hash);
                }
            }

            return InternCore(utf8, hash);
        }

        public void Freeze()
        {
            lock (_lock)
            {
                _slots = null;
                _singleWriter = false;
                _nextChunkSize = FrozenInitialChunkSize;

                if (_chunkCount > 0)
                {
                    var lastChunkIndex = _chunkCount - 1;
                    var lastChunk = _chunks[lastChunkIndex];

                    if (lastChunk.Length - _chunkOffset >= FreezeTrimChunkThreshold)
                    {
                        var trimmed = new byte[_chunkOffset];
                        Array.Copy(lastChunk, trimmed, _chunkOffset);
                        Volatile.Write(ref _chunks[lastChunkIndex], trimmed);
                    }
                }

                if (_offsetBlockCount > 0)
                {
                    var lastBlockIndex = _offsetBlockCount - 1;
                    var lastBlock = _offsets[lastBlockIndex];
                    var usedCount = _count - (lastBlockIndex << OffsetBlockShift);

                    if (lastBlock.Length - usedCount >= FreezeTrimOffsetThreshold)
                    {
                        var trimmed = new int[usedCount];
                        Array.Copy(lastBlock, trimmed, usedCount);
                        Volatile.Write(ref _offsets[lastBlockIndex], trimmed);
                    }
                }
            }
        }

        public ReadOnlySpan<byte> GetUtf8(int localId)
        {
            GetSlice(localId, out var chunk, out var start, out var length);
            return chunk.AsSpan(start, length);
        }

        private int InternCore(ReadOnlySpan<byte> utf8, uint hash)
        {
            var slots = _slots;

            if (slots is null)
            {
                return AppendEntry(utf8);
            }

            var mask = slots.Length - 1;
            var slotIndex = (int)(BitOperations.RotateRight(hash, _shardShift) & (uint)mask);

            while (true)
            {
                var entry = slots[slotIndex];

                if (entry == 0)
                {
                    break;
                }

                var existingId = entry - 1;
                GetSliceLocked(existingId, out var chunk, out var start, out var length);

                if (utf8.SequenceEqual(chunk.AsSpan(start, length)))
                {
                    return existingId;
                }

                slotIndex = (slotIndex + 1) & mask;
            }

            var newId = AppendEntry(utf8);

            if (_count > _resizeThreshold)
            {
                ResizeSlots();
                slots = _slots!;
                mask = slots.Length - 1;
                slotIndex = (int)(BitOperations.RotateRight(hash, _shardShift) & (uint)mask);

                while (slots[slotIndex] != 0)
                {
                    slotIndex = (slotIndex + 1) & mask;
                }
            }

            slots[slotIndex] = newId + 1;
            return newId;
        }

        private int AppendEntry(ReadOnlySpan<byte> utf8)
        {
            var localId = _count++;
            var blockIndex = localId >> OffsetBlockShift;
            var indexInBlock = localId & OffsetBlockMask;

            EnsureOffsetSlot(blockIndex, indexInBlock);

            var varintLength = MeasureVarint(utf8.Length);
            var totalBytes = varintLength + utf8.Length;
            var currentChunkLength = _chunkCount > 0 ? _chunks[_chunkCount - 1].Length : 0;
            int encodedOffset;

            if (NeedsOverflow(totalBytes, _maxChunkSize, _chunkCount, _chunkOffset, currentChunkLength))
            {
                var overflow = _overflow;

                if (overflow is null)
                {
                    overflow = new byte[4][];
                    Volatile.Write(ref _overflow, overflow);
                }
                else if (_overflowCount == overflow.Length)
                {
                    var nextOverflow = new byte[overflow.Length * 2][];
                    Array.Copy(overflow, nextOverflow, _overflowCount);
                    Volatile.Write(ref _overflow, nextOverflow);
                    overflow = nextOverflow;
                }

                var copy = utf8.ToArray();
                var overflowIndex = _overflowCount;
                Volatile.Write(ref overflow[overflowIndex], copy);
                _overflowCount = overflowIndex + 1;
                encodedOffset = ~overflowIndex;
            }
            else
            {
                if (_chunkCount == 0 || (_chunkOffset + totalBytes) > currentChunkLength)
                {
                    var chunkSize = _nextChunkSize;

                    while (chunkSize < totalBytes)
                    {
                        chunkSize = Math.Min(_maxChunkSize, chunkSize * 4);
                    }

                    _nextChunkSize = Math.Min(_maxChunkSize, chunkSize * 4);

                    var chunkIndex = _chunkCount;

                    if (chunkIndex == _chunks.Length)
                    {
                        var nextChunks = new byte[_chunks.Length * 2][];
                        Array.Copy(_chunks, nextChunks, chunkIndex);
                        Volatile.Write(ref _chunks, nextChunks);
                    }

                    var newChunk = new byte[chunkSize];
                    WriteVarint(newChunk.AsSpan(0, varintLength), utf8.Length);
                    utf8.CopyTo(newChunk.AsSpan(varintLength));
                    Volatile.Write(ref _chunks[chunkIndex], newChunk);
                    _chunkCount = chunkIndex + 1;
                    _chunkOffset = totalBytes;
                    encodedOffset = chunkIndex << ChunkShift;
                }
                else
                {
                    var chunkIndex = _chunkCount - 1;
                    var chunk = _chunks[chunkIndex];
                    var offset = _chunkOffset;

                    WriteVarint(chunk.AsSpan(offset, varintLength), utf8.Length);
                    utf8.CopyTo(chunk.AsSpan(offset + varintLength));
                    _chunkOffset = offset + totalBytes;
                    encodedOffset = (chunkIndex << ChunkShift) | offset;
                }
            }

            Volatile.Write(ref _offsets[blockIndex][indexInBlock], encodedOffset);
            return localId;
        }

        private void EnsureOffsetSlot(int blockIndex, int indexInBlock)
        {
            if (blockIndex >= _offsetBlockCount)
            {
                if (blockIndex >= _offsets.Length)
                {
                    var nextOffsets = new int[_offsets.Length * 2][];
                    Array.Copy(_offsets, nextOffsets, _offsetBlockCount);
                    Volatile.Write(ref _offsets, nextOffsets);
                }

                int blockCapacity;

                if (blockIndex == 0)
                {
                    blockCapacity = _initialOffsetCapacity;
                }
                else
                {
                    blockCapacity = _slots is null ? FrozenOffsetGrowStep : OffsetBlockSize;
                }

                Volatile.Write(ref _offsets[blockIndex], new int[blockCapacity]);
                _offsetBlockCount = blockIndex + 1;
                return;
            }

            var currentBlock = _offsets[blockIndex];

            if (indexInBlock >= currentBlock.Length)
            {
                var doubled = currentBlock.Length * 2;
                var targetLength = _slots is null
                    ? Math.Min(doubled, currentBlock.Length + FrozenOffsetGrowStep)
                    : doubled;
                var nextLength = Math.Min(OffsetBlockSize, Math.Max(indexInBlock + 1, targetLength));
                var nextBlock = new int[nextLength];
                Array.Copy(currentBlock, nextBlock, currentBlock.Length);
                Volatile.Write(ref _offsets[blockIndex], nextBlock);
            }
        }

        private void ResizeSlots()
        {
            var oldSlots = _slots!;
            var newCapacity = oldSlots.Length * 2;
            var newSlots = new int[newCapacity];
            var mask = newCapacity - 1;

            for (var i = 0; i < oldSlots.Length; i++)
            {
                var entry = oldSlots[i];

                if (entry == 0)
                {
                    continue;
                }

                var localId = entry - 1;
                GetSliceLocked(localId, out var chunk, out var start, out var length);
                var hash = HashUtf8(chunk.AsSpan(start, length));
                var slotIndex = (int)(BitOperations.RotateRight(hash, _shardShift) & (uint)mask);

                while (newSlots[slotIndex] != 0)
                {
                    slotIndex = (slotIndex + 1) & mask;
                }

                newSlots[slotIndex] = entry;
            }

            _slots = newSlots;
            _resizeThreshold = ComputeResizeThreshold(newCapacity);
        }

        private void GetSliceLocked(int localId, out byte[] chunk, out int start, out int length)
        {
            var encoded = _offsets[localId >> OffsetBlockShift][localId & OffsetBlockMask];

            if (encoded < 0)
            {
                chunk = _overflow![~encoded];
                start = 0;
                length = chunk.Length;
                return;
            }

            chunk = _chunks[encoded >> ChunkShift];
            var pos = encoded & ChunkMask;
            var varintLength = ReadVarint(chunk, pos, out length);
            start = pos + varintLength;
        }

        private void GetSlice(int localId, out byte[] chunk, out int start, out int length)
        {
            var offsets = Volatile.Read(ref _offsets);
            var block = Volatile.Read(ref offsets[localId >> OffsetBlockShift]);
            var encoded = Volatile.Read(ref block[localId & OffsetBlockMask]);

            if (encoded < 0)
            {
                var overflow = Volatile.Read(ref _overflow)!;
                chunk = Volatile.Read(ref overflow[~encoded]);
                start = 0;
                length = chunk.Length;
                return;
            }

            var chunks = Volatile.Read(ref _chunks);
            chunk = Volatile.Read(ref chunks[encoded >> ChunkShift]);
            var pos = encoded & ChunkMask;
            var varintLength = ReadVarint(chunk, pos, out length);
            start = pos + varintLength;
        }

        private static int MeasureVarint(int value)
        {
            var remaining = (uint)value;
            var bytes = 1;

            while (remaining >= 0x80u)
            {
                remaining >>= 7;
                bytes++;
            }

            return bytes;
        }

        private static void WriteVarint(Span<byte> destination, int value)
        {
            var remaining = (uint)value;
            var i = 0;

            while (remaining >= 0x80u)
            {
                destination[i++] = (byte)(remaining | 0x80u);
                remaining >>= 7;
            }

            destination[i] = (byte)remaining;
        }

        private static int ReadVarint(byte[] chunk, int pos, out int value)
        {
            var b0 = chunk[pos];

            if (b0 < 0x80)
            {
                value = b0;
                return 1;
            }

            var result = b0 & 0x7F;
            var shift = 7;
            var index = pos + 1;

            while (true)
            {
                var b = chunk[index++];
                result |= (b & 0x7F) << shift;

                if (b < 0x80)
                {
                    value = result;
                    return index - pos;
                }

                shift += 7;
            }
        }
    }
}

internal readonly struct LocalNameCache
{
    private const int SlotCount = 2048;
    private const int SlotMask = SlotCount - 1;
    private const int MaxStackUtf8Chars = 341;

    private readonly CacheSlot[]? _slots;

    public bool IsCreated => _slots is not null;

    private LocalNameCache(CacheSlot[] slots)
    {
        _slots = slots;
    }

    public static LocalNameCache Create()
    {
        var slots = new CacheSlot[SlotCount];

        for (var i = 0; i < slots.Length; i++)
        {
            slots[i].NameId = -1;
        }

        return new(slots);
    }

    public int Intern(NameTable names, ReadOnlySpan<char> chars)
    {
        if (_slots is null)
        {
            return names.Intern(chars);
        }

        if (chars.Length <= MaxStackUtf8Chars)
        {
            Span<byte> buffer = stackalloc byte[1024];
            var written = Wtf8.FromUtf16(chars, buffer);
            return Intern(names, buffer[..written]);
        }

        var maxBytes = Wtf8.GetMaxByteCount(chars.Length);
        var rented = ArrayPool<byte>.Shared.Rent(maxBytes);

        try
        {
            var written = Wtf8.FromUtf16(chars, rented);
            return Intern(names, rented.AsSpan(0, written));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public int Intern(NameTable names, ReadOnlySpan<byte> utf8)
    {
        if (_slots is null)
        {
            return names.Intern(utf8);
        }

        var hash = NameTable.HashUtf8(utf8);
        ref var slot = ref _slots[(int)(hash & SlotMask)];

        if (slot.NameId >= 0 && slot.Hash == hash && names.GetUtf8(slot.NameId).SequenceEqual(utf8))
        {
            return slot.NameId;
        }

        var nameId = names.Intern(utf8, hash);
        slot.Hash = hash;
        slot.NameId = nameId;
        return nameId;
    }

    private struct CacheSlot
    {
        public uint Hash;
        public int NameId;
    }
}
