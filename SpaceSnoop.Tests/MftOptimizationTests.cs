using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;
using System.Runtime.CompilerServices;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftOptimizationTests
{
    [Test]
    public void MftEntry_Is40BytesAndReferenceFree()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unsafe.SizeOf<MftEntry>(), Is.EqualTo(40));
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<MftEntry>(), Is.False);
        }
    }

    [Test]
    public void MftEntry_NameIdZeroWithHasNameIsDistinctFromDefault()
    {
        var withZeroId = new MftEntry { Present = true };
        withZeroId.SetName(0);

        var withoutName = new MftEntry { Present = true };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withZeroId.HasName, Is.True);
            Assert.That(withZeroId.NameId, Is.Zero);
            Assert.That(withZeroId.Exists, Is.True);

            Assert.That(withoutName.HasName, Is.False);
            Assert.That(withoutName.NameId, Is.Zero);
            Assert.That(withoutName.Exists, Is.False);
        }

        withZeroId.ClearName();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withZeroId.HasName, Is.False);
            Assert.That(withZeroId.Exists, Is.False);
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
    public void LocalNameCache_HandlesAsciiCyrillicSurrogatesAndPreSizedShards()
    {
        var names = new NameTable(16, singleWriter: false, initialSlotCapacity: 4096);
        var cache = LocalNameCache.Create();

        var idAscii = cache.Intern(names, "System32".AsSpan());
        var idAsciiCached = cache.Intern(names, "System32".AsSpan());
        var idCyrillic = cache.Intern(names, "Отчёт_Данные.xlsx".AsSpan());
        var idSurrogate = cache.Intern(names, "файл_🚀_тест.bin".AsSpan());
        var idEmpty = cache.Intern(names, ReadOnlySpan<char>.Empty);
        var idUtf8 = cache.Intern(names, "System32"u8);

        var longSample = new string('Ж', 250);
        var ids = new List<(int Id, string Expected)>();

        for (var i = 0; i < 5000; i++)
        {
            var expected = $"{longSample}_{i:D4}";
            ids.Add((cache.Intern(names, expected.AsSpan()), expected));
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(idAsciiCached, Is.EqualTo(idAscii));
            Assert.That(idUtf8, Is.EqualTo(idAscii));
            Assert.That(names.GetString(idAscii), Is.EqualTo("System32"));
            Assert.That(names.GetString(idCyrillic), Is.EqualTo("Отчёт_Данные.xlsx"));
            Assert.That(names.GetString(idSurrogate), Is.EqualTo("файл_🚀_тест.bin"));
            Assert.That(names.GetString(idEmpty), Is.Empty);
            Assert.That(names.GetUtf8(idEmpty).IsEmpty, Is.True);

            Assert.That(names.Equals(idAscii, "System32", StringComparison.Ordinal), Is.True);
            Assert.That(names.Equals(idAscii, "system32", StringComparison.Ordinal), Is.False);
            Assert.That(names.Equals(idAscii, "system32", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(names.Equals(idCyrillic, "Отчёт_Данные.xlsx", StringComparison.Ordinal), Is.True);
            Assert.That(names.Equals(idCyrillic, "отчёт_данные.xlsx", StringComparison.Ordinal), Is.False);
            Assert.That(names.Equals(idCyrillic, "отчёт_данные.xlsx", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(names.Equals(idSurrogate, "файл_🚀_тест.bin", StringComparison.Ordinal), Is.True);
            Assert.That(names.Equals(idSurrogate, "ФАЙЛ_🚀_ТЕСТ.BIN", StringComparison.OrdinalIgnoreCase), Is.True);

            Assert.That(names.GetString(ids[0].Id), Is.EqualTo(ids[0].Expected));
            Assert.That(names.GetString(ids[^1].Id), Is.EqualTo(ids[^1].Expected));
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
    public void LocalNameCache_PreservesLoneSurrogatesDistinctFromReplacementChar()
    {
        var names = new NameTable(1);
        var cache = LocalNameCache.Create();

        const string loneSurrogate = "a\uD800b";
        const string replacementChar = "a\uFFFDb";

        var idSurrogate = cache.Intern(names, loneSurrogate.AsSpan());
        var idReplacement = cache.Intern(names, replacementChar.AsSpan());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names.GetUtf8(idSurrogate).SequenceEqual(names.GetUtf8(idReplacement)), Is.False);
            Assert.That(names.GetString(idSurrogate), Is.EqualTo(loneSurrogate));
            Assert.That(names.GetString(idReplacement), Is.EqualTo(replacementChar));
            Assert.That(names.Equals(idSurrogate, loneSurrogate, StringComparison.Ordinal), Is.True);
            Assert.That(names.Equals(idSurrogate, replacementChar, StringComparison.Ordinal), Is.False);
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
            Assert.That(table.Entries[20].HasName, Is.True);
            Assert.That(table.Entries[20].Exists, Is.True);
            Assert.That(table.Names.GetString(table.Entries[20].NameId), Is.EqualTo(primaryName));
            Assert.That(table.Entries[20].Names, Is.EqualTo(2));
            Assert.That(table.Alternates.ContainsKey(20), Is.True);
            Assert.That(table.Alternates[20], Has.Count.EqualTo(1));
            Assert.That(table.Names.GetString(table.Alternates[20][0].NameId), Is.EqualTo(alternateName));
        }
    }

    [Test]
    public void MftScanner_ParallelMaterialize_RootAndSubdirFoldTotalsDropUnreachableAndProgress()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "BranchA"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "BranchB"))
            .Record(22, new MftRecordBuilder().AsDirectory().FileName(20, 1, "LeafA1"))
            .Record(23, new MftRecordBuilder().AsDirectory().FileName(20, 1, "SubA2"))
            .Record(24, new MftRecordBuilder().AsDirectory().FileName(23, 1, "DeepA2"))
            .Record(25, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "root.bin").ResidentData(100))
            .Record(26, new MftRecordBuilder().FileName(20, 1, "a.bin").ResidentData(200))
            .Record(27, new MftRecordBuilder().FileName(22, 1, "a1.bin").ResidentData(300))
            .Record(28, new MftRecordBuilder().FileName(24, 1, "deep.bin").ResidentData(400))
            .Record(29, new MftRecordBuilder().FileName(21, 1, "b.bin").ResidentData(500))
            .Record(30, new MftRecordBuilder().AsDirectory().FileName(99, 1, "OrphanDir"))
            .Record(31, new MftRecordBuilder().FileName(30, 1, "orphan.bin").ResidentData(777))
            .Build(128);

        var scanner = new MftScanner();
        var rootProgress = new ScanProgress();
        var rootResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 4, 4096, rootProgress);
        var rootSnapshot = rootProgress.CreateSnapshot();

        var subProgress = new ScanProgress();
        var subResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\BranchA"), 'T', 4, 4096, subProgress);
        var subSnapshot = subProgress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootResult.Root.TotalSize, Is.EqualTo(1500));
            Assert.That(rootResult.Root.TotalFileCount, Is.EqualTo(5));
            Assert.That(rootResult.Root.TotalDirectoryCount, Is.EqualTo(5));
            Assert.That(rootResult.DetachedRecords, Is.EqualTo(2));
            Assert.That(rootResult.OrphanFiles, Is.EqualTo(1));
            Assert.That(rootResult.OrphanBytes, Is.EqualTo(777));
            Assert.That(rootSnapshot.DirectoriesScanned, Is.EqualTo(6));
            Assert.That(rootSnapshot.FilesScanned, Is.EqualTo(5));
            Assert.That(rootSnapshot.BytesScanned, Is.EqualTo(1500));
            Assert.That(rootSnapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(rootSnapshot.TopLevelCompleted, Is.EqualTo(2));

            var postFreezeId = rootResult.Root.Store!.Names.Intern("post_freeze.txt");
            Assert.That(rootResult.Root.Store.Names.GetString(postFreezeId), Is.EqualTo("post_freeze.txt"));

            Assert.That(subResult.Root.TotalSize, Is.EqualTo(900));
            Assert.That(subResult.Root.TotalFileCount, Is.EqualTo(3));
            Assert.That(subResult.Root.TotalDirectoryCount, Is.EqualTo(3));
            Assert.That(subResult.DetachedRecords, Is.EqualTo(2));
            Assert.That(subResult.OrphanFiles, Is.EqualTo(1));
            Assert.That(subResult.OrphanBytes, Is.EqualTo(777));
            Assert.That(subSnapshot.DirectoriesScanned, Is.EqualTo(4));
            Assert.That(subSnapshot.FilesScanned, Is.EqualTo(3));
            Assert.That(subSnapshot.BytesScanned, Is.EqualTo(900));
            Assert.That(subSnapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(subSnapshot.TopLevelCompleted, Is.EqualTo(2));
        }
    }
}
