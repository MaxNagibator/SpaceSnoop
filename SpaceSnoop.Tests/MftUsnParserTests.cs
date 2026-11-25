using SpaceSnoop.Core.Mft;
using System.Buffers.Binary;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftUsnParserTests
{
    private const int Parent = 40;
    private const uint JunctionTag = 0xA0000003u;
    private const uint SymlinkTag = 0xA000000Cu;
    private const uint CloudPlaceholderTag = 0x9000001Au;

    [Test]
    public void Free_slot_mismatch_is_rejected()
    {
        var raw = new MftRecordBuilder()
            .FileName(Parent, 1, "ghost.bin")
            .ResidentData(128)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 99, sequence: 2);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, requestedRecordNumber: 100);

        Assert.That(parsed.Exists, Is.False);
    }

    [Test]
    public void Unused_record_is_rejected()
    {
        var raw = new MftRecordBuilder()
            .AsFree()
            .FileName(Parent, 1, "deleted.bin")
            .ResidentData(64)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 55, sequence: 3);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, requestedRecordNumber: 55);

        Assert.That(parsed.Exists, Is.False);
    }

    [Test]
    public void Extension_record_queried_as_base_is_rejected()
    {
        var raw = new MftRecordBuilder()
            .ExtensionOf(42, sequence: 5)
            .FileName(Parent, 1, "ext.bin")
            .ResidentData(256)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 77, sequence: 1);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, requestedRecordNumber: 77);

        Assert.That(parsed.Exists, Is.False);
    }

    [Test]
    public void Missing_signature_and_truncated_buffers_return_default()
    {
        var noSig = new MftRecordBuilder()
            .WithoutSignature()
            .FileName(Parent, 1, "bad.bin")
            .Build();
        var badSigOut = WrapFileRecordOutput(noSig, returnedRecordNumber: 50, sequence: 1, applyFixup: false);

        var tooShort = new byte[20];
        var badRecLen = new byte[12 + 64];
        BinaryPrimitives.WriteUInt64LittleEndian(badRecLen, 50UL);
        BinaryPrimitives.WriteUInt32LittleEndian(badRecLen.AsSpan(8), 200u);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(MftUsnNativeSource.ParseFileRecordBuffer(badSigOut, 50).Exists, Is.False);
            Assert.That(MftUsnNativeSource.ParseFileRecordBuffer(tooShort, 50).Exists, Is.False);
            Assert.That(MftUsnNativeSource.ParseFileRecordBuffer(badRecLen, 50).Exists, Is.False);
        }
    }

    [Test]
    public void Attribute_length_near_int_max_and_oversized_resident_data_stop_safely()
    {
        var overflowRaw = new MftRecordBuilder()
            .WithSequence(4)
            .FileName(Parent, 1, "safe.bin")
            .OverflowingAttribute()
            .Build();
        var overflowOut = WrapFileRecordOutput(overflowRaw, returnedRecordNumber: 60, sequence: 4);

        var oversizedRaw = new MftRecordBuilder()
            .WithSequence(4)
            .FileName(Parent, 1, "oversized.bin")
            .OversizedResidentData(10_000u)
            .Build();
        var oversizedOut = WrapFileRecordOutput(oversizedRaw, returnedRecordNumber: 61, sequence: 4);

        var parsedOverflow = MftUsnNativeSource.ParseFileRecordBuffer(overflowOut, 60);
        var parsedOversized = MftUsnNativeSource.ParseFileRecordBuffer(oversizedOut, 61);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsedOverflow.Exists, Is.True);
            Assert.That(parsedOverflow.PrimaryName, Is.EqualTo("safe.bin"));
            Assert.That(parsedOverflow.SizeKnown, Is.False);

            Assert.That(parsedOversized.Exists, Is.True);
            Assert.That(parsedOversized.PrimaryName, Is.EqualTo("oversized.bin"));
            Assert.That(parsedOversized.SizeKnown, Is.False);
        }
    }

    [Test]
    public void Resident_attribute_list_resolves_extension_data_and_hardlink_names()
    {
        const int baseRec = 100;
        const ushort baseSeq = 7;
        const int extDataRec = 101;
        const int extNameRec = 102;
        const int staleExtRec = 103;

        var baseRaw = new MftRecordBuilder()
            .WithSequence(baseSeq)
            .AttributeList(
                new MftListEntry(MftLayout.AttributeFileName, 0, baseRec),
                new MftListEntry(MftLayout.AttributeFileName, 0, extNameRec),
                new MftListEntry(MftLayout.AttributeData, 0, extDataRec),
                new MftListEntry(MftLayout.AttributeData, 0, staleExtRec))
            .FileName(Parent, 2, "posix_link.bin", space: 0)
            .Build();
        var baseOut = WrapFileRecordOutput(baseRaw, baseRec, baseSeq);

        var extDataRaw = new MftRecordBuilder()
            .WithSequence(1)
            .ExtensionOf(baseRec, baseSeq)
            .NonResidentData(98_765_432L, startVcn: 0)
            .Build();
        var extDataOut = WrapFileRecordOutput(extDataRaw, extDataRec, sequence: 1);

        var extNameRaw = new MftRecordBuilder()
            .WithSequence(1)
            .ExtensionOf(baseRec, baseSeq)
            .FileName(88, 5, "primary_win32.bin", space: 1)
            .Build();
        var extNameOut = WrapFileRecordOutput(extNameRaw, extNameRec, sequence: 1);

        var staleExtRaw = new MftRecordBuilder()
            .WithSequence(1)
            .ExtensionOf(baseRec, (ushort)(baseSeq + 1))
            .NonResidentData(111L, startVcn: 0)
            .Build();
        var staleExtOut = WrapFileRecordOutput(staleExtRaw, staleExtRec, sequence: 1);

        var extensions = new Dictionary<int, byte[]>
        {
            [extDataRec] = extDataOut,
            [extNameRec] = extNameOut,
            [staleExtRec] = staleExtOut,
        };

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(
            baseOut,
            baseRec,
            (int rec, out ReadOnlySpan<byte> buf) =>
            {
                if (extensions.TryGetValue(rec, out var found))
                {
                    buf = found;
                    return true;
                }

                buf = default;
                return false;
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Exists, Is.True);
            Assert.That(parsed.Sequence, Is.EqualTo(baseSeq));
            Assert.That(parsed.SizeKnown, Is.True);
            Assert.That(parsed.Size, Is.EqualTo(98_765_432L));
            Assert.That(parsed.PrimaryName, Is.EqualTo("primary_win32.bin"));
            Assert.That(parsed.PrimaryParent, Is.EqualTo(88));
            Assert.That(parsed.PrimaryParentSequence, Is.EqualTo(5));
            Assert.That(parsed.TotalNameCount, Is.EqualTo(2));
            Assert.That(parsed.Alternates, Is.Not.Null);
            var alternates = parsed.Alternates!;
            Assert.That(alternates, Has.Count.EqualTo(1));
            Assert.That(alternates[0].Name, Is.EqualTo("posix_link.bin"));
            Assert.That(alternates[0].Parent, Is.EqualTo(Parent));
        }
    }

    [Test]
    public void Non_resident_attribute_list_leaves_size_unknown_safely()
    {
        var raw = new MftRecordBuilder()
            .WithSequence(2)
            .AttributeListRuns(64, [0x11, 0x01, 0x05, 0x00])
            .FileName(Parent, 1, "biglist.bin")
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 120, sequence: 2);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, 120);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Exists, Is.True);
            Assert.That(parsed.PrimaryName, Is.EqualTo("biglist.bin"));
            Assert.That(parsed.SizeKnown, Is.False);
            Assert.That(parsed.Size, Is.Zero);
        }
    }

    [Test]
    public void Hardlink_namespace_priority_excludes_dos_and_counts_distinct_links()
    {
        var raw = new MftRecordBuilder()
            .WithSequence(3)
            .FileName(Parent, 1, "PROGRA~1", space: MftLayout.NamespaceDos)
            .FileName(Parent, 1, "posix-name", space: 0)
            .FileName(50, 2, "win32-name", space: 1)
            .FileName(70, 4, "WIN32D~1", space: MftLayout.NamespaceDos)
            .FileName(70, 4, "win32-and-dos", space: 3)
            .FileName(50, 2, "win32-name", space: 1)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 130, sequence: 3);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, 130);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Exists, Is.True);
            Assert.That(parsed.PrimaryName, Is.EqualTo("win32-and-dos"));
            Assert.That(parsed.PrimaryParent, Is.EqualTo(70));
            Assert.That(parsed.PrimaryParentSequence, Is.EqualTo(4));
            Assert.That(parsed.TotalNameCount, Is.EqualTo(3));
            Assert.That(parsed.Alternates, Is.Not.Null);
            Assert.That(parsed.Alternates!, Has.Count.EqualTo(2));
            Assert.That(parsed.Alternates!.Select(static x => x.Name), Is.EquivalentTo(new[] { "posix-name", "win32-name" }));
        }
    }

    [Test]
    public void Dos_only_name_is_used_when_no_better_namespace_exists()
    {
        var raw = new MftRecordBuilder()
            .FileName(Parent, 1, "ONLY83~1.TXT", space: MftLayout.NamespaceDos)
            .ResidentData(10)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 131, sequence: 1);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, 131);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Exists, Is.True);
            Assert.That(parsed.PrimaryName, Is.EqualTo("ONLY83~1.TXT"));
            Assert.That(parsed.Alternates, Is.Null);
            Assert.That(parsed.TotalNameCount, Is.EqualTo(1));
        }
    }

    [TestCase(true, JunctionTag, false, true, TestName = "Junction directory is link")]
    [TestCase(false, SymlinkTag, false, true, TestName = "Symlink file is link")]
    [TestCase(false, CloudPlaceholderTag, false, false, TestName = "Data reparse tag on file is content")]
    [TestCase(true, CloudPlaceholderTag, false, false, TestName = "Data reparse tag on directory is content")]
    [TestCase(true, 0u, true, true, TestName = "Unreadable reparse on directory is link")]
    [TestCase(false, 0u, true, false, TestName = "Unreadable reparse on file is content")]
    public void Reparse_point_parity_matches_mft_reader(bool isDirectory, uint tag, bool nonResidentReparse, bool expectedIsLink)
    {
        var builder = new MftRecordBuilder().FileName(Parent, 1, "item");

        if (isDirectory)
        {
            builder.AsDirectory();
        }
        else
        {
            builder.ResidentData(256);
        }

        if (nonResidentReparse)
        {
            builder.NonResidentReparse();
        }
        else
        {
            builder.Reparse(tag);
        }

        var output = WrapFileRecordOutput(builder.Build(), returnedRecordNumber: 140, sequence: 1);
        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, 140);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.Exists, Is.True);
            Assert.That(parsed.IsDirectory, Is.EqualTo(isDirectory));
            Assert.That(parsed.IsLink, Is.EqualTo(expectedIsLink));
            Assert.That(parsed.Size, Is.EqualTo(isDirectory ? 0L : 256L));
        }
    }

    [Test]
    public void Standard_information_timestamps_are_extracted()
    {
        var creation = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var access = new DateTime(2025, 6, 2, 11, 0, 0, DateTimeKind.Utc);

        var raw = new MftRecordBuilder()
            .StandardInformation(creation, access)
            .FileName(Parent, 1, "timed.bin")
            .ResidentData(12)
            .Build();
        var output = WrapFileRecordOutput(raw, returnedRecordNumber: 150, sequence: 1);

        var parsed = MftUsnNativeSource.ParseFileRecordBuffer(output, 150);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.CreationFileTime, Is.EqualTo(creation.ToFileTimeUtc()));
            Assert.That(parsed.LastAccessFileTime, Is.EqualTo(access.ToFileTimeUtc()));
        }
    }

    [Test]
    public void Usn_buffer_parses_v2_records_and_handles_empty_and_truncated_tails()
    {
        var events = new List<MftUsnEvent>();

        var shortResult = MftUsnNativeSource.ParseUsnJournalBuffer(new byte[4], events, out var shortNext);
        Assert.That(shortResult, Is.False);
        Assert.That(shortNext, Is.Zero);

        var emptyBatch = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(emptyBatch, 1024L);
        var emptyResult = MftUsnNativeSource.ParseUsnJournalBuffer(emptyBatch, events, out var emptyNext);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(emptyResult, Is.True);
            Assert.That(emptyNext, Is.EqualTo(1024L));
            Assert.That(events, Is.Empty);
        }

        const int rec1Len = 64;
        const int rec2Len = 72;
        const int rec3Len = 64;
        var validBuffer = new byte[8 + rec1Len + rec2Len + rec3Len];
        BinaryPrimitives.WriteInt64LittleEndian(validBuffer, 9000L);

        WriteUsnRecord(validBuffer.AsSpan(8, rec1Len), rec1Len, majorVersion: 2, frn: 0x0002_0000_0000_0050UL, reason: 0x00000100u);
        WriteUsnRecord(validBuffer.AsSpan(8 + rec1Len, rec2Len), rec2Len, majorVersion: 3, frn: 0x0001_0000_0000_0051UL, reason: 0x00000200u);
        WriteUsnRecord(validBuffer.AsSpan(8 + rec1Len + rec2Len, rec3Len), rec3Len, majorVersion: 2, frn: 0x0003_0000_0000_0052UL, reason: 0x80000002u);

        var ok = MftUsnNativeSource.ParseUsnJournalBuffer(validBuffer, events, out var nextUsn);

        const int truncatedTailLen = 64;
        var truncatedBuffer = new byte[validBuffer.Length + truncatedTailLen];
        validBuffer.CopyTo(truncatedBuffer.AsSpan());
        WriteUsnRecord(truncatedBuffer.AsSpan(validBuffer.Length, truncatedTailLen), declaredLen: 128, majorVersion: 2, frn: 0x0004_0000_0000_0053UL, reason: 0x1u);
        var truncatedEvents = new List<MftUsnEvent>();
        var truncatedOk = MftUsnNativeSource.ParseUsnJournalBuffer(truncatedBuffer, truncatedEvents, out _);

        var trailingGarbageBuffer = new byte[validBuffer.Length + 12];
        validBuffer.CopyTo(trailingGarbageBuffer.AsSpan());
        var trailingOk = MftUsnNativeSource.ParseUsnJournalBuffer(trailingGarbageBuffer, new List<MftUsnEvent>(), out _);

        var tinyRecLenBuffer = new byte[8 + 64];
        BinaryPrimitives.WriteInt64LittleEndian(tinyRecLenBuffer, 9001L);
        WriteUsnRecord(tinyRecLenBuffer.AsSpan(8, 64), declaredLen: 32, majorVersion: 2, frn: 0x0005_0000_0000_0054UL, reason: 0x1u);
        var tinyRecLenOk = MftUsnNativeSource.ParseUsnJournalBuffer(tinyRecLenBuffer, new List<MftUsnEvent>(), out _);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.True);
            Assert.That(nextUsn, Is.EqualTo(9000L));
            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events[0], Is.EqualTo(new MftUsnEvent(0x0002_0000_0000_0050UL, 0x00000100u)));
            Assert.That(events[1], Is.EqualTo(new MftUsnEvent(0x0003_0000_0000_0052UL, 0x80000002u)));
            Assert.That(truncatedOk, Is.False);
            Assert.That(trailingOk, Is.False);
            Assert.That(tinyRecLenOk, Is.False);
        }
    }

    [TestCase(0, 0)]
    [TestCase(1181, 1)]
    [TestCase(87, 2)]
    [TestCase(1179, 3)]
    [TestCase(1178, 3)]
    [TestCase(5, 4)]
    [TestCase(1, 5)]
    [TestCase(50, 5)]
    [TestCase(2, 6)]
    [TestCase(1117, 6)]
    public void Win32_error_classification_maps_expected_kinds(int win32Error, int expectedKind)
    {
        Assert.That(MftUsnNativeSource.ClassifyWin32Error(win32Error), Is.EqualTo((MftUsnErrorKind)expectedKind));
    }

    private static byte[] WrapFileRecordOutput(byte[] rawRecord, int returnedRecordNumber, ushort sequence, bool applyFixup = true)
    {
        var copy = (byte[])rawRecord.Clone();

        if (applyFixup)
        {
            MftLayout.ApplyFixup(copy, 512);
        }

        var output = new byte[12 + copy.Length];
        var frn = ((ulong)returnedRecordNumber & (ulong)MftLayout.ReferenceMask) | ((ulong)sequence << MftLayout.ReferenceMaskBits);
        BinaryPrimitives.WriteUInt64LittleEndian(output, frn);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(8), copy.Length);
        copy.CopyTo(output.AsSpan(12));
        return output;
    }

    private static void WriteUsnRecord(Span<byte> dest, int declaredLen, ushort majorVersion, ulong frn, uint reason)
    {
        BinaryPrimitives.WriteInt32LittleEndian(dest, declaredLen);
        BinaryPrimitives.WriteUInt16LittleEndian(dest[4..], majorVersion);
        BinaryPrimitives.WriteUInt64LittleEndian(dest[8..], frn);
        BinaryPrimitives.WriteUInt32LittleEndian(dest[40..], reason);
    }
}
