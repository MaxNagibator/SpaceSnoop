using SpaceSnoop.Core.Mft;
using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Tests;

internal sealed class MftRecordBuilder(int size = 1024, int bytesPerSector = 512)
{
    private const int UpdateSequenceOffset = 0x30;
    private const int ResidentValueOffset = 0x18;
    private const int NonResidentHeaderBytes = 0x40;

    private readonly byte[] _record = new byte[size];
    private int _position = Align(UpdateSequenceOffset + (size / bytesPerSector + 1) * 2);
    private ushort _flags = MftLayout.RecordInUse;
    private ushort _sequence = 1;
    private long _baseRecord;
    private ushort _baseSequence;
    private bool _keepSignature = true;

    public MftRecordBuilder AsDirectory()
    {
        _flags |= MftLayout.RecordIsDirectory;
        return this;
    }

    public MftRecordBuilder AsFree()
    {
        _flags &= unchecked((ushort)~MftLayout.RecordInUse);
        return this;
    }

    public MftRecordBuilder WithSequence(ushort sequence)
    {
        _sequence = sequence;
        return this;
    }

    public MftRecordBuilder ExtensionOf(int record, ushort sequence = 1)
    {
        _baseRecord = record;
        _baseSequence = sequence;
        return this;
    }

    public MftRecordBuilder WithoutSignature()
    {
        _keepSignature = false;
        return this;
    }

    public MftRecordBuilder StandardInformation(DateTime creation, DateTime access)
    {
        var value = new byte[0x30];
        BinaryPrimitives.WriteInt64LittleEndian(value, creation.ToFileTimeUtc());
        BinaryPrimitives.WriteInt64LittleEndian(value.AsSpan(0x18), access.ToFileTimeUtc());
        return Resident(MftLayout.AttributeStandardInformation, value);
    }

    public MftRecordBuilder FileName(int parent, ushort parentSequence, string name, byte space = 1)
    {
        var value = new byte[0x42 + name.Length * 2];
        var reference = ((ulong)parent & MftLayout.ReferenceMask) | ((ulong)parentSequence << MftLayout.ReferenceMaskBits);
        BinaryPrimitives.WriteUInt64LittleEndian(value, reference);
        value[0x40] = (byte)name.Length;
        value[0x41] = space;
        MemoryMarshal.Cast<char, byte>(name).CopyTo(value.AsSpan(0x42));
        return Resident(MftLayout.AttributeFileName, value);
    }

    public MftRecordBuilder ResidentData(int bytes)
    {
        return Resident(MftLayout.AttributeData, new byte[bytes]);
    }

    public MftRecordBuilder NonResidentData(long realSize, long startVcn = 0)
    {
        return NonResident(MftLayout.AttributeData, realSize, startVcn, []);
    }

    public MftRecordBuilder DataRuns(long realSize, ReadOnlySpan<byte> runs)
    {
        return NonResident(MftLayout.AttributeData, realSize, 0, runs);
    }

    public MftRecordBuilder Reparse(uint tag)
    {
        var value = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(value, tag);
        return Resident(MftLayout.AttributeReparsePoint, value);
    }

    public MftRecordBuilder NonResidentReparse()
    {
        return NonResident(MftLayout.AttributeReparsePoint, 64, 0, []);
    }

    public MftRecordBuilder BrokenAttribute()
    {
        BinaryPrimitives.WriteUInt32LittleEndian(_record.AsSpan(_position), MftLayout.AttributeData);
        BinaryPrimitives.WriteUInt32LittleEndian(_record.AsSpan(_position + 4), 8);
        _position += 8;
        return this;
    }

