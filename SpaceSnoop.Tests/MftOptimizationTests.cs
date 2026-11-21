using SpaceSnoop.Core.Mft;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftOptimizationTests
{
    [Test]
    public void MftEntry_Is48BytesAndReferenceFree()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unsafe.SizeOf<MftNameRef>(), Is.EqualTo(8));
            Assert.That(Unsafe.SizeOf<MftEntry>(), Is.EqualTo(48));
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<MftEntry>(), Is.False);
        }
    }

    [Test]
    public void MftEntry_IncrementNames_SaturatesAtUShortMax()
    {
        var entry = new MftEntry
        {
            Names = ushort.MaxValue - 1,
        };

        entry.IncrementNames();
        Assert.That(entry.Names, Is.EqualTo(ushort.MaxValue));

        entry.IncrementNames();
        Assert.That(entry.Names, Is.EqualTo(ushort.MaxValue));
    }

    [Test]
    public void MftNameArena_HandlesAsciiCyrillicSurrogatesAndMultiChunkWriters()
    {
        var arena = new MftNameArena();
        var writer = new MftNameArenaWriter(arena);

        var rAscii = writer.Append("System32".AsSpan());
        var rCyrillic = writer.Append("Отчёт_Данные.xlsx".AsSpan());
        var rSurrogate = writer.Append("файл_🚀_тест.bin".AsSpan());
        var rEmpty = writer.Append(ReadOnlySpan<char>.Empty);

        var longSample = new string('Ж', 250);
        var refs = new List<(MftNameRef Ref, string Expected)>();

        for (var i = 0; i < 5000; i++)
        {
            var expected = $"{longSample}_{i:D4}";
            var bytes = Encoding.Unicode.GetBytes(expected);
            refs.Add((writer.AppendUtf16(bytes), expected));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(arena.GetString(rAscii), Is.EqualTo("System32"));
            Assert.That(arena.GetString(rCyrillic), Is.EqualTo("Отчёт_Данные.xlsx"));
            Assert.That(arena.GetString(rSurrogate), Is.EqualTo("файл_🚀_тест.bin"));
            Assert.That(arena.GetString(rEmpty), Is.Empty);
            Assert.That(arena.GetUtf8(rEmpty).IsEmpty, Is.True);

            Assert.That(arena.Equals(rAscii, "System32", StringComparison.Ordinal), Is.True);
            Assert.That(arena.Equals(rAscii, "system32", StringComparison.Ordinal), Is.False);
            Assert.That(arena.Equals(rAscii, "system32", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(arena.Equals(rCyrillic, "Отчёт_Данные.xlsx", StringComparison.Ordinal), Is.True);
            Assert.That(arena.Equals(rCyrillic, "отчёт_данные.xlsx", StringComparison.Ordinal), Is.False);
            Assert.That(arena.Equals(rCyrillic, "отчёт_данные.xlsx", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(arena.Equals(rSurrogate, "файл_🚀_тест.bin", StringComparison.Ordinal), Is.True);
            Assert.That(arena.Equals(rSurrogate, "ФАЙЛ_🚀_ТЕСТ.BIN", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(refs.Max(static x => x.Ref.Chunk), Is.GreaterThanOrEqualTo(1));
            Assert.That(arena.GetString(refs[0].Ref), Is.EqualTo(refs[0].Expected));
            Assert.That(arena.GetString(refs[^1].Ref), Is.EqualTo(refs[^1].Expected));
        }
    }

    [Test]
    public void MftScanner_Calculate_ProducesStoreBackedTreeWithExactTimestampsAndCaseLocate()
    {
        var winterUtc = new DateTime(2024, 1, 15, 8, 30, 0, DateTimeKind.Utc);
        var summerUtc = new DateTime(2024, 7, 15, 8, 30, 0, DateTimeKind.Utc);
        var dstTransitionUtc = new DateTime(2024, 3, 10, 7, 30, 0, DateTimeKind.Utc);
        var zeroFileTimeUtc = DateTime.FromFileTimeUtc(0);

        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Данные"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "данные"))
            .Record(
                22,
                new MftRecordBuilder()
                    .StandardInformation(winterUtc, summerUtc)
                    .FileName(21, 1, "отчёт_🚀.bin")
                    .NonResidentData(1234))
            .Record(
                23,
                new MftRecordBuilder()
                    .StandardInformation(dstTransitionUtc, zeroFileTimeUtc)
                    .FileName(21, 1, "нулевой_штамп.bin")
                    .ResidentData(200))
            .Record(
                24,
                new MftRecordBuilder()
                    .FileName(20, 1, "чужой.bin")
                    .NonResidentData(9999))
            .Build(32);

        var scanner = new MftScanner();
        var result = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\данные"), 'T', 2, 1024, null);
        var root = result.Root;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Store, Is.Not.Null);
            Assert.That(root.Name, Is.EqualTo("данные"));
            Assert.That(root.TotalFileCount, Is.EqualTo(2));
            Assert.That(root.TotalSize, Is.EqualTo(1434));
            Assert.That(root.Files, Has.Count.EqualTo(2));

            var reportFile = root.Files.Single(static f => f.Name == "отчёт_🚀.bin");
            Assert.That(reportFile.Size, Is.EqualTo(1234));
            Assert.That(reportFile.CreationDate, Is.EqualTo(winterUtc.ToLocalTime()));
            Assert.That(reportFile.LastAccessTime, Is.EqualTo(summerUtc.ToLocalTime()));

            var zeroStampFile = root.Files.Single(static f => f.Name == "нулевой_штамп.bin");
            Assert.That(zeroStampFile.Size, Is.EqualTo(200));
            Assert.That(zeroStampFile.CreationDate, Is.EqualTo(dstTransitionUtc.ToLocalTime()));
            Assert.That(zeroStampFile.LastAccessTime, Is.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public void MftNameArena_PreservesLoneSurrogatesDistinctFromReplacementChar()
    {
        var arena = new MftNameArena();
        var writer = new MftNameArenaWriter(arena);

        const string loneSurrogate = "a\uD800b";
        const string replacementChar = "a\uFFFDb";

        var refSurrogate = writer.Append(loneSurrogate.AsSpan());
        var refReplacement = writer.Append(replacementChar.AsSpan());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(arena.GetUtf8(refSurrogate).SequenceEqual(arena.GetUtf8(refReplacement)), Is.False);
            Assert.That(arena.GetString(refSurrogate), Is.EqualTo(loneSurrogate));
            Assert.That(arena.GetString(refReplacement), Is.EqualTo(replacementChar));
            Assert.That(arena.Equals(refSurrogate, loneSurrogate, StringComparison.Ordinal), Is.True);
            Assert.That(arena.Equals(refSurrogate, replacementChar, StringComparison.Ordinal), Is.False);
        }
    }

    [Test]
    public void MftReader_RetainsDistinctAlternateNamesWithLoneSurrogates()
    {
        const string primaryName = "bad_\uD800.bin";
        const string alternateName = "bad_\uFFFD.bin";

        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(
                20,
                new MftRecordBuilder()
                    .FileName(MftLayout.RootRecord, 1, primaryName, space: 1)
                    .FileName(MftLayout.RootRecord, 1, alternateName, space: 3)
                    .ResidentData(64))
            .Build(32);

        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 1, 1024, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Names.GetString(table.Entries[20].Name), Is.EqualTo(primaryName));
            Assert.That(table.Entries[20].Names, Is.EqualTo(2));
            Assert.That(table.Alternates.ContainsKey(20), Is.True);
            Assert.That(table.Alternates[20], Has.Count.EqualTo(1));
            Assert.That(table.Names.GetString(table.Alternates[20][0].Name), Is.EqualTo(alternateName));
        }
    }
}
