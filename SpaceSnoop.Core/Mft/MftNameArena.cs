using SpaceSnoop.Core.Domain;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Core.Mft;

internal sealed class MftNameArena
{
    public const int ChunkSize = 2 * 1024 * 1024;

    private readonly Lock _lock = new();
    private byte[][] _chunks = new byte[64][];
    private int _chunkCount;
    private MftNameArenaWriter? _sharedWriter;

    public MftNameRef Append(ReadOnlySpan<char> chars)
    {
        if (chars.IsEmpty)
        {
            return default;
        }

        lock (_lock)
        {
            _sharedWriter ??= new(this);
            return _sharedWriter.Append(chars, lockChunks: false);
        }
    }

    public ReadOnlySpan<byte> GetUtf8(MftNameRef name)
    {
        if (!name.HasValue)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        return Volatile.Read(ref _chunks)[name.Chunk].AsSpan(name.Offset, name.ByteLength);
    }

    public string GetString(MftNameRef name)
    {
        return Wtf8.GetString(GetUtf8(name));
    }

    public bool Equals(MftNameRef name, ReadOnlySpan<char> value, StringComparison comparison)
    {
        if (!name.HasValue)
        {
            return value.IsEmpty;
        }

        return Wtf8.Equals(GetUtf8(name), value, comparison);
    }

    internal ushort ReserveChunk(out byte[] chunk)
    {
        lock (_lock)
        {
            return ReserveChunkCore(out chunk);
        }
    }

    internal ushort ReserveChunkUnlocked(out byte[] chunk)
    {
        return ReserveChunkCore(out chunk);
    }

    private ushort ReserveChunkCore(out byte[] chunk)
    {
        var index = _chunkCount;

        if (index > ushort.MaxValue)
        {
            throw new InvalidOperationException("Exceeded MftNameArena chunk limit.");
        }

        if (index == _chunks.Length)
        {
            var next = new byte[_chunks.Length * 2][];
            Array.Copy(_chunks, next, _chunks.Length);
            Volatile.Write(ref _chunks, next);
        }

        chunk = new byte[ChunkSize];
        Volatile.Write(ref _chunks[index], chunk);
        _chunkCount = index + 1;
        return (ushort)index;
    }
}

internal sealed class MftNameArenaWriter(MftNameArena arena)
{
    private byte[]? _chunk;
    private ushort _chunkIndex;
    private int _offset;

    public MftNameArena Arena { get; } = arena;

    public MftNameRef AppendUtf16(ReadOnlySpan<byte> utf16Bytes)
    {
        return Append(MemoryMarshal.Cast<byte, char>(utf16Bytes));
    }

    public MftNameRef Append(ReadOnlySpan<char> chars)
    {
        return Append(chars, lockChunks: true);
    }

    public MftNameRef AppendUtf8(ReadOnlySpan<byte> utf8)
    {
        if (utf8.IsEmpty)
        {
            return default;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(utf8.Length, ushort.MaxValue, nameof(utf8));

        if (_chunk is null || (_offset + utf8.Length) > _chunk.Length)
        {
            _chunkIndex = Arena.ReserveChunk(out _chunk);
            _offset = 0;
        }

        utf8.CopyTo(_chunk.AsSpan(_offset));
        var nameRef = new MftNameRef(_offset, _chunkIndex, (ushort)utf8.Length);
        _offset += utf8.Length;
        return nameRef;
    }

    internal MftNameRef Append(ReadOnlySpan<char> chars, bool lockChunks)
    {
        if (chars.IsEmpty)
        {
            return default;
        }

        var maxBytes = Wtf8.GetMaxByteCount(chars.Length);

        if (maxBytes > ushort.MaxValue)
        {
            var actualBytes = Wtf8.GetByteCount(chars);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(actualBytes, ushort.MaxValue, nameof(chars));
            maxBytes = actualBytes;
        }

        if (_chunk is null || (_offset + maxBytes) > _chunk.Length)
        {
            _chunkIndex = lockChunks
                ? Arena.ReserveChunk(out _chunk)
                : Arena.ReserveChunkUnlocked(out _chunk);
            _offset = 0;
        }

        var written = Wtf8.FromUtf16(chars, _chunk.AsSpan(_offset));
        var nameRef = new MftNameRef(_offset, _chunkIndex, (ushort)written);
        _offset += written;
        return nameRef;
    }
}