    public byte[] Build()
    {
        var record = (byte[])_record.Clone();

        if (_keepSignature)
        {
            "FILE"u8.CopyTo(record);
        }

        var count = (ushort)(record.Length / bytesPerSector + 1);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), UpdateSequenceOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), count);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(MftLayout.RecordSequenceOffset), _sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x14), (ushort)Align(UpdateSequenceOffset + count * 2));
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(0x16), _flags);

        var reference = ((ulong)_baseRecord & MftLayout.ReferenceMask) | ((ulong)_baseSequence << MftLayout.ReferenceMaskBits);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(0x20), reference);

        if (_position + 4 <= record.Length)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(_position), MftLayout.AttributeEnd);
        }

        var signature = record.AsSpan(UpdateSequenceOffset, 2);
        signature[0] = 0x0B;
        signature[1] = 0xAD;

        for (var sector = 1; sector < count; sector++)
        {
            var tail = record.AsSpan(sector * bytesPerSector - 2, 2);
            tail.CopyTo(record.AsSpan(UpdateSequenceOffset + sector * 2));
            signature.CopyTo(tail);
        }

        return record;
    }

    private MftRecordBuilder Resident(uint type, ReadOnlySpan<byte> value)
    {
        var length = Align(ResidentValueOffset + value.Length);
        var attribute = _record.AsSpan(_position, length);

        BinaryPrimitives.WriteUInt32LittleEndian(attribute, type);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute[4..], (uint)length);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute[0x10..], (uint)value.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute[0x14..], ResidentValueOffset);
        value.CopyTo(attribute[ResidentValueOffset..]);

        _position += length;
        return this;
    }

    private MftRecordBuilder NonResident(uint type, long realSize, long startVcn, ReadOnlySpan<byte> runs)
    {
        var length = Align(NonResidentHeaderBytes + runs.Length);
        var attribute = _record.AsSpan(_position, length);

        BinaryPrimitives.WriteUInt32LittleEndian(attribute, type);
        BinaryPrimitives.WriteUInt32LittleEndian(attribute[4..], (uint)length);
        attribute[0x08] = 1;
        BinaryPrimitives.WriteInt64LittleEndian(attribute[0x10..], startVcn);
        BinaryPrimitives.WriteUInt16LittleEndian(attribute[0x20..], NonResidentHeaderBytes);
        BinaryPrimitives.WriteInt64LittleEndian(attribute[0x30..], realSize);
        runs.CopyTo(attribute[NonResidentHeaderBytes..]);

        _position += length;
        return this;
    }

    private static int Align(int value)
    {
        return (value + 7) & ~7;
    }
}

internal sealed class MftVolumeBuilder(int bytesPerSector = 512, int sectorsPerCluster = 8, int recordSize = 1024)
{
    private const long FirstCluster = 4;
    private const long GapClusters = 2;

    private readonly List<MftRun> _runs = [];
    private readonly Dictionary<int, byte[]> _records = [];

    private int ClusterSize => bytesPerSector * sectorsPerCluster;

    public long MftOffset => _runs[0].Cluster * ClusterSize;

    public MftVolumeBuilder Fragment(long clusters)
    {
        var start = _runs.Count == 0 ? FirstCluster : _runs[^1].Cluster + _runs[^1].Count + GapClusters;
        _runs.Add(new(start, clusters));
        return this;
    }

    public MftVolumeBuilder Record(int index, MftRecordBuilder builder)
    {
        _records[index] = builder.Build();
        return this;
    }

    public byte[] Build(int records, long? declaredBytes = null)
    {
        if (_runs.Count == 0)
        {
            Fragment((records * (long)recordSize + ClusterSize - 1) / ClusterSize);
        }

        var last = _runs[^1];
        var image = new byte[(last.Cluster + last.Count + GapClusters) * ClusterSize];

        WriteBoot(image);
        WriteLogical(image, 0, SelfRecord(declaredBytes ?? records * (long)recordSize));

        foreach (var (index, record) in _records)
        {
            WriteLogical(image, index * (long)recordSize, record);
        }

        return image;
    }

    private byte[] SelfRecord(long declaredBytes)
    {
        var runs = new List<byte>();
        long previous = 0;

        foreach (var run in _runs)
        {
            runs.Add(0x44);
            runs.AddRange(BitConverter.GetBytes((int)run.Count));
            runs.AddRange(BitConverter.GetBytes((int)(run.Cluster - previous)));
            previous = run.Cluster;
        }

        runs.Add(0);

        return new MftRecordBuilder(recordSize, bytesPerSector)
            .FileName(MftLayout.RootRecord, 1, "$MFT")
            .DataRuns(declaredBytes, CollectionsMarshal.AsSpan(runs))
            .Build();
    }

    private void WriteBoot(byte[] image)
    {
        "NTFS    "u8.CopyTo(image.AsSpan(3));
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(0x0B), (ushort)bytesPerSector);
        image[0x0D] = (byte)sectorsPerCluster;
        BinaryPrimitives.WriteInt64LittleEndian(image.AsSpan(0x30), _runs[0].Cluster);
        image[0x40] = unchecked((byte)(sbyte)(recordSize >= ClusterSize
            ? recordSize / ClusterSize
            : -BitOperations.Log2((uint)recordSize)));
    }

    private void WriteLogical(byte[] image, long logical, ReadOnlySpan<byte> data)
    {
        var position = 0L;

        foreach (var run in _runs)
        {
            var runBytes = run.Count * ClusterSize;
            var from = Math.Max(logical, position);
            var to = Math.Min(logical + data.Length, position + runBytes);

            if (from < to)
            {
                var source = data.Slice((int)(from - logical), (int)(to - from));
                source.CopyTo(image.AsSpan((int)(run.Cluster * ClusterSize + (from - position))));
            }

            position += runBytes;
        }
    }
}

internal sealed class MftMemoryVolume(byte[] image) : IMftVolume
{
    public void ReadAt(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset + buffer.Length > image.Length)
        {
            throw new EndOfStreamException($"Чтение образа тома вышло за границу на смещении {offset}");
        }

        image.AsSpan((int)offset, buffer.Length).CopyTo(buffer);
    }
}
