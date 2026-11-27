using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftUsnIndexTests
{
    private const uint DataExtendReason = 0x0000_0002u;
    private const uint FileCreateReason = 0x0000_0100u;
    private const uint FileDeleteReason = 0x0000_0200u;
    private const uint HardlinkChangeReason = 0x0001_0000u;
    private const uint RenameNewNameReason = 0x0000_2000u;
    private const uint CloseReason = 0x8000_0000u;

    [TestCase(0)]
    [TestCase(100)]
    [TestCase(1000)]
    public void TryRefresh_AppliesZeroHundredAndThousandDeltasAccurately(int fileCount)
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Work"))
            .Record(21, new MftRecordBuilder().FileName(20, 1, "seed.bin").ResidentData(100))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 0xAA55u, firstUsn: 0, nextUsn: 1000);
        var scanner = CreateScanner(source);

        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(cold.UsedUsn, Is.False);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        var expectedAddedBytes = 0L;

        for (var i = 0; i < fileCount; i++)
        {
            var rec = 30 + i;
            var size = 50L + i;
            expectedAddedBytes += size;
            source.SetRecord(
                rec,
                new MftUsnRecord
                {
                    Exists = true,
                    Sequence = 1,
                    PrimaryParent = 20,
                    PrimaryParentSequence = 1,
                    PrimaryName = $"f_{i:D4}.bin",
                    Size = size,
                    SizeKnown = true,
                    TotalNameCount = 1,
                });
            source.QueueEvent(MakeFrn(rec, 1), FileCreateReason | CloseReason);
        }

        source.AdvanceUsn(2000);
        var warm = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warm.UsedUsn, Is.True);
            Assert.That(warm.Root.TotalSize, Is.EqualTo(100 + expectedAddedBytes));
            Assert.That(warm.Root.TotalFileCount, Is.EqualTo(1 + fileCount));
            Assert.That(warm.Root.TotalDirectoryCount, Is.EqualTo(1));
            Assert.That(source.ReadDeltaCalls, Is.EqualTo(new[] { (100UL, 1000L), (100UL, 1000L) }));
        }

        source.AdvanceUsn(2500);
        var warm2 = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warm2.UsedUsn, Is.True);
            Assert.That(source.ReadDeltaCalls, Is.EqualTo(new[] { (100UL, 1000L), (100UL, 1000L), (100UL, 2000L) }));
        }
    }

    [Test]
    public void TryRefresh_NestedDirectoryCreationAndSameBatchParentChildDeleteAvoidDoubleSubtraction()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Anchor"))
            .Record(21, new MftRecordBuilder().FileName(20, 1, "keep.bin").ResidentData(500))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        source.SetRecord(
            32,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = 31,
                PrimaryParentSequence = 1,
                PrimaryName = "Level2",
            });
        source.SetRecord(
            31,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = 30,
                PrimaryParentSequence = 1,
                PrimaryName = "Level1",
            });
        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = MftLayout.RootRecord,
                PrimaryParentSequence = 1,
                PrimaryName = "NewRoot",
            });
        source.SetRecord(
            33,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 32,
                PrimaryParentSequence = 1,
                PrimaryName = "nested.bin",
                Size = 1234,
                SizeKnown = true,
                TotalNameCount = 1,
            });

        source.QueueEvent(MakeFrn(32, 1), FileCreateReason | CloseReason);
        source.QueueEvent(MakeFrn(33, 1), FileCreateReason | CloseReason);
        source.QueueEvent(MakeFrn(31, 1), FileCreateReason | CloseReason);
        source.QueueEvent(MakeFrn(30, 1), FileCreateReason | CloseReason);
        source.AdvanceUsn(200);

        var afterCreate = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterCreate.Root.TotalSize, Is.EqualTo(1734));
            Assert.That(afterCreate.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(afterCreate.Root.TotalDirectoryCount, Is.EqualTo(4));
        }

        source.RemoveRecord(30);
        source.RemoveRecord(31);
        source.RemoveRecord(32);
        source.RemoveRecord(33);
        source.QueueEvent(MakeFrn(30, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(31, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(32, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(33, 1), FileDeleteReason | CloseReason);
        source.AdvanceUsn(300);

        var afterDelete = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDelete.Root.TotalSize, Is.EqualTo(500));
            Assert.That(afterDelete.Root.TotalFileCount, Is.EqualTo(1));
            Assert.That(afterDelete.Root.TotalDirectoryCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void TryRefresh_DirectoryMovePreservesSubtreeTotalsAndCanonicalFacade()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Src"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Dst"))
            .Record(22, new MftRecordBuilder().AsDirectory().FileName(20, 1, "Branch"))
            .Record(23, new MftRecordBuilder().FileName(22, 1, "payload.bin").NonResidentData(4096))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var srcNode = cold.Root.SubDirectories.Single(static x => x.Name == "Src");
        var dstNode = cold.Root.SubDirectories.Single(static x => x.Name == "Dst");
        var branchNode = srcNode.SubDirectories.Single();

        source.SetRecord(
            22,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = 21,
                PrimaryParentSequence = 1,
                PrimaryName = "BranchMoved",
            });
        source.QueueEvent(MakeFrn(22, 1), RenameNewNameReason | CloseReason);
        source.AdvanceUsn(200);

        var warm = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warm.UsedUsn, Is.True);
            Assert.That(srcNode.TotalSize, Is.Zero);
            Assert.That(srcNode.TotalFileCount, Is.Zero);
            Assert.That(srcNode.TotalDirectoryCount, Is.Zero);
            Assert.That(dstNode.TotalSize, Is.EqualTo(4096));
            Assert.That(dstNode.TotalFileCount, Is.EqualTo(1));
            Assert.That(dstNode.TotalDirectoryCount, Is.EqualTo(1));
            Assert.That(ReferenceEquals(dstNode.SubDirectories.Single(), branchNode), Is.True);
            Assert.That(branchNode.Name, Is.EqualTo("BranchMoved"));
            Assert.That(branchNode.AbsolutePath, Is.EqualTo(@"T:\Dst\BranchMoved"));
        }
    }

    [Test]
    public void TryRefresh_HardlinksDeduplicateAndSurviveUntilLastLinkDeleted()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Links"))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var dir = cold.Root.SubDirectories.Single();

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "a.bin",
                Size = 10_000,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.QueueEvent(MakeFrn(30, 1), FileCreateReason | CloseReason);
        source.AdvanceUsn(200);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.EqualTo(10_000));
        Assert.That(dir.TotalFileCount, Is.EqualTo(1));

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "a.bin",
                Alternates = [new MftUsnNameEntry(20, 1, "b.bin", 1)],
                Size = 10_000,
                SizeKnown = true,
                TotalNameCount = 2,
            });
        source.QueueEvent(MakeFrn(30, 1), HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(300);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.EqualTo(10_000));
        Assert.That(dir.TotalFileCount, Is.EqualTo(1));

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "b.bin",
                Size = 10_000,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.QueueEvent(MakeFrn(30, 1), HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(400);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dir.TotalSize, Is.EqualTo(10_000));
            Assert.That(dir.TotalFileCount, Is.EqualTo(1));
            Assert.That(dir.Files.Single().Name, Is.EqualTo("b.bin"));
        }

        source.RemoveRecord(30);
        source.QueueEvent(MakeFrn(30, 1), FileDeleteReason | CloseReason);
        source.AdvanceUsn(500);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.Zero);
        Assert.That(dir.TotalFileCount, Is.Zero);
    }

    [Test]
    public void TryRefresh_OpenDirtyFrnsQueriesLiveSizeUntilClose()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Logs"))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var dir = cold.Root.SubDirectories.Single();

        var frn = MakeFrn(30, 1);
        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "app.log",
                Size = 4096,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.SetLiveSize(frn, 4096);
        source.QueueEvent(frn, FileCreateReason | DataExtendReason);
        source.AdvanceUsn(200);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.EqualTo(4096));

        source.SetLiveSize(frn, 4096 + 1_048_576);
        source.AdvanceUsn(250);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.EqualTo(4096 + 1_048_576));

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "app.log",
                Size = 4096 + 1_048_576,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.RemoveLiveSize(frn);
        source.QueueEvent(frn, CloseReason);
        source.AdvanceUsn(300);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(dir.TotalSize, Is.EqualTo(4096 + 1_048_576));
    }

    [Test]
    public void TryRefresh_IsIdempotentWithUiRemoveAndAddFileAndResolvesSubdirectory()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Alpha"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(20, 1, "Inner"))
            .Record(22, new MftRecordBuilder().FileName(21, 1, "dead.bin").ResidentData(200))
            .Record(23, new MftRecordBuilder().FileName(20, 1, "keep.bin").ResidentData(300))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var alpha = cold.Root.SubDirectories.Single();
        var inner = alpha.SubDirectories.Single();
        var deadFile = inner.Files.Single();

        inner.Remove(deadFile);
        _ = alpha.AddFile("archive.zip", 150, DateTime.Now, DateTime.Now);
        alpha.Remove(inner);

        Assert.That(cold.Root.TotalSize, Is.EqualTo(450));

        source.RemoveRecord(22);
        source.RemoveRecord(21);
        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "archive.zip",
                Size = 150,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.QueueEvent(MakeFrn(22, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(21, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(30, 1), FileCreateReason | CloseReason);
        source.AdvanceUsn(200);

        var subResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Alpha"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(subResult.UsedUsn, Is.True);
            Assert.That(subResult.Root.AbsolutePath, Is.EqualTo(@"T:\Alpha"));
            Assert.That(subResult.Root.TotalSize, Is.EqualTo(450));
            Assert.That(subResult.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(subResult.Root.TotalDirectoryCount, Is.Zero);
            Assert.That(cold.Root.TotalSize, Is.EqualTo(450));
        }
    }

    [TestCase(1, 1181)]
    [TestCase(2, 87)]
    [TestCase(3, 1179)]
    public void TryRefresh_FallsBackToFullScanOnUsnErrors(int rawErrorKind, int win32Error)
    {
        var errorKind = (MftUsnErrorKind)rawErrorKind;
        var image = BuildSimpleImage();
        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);

        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        source.ReadError = (errorKind, win32Error);
        var fallback = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(fallback.UsedUsn, Is.False);
        Assert.That(fallback.Root.TotalSize, Is.EqualTo(123));
    }

    [Test]
    public void TryRefresh_FallsBackOnJournalIdFirstUsnSerialDeltaThresholdAndEvictsLruAndInvalidate()
    {
        var image = BuildSimpleImage();
        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source, maxDeltaRecords: 3);

        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        source.JournalId = 200UL;
        var r1 = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(r1.UsedUsn, Is.False);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        source.AdvanceUsn(600);
        source.FirstUsn = 500;
        var r2 = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(r2.UsedUsn, Is.False);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        source.VolumeSerial = 999u;
        var r3 = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(r3.UsedUsn, Is.False);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        for (var i = 0; i < 4; i++)
        {
            source.QueueEvent(MakeFrn(30 + i, 1), FileCreateReason | CloseReason);
        }

        source.AdvanceUsn(700);
        var r4 = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(r4.UsedUsn, Is.False);

        scanner.InvalidateUsnCache(@"T:\");
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.False);

        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"U:\"), 'U', 2, 4096, null);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"V:\"), 'V', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.False);
            Assert.That(scanner.HasUsnCacheFor(@"U:\"), Is.True);
            Assert.That(scanner.HasUsnCacheFor(@"V:\"), Is.True);
        }

        scanner.InvalidateUsnCache();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scanner.HasUsnCacheFor(@"U:\"), Is.False);
            Assert.That(scanner.HasUsnCacheFor(@"V:\"), Is.False);
        }
    }

    [Test]
    public void TryRefresh_SingleDirectoryFiveThousandFilesCreateModifyAndDeleteUsesO1SwapAndBatchIndex()
    {
        const int count = 5000;
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Bulk"))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var bulk = cold.Root.SubDirectories.Single();

        var expectedSize = 0L;

        for (var i = 0; i < count; i++)
        {
            var rec = 30 + i;
            var size = 100L + i;
            expectedSize += size;
            source.SetRecord(
                rec,
                new MftUsnRecord
                {
                    Exists = true,
                    Sequence = 1,
                    PrimaryParent = 20,
                    PrimaryParentSequence = 1,
                    PrimaryName = $"bulk_{i:D4}.bin",
                    Size = size,
                    SizeKnown = true,
                    TotalNameCount = 1,
                });
            source.QueueEvent(MakeFrn(rec, 1), FileCreateReason | CloseReason);
        }

        source.AdvanceUsn(200);
        var afterCreate = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterCreate.UsedUsn, Is.True);
            Assert.That(bulk.TotalSize, Is.EqualTo(expectedSize));
            Assert.That(bulk.TotalFileCount, Is.EqualTo(count));
        }

        var modifiedSize = 0L;

        for (var i = 0; i < count; i++)
        {
            var rec = 30 + i;
            var newSize = 200L + (i * 2);
            modifiedSize += newSize;
            source.SetRecord(
                rec,
                new MftUsnRecord
                {
                    Exists = true,
                    Sequence = 1,
                    PrimaryParent = 20,
                    PrimaryParentSequence = 1,
                    PrimaryName = $"renamed_{i:D4}.bin",
                    Size = newSize,
                    SizeKnown = true,
                    TotalNameCount = 1,
                });
            source.QueueEvent(MakeFrn(rec, 1), RenameNewNameReason | DataExtendReason | CloseReason);
        }

        source.AdvanceUsn(300);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bulk.TotalSize, Is.EqualTo(modifiedSize));
            Assert.That(bulk.TotalFileCount, Is.EqualTo(count));
        }

        for (var i = 0; i < count; i++)
        {
            var rec = 30 + i;
            source.RemoveRecord(rec);
            source.QueueEvent(MakeFrn(rec, 1), FileDeleteReason | CloseReason);
        }

        source.AdvanceUsn(400);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bulk.TotalSize, Is.Zero);
            Assert.That(bulk.TotalFileCount, Is.Zero);
        }
    }

    [TestCase(21, 22)]
    [TestCase(22, 21)]
    public void TryRefresh_SameDeltaFilePathReplacementPreservesNewFile(int oldRec, int newRec)
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Work"))
            .Record(oldRec, new MftRecordBuilder().FileName(20, 1, "target.bin").ResidentData(100))
            .Record(newRec, new MftRecordBuilder().FileName(20, 1, "staging.bin").ResidentData(500))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var work = cold.Root.SubDirectories.Single();

        source.RemoveRecord(oldRec);
        source.SetRecord(
            newRec,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "target.bin",
                Size = 750,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.QueueEvent(MakeFrn(oldRec, 1), FileDeleteReason | CloseReason);
        source.QueueEvent(MakeFrn(newRec, 1), RenameNewNameReason | DataExtendReason | CloseReason);
        source.AdvanceUsn(200);

        var warm = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warm.UsedUsn, Is.True);
            Assert.That(work.TotalFileCount, Is.EqualTo(1));
            Assert.That(work.TotalSize, Is.EqualTo(750));
            Assert.That(work.Files.Single().Name, Is.EqualTo("target.bin"));
            Assert.That(work.Files.Single().Size, Is.EqualTo(750));
        }
    }

    [Test]
    public void TryRefresh_FetchIoErrorAbortsWithoutMutatingStoreOrAdvancingCursor()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Data"))
            .Record(21, new MftRecordBuilder().FileName(20, 1, "live.bin").ResidentData(256))
            .Build(64);

        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 1, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var reachable = new ulong[(table.Entries.Length + 63) >> 6];
        MftLinks.SetReachable(reachable, MftLayout.RootRecord);
        var capture = new MftUsnMaterializeCapture(table.Entries.Length);
        var root = MftScanner.Materialize(table, links, MftLayout.RootRecord, new DirectoryInfo(@"T:\"), new MftSubtreeTotals(), reachable, null, CancellationToken.None, capture);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var index = new MftUsnVolumeIndex(root.Store!, new MftUsnJournalInfo(100UL, 0, 100, 0, 1u), table, capture);

        source.SetFetchError(21, MftUsnErrorKind.IoError, 1117);
        source.QueueEvent(MakeFrn(21, 1), DataExtendReason | CloseReason);
        source.AdvanceUsn(200);

        var ok = index.TryRefresh(source, out var summary, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.False);
            Assert.That(summary.ErrorKind, Is.EqualTo(MftUsnErrorKind.IoError));
            Assert.That(summary.Win32Error, Is.EqualTo(1117));
            Assert.That(index.CursorUsn, Is.EqualTo(100));
            Assert.That(root.TotalFileCount, Is.EqualTo(1));
            Assert.That(root.TotalSize, Is.EqualTo(256));
        }

        var scanner = CreateScanner(source);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        source.SetFetchError(20, MftUsnErrorKind.IoError, 1117);
        source.QueueEvent(MakeFrn(20, 1), DataExtendReason | CloseReason);
        source.AdvanceUsn(300);
        var fallback = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(fallback.UsedUsn, Is.False);
        Assert.That(fallback.Root.TotalSize, Is.EqualTo(256));
    }

    [Test]
    public void TryRefresh_EarlyDeltaLimitAbortStopsReadDeltaWithDeltaLimitExceeded()
    {
        var image = BuildSimpleImage();
        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 1, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var reachable = new ulong[(table.Entries.Length + 63) >> 6];
        MftLinks.SetReachable(reachable, MftLayout.RootRecord);
        var capture = new MftUsnMaterializeCapture(table.Entries.Length);
        var root = MftScanner.Materialize(table, links, MftLayout.RootRecord, new DirectoryInfo(@"T:\"), new MftSubtreeTotals(), reachable, null, CancellationToken.None, capture);

        var directSource = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var index = new MftUsnVolumeIndex(root.Store!, new MftUsnJournalInfo(100UL, 0, 100, 0, 1u), table, capture, maxDeltaRecords: 2);

        for (var i = 0; i < 30; i++)
        {
            directSource.QueueEvent(MakeFrn(30 + i, 1), FileCreateReason | CloseReason);
        }

        directSource.AdvanceUsn(500);
        var ok = index.TryRefresh(directSource, out var summary, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.False);
            Assert.That(summary.ErrorKind, Is.EqualTo(MftUsnErrorKind.DeltaLimitExceeded));
            Assert.That(index.CursorUsn, Is.EqualTo(100));
            Assert.That(directSource.PendingEventCount, Is.EqualTo(13));
        }

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source, maxDeltaRecords: 2);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        for (var i = 0; i < 30; i++)
        {
            source.QueueEvent(MakeFrn(30 + i, 1), FileCreateReason | CloseReason);
        }

        source.AdvanceUsn(500);
        var result = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.UsedUsn, Is.False);
            Assert.That(source.ReadDeltaErrors, Does.Contain(MftUsnErrorKind.DeltaLimitExceeded));
        }
    }

    [Test]
    public void TryRefresh_PreservesBaseReadModeOnUsnCacheHit()
    {
        var image = BuildSimpleImage();
        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);

        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null, MftReadMode.Volume, null);
        Assert.That(cold.ReadMode, Is.EqualTo(MftReadMode.Volume));

        source.AdvanceUsn(200);
        var warm = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null, MftReadMode.RawDisk, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(warm.UsedUsn, Is.True);
            Assert.That(warm.ReadMode, Is.EqualTo(MftReadMode.Volume));
        }
    }

    [Test]
    public void TryRefresh_TracksLiveExtraNamesAndExtraNameBytesAcrossHardlinkMutations()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Links"))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        Assert.That(cold.ExtraNames, Is.Zero);
        Assert.That(cold.ExtraNameBytes, Is.Zero);

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "a.bin",
                Alternates = [new MftUsnNameEntry(20, 1, "b.bin", 1), new MftUsnNameEntry(20, 1, "c.bin", 1)],
                Size = 1_000,
                SizeKnown = true,
                TotalNameCount = 3,
            });
        source.QueueEvent(MakeFrn(30, 1), FileCreateReason | HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(200);

        var afterAdd = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterAdd.ExtraNames, Is.EqualTo(2));
            Assert.That(afterAdd.ExtraNameBytes, Is.EqualTo(2_000));
        }

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "a.bin",
                Alternates = [new MftUsnNameEntry(20, 1, "b.bin", 1)],
                Size = 2_500,
                SizeKnown = true,
                TotalNameCount = 2,
            });
        source.QueueEvent(MakeFrn(30, 1), HardlinkChangeReason | DataExtendReason | CloseReason);
        source.AdvanceUsn(300);

        var afterResizeAndUnlink = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterResizeAndUnlink.ExtraNames, Is.EqualTo(1));
            Assert.That(afterResizeAndUnlink.ExtraNameBytes, Is.EqualTo(2_500));
        }

        source.RemoveRecord(30);
        source.QueueEvent(MakeFrn(30, 1), FileDeleteReason | CloseReason);
        source.AdvanceUsn(400);

        var afterDelete = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterDelete.ExtraNames, Is.Zero);
            Assert.That(afterDelete.ExtraNameBytes, Is.Zero);
        }
    }

    [Test]
    public void TryRefresh_RecyclesDetachedDirectorySlotsWhenNoLiveFacadeExists()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Anchor"))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);
        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var store = cold.Root.Store!;
        Assert.That(store.DirectoryCount, Is.EqualTo(2));

        source.SetRecord(
            30,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = MftLayout.RootRecord,
                PrimaryParentSequence = 1,
                PrimaryName = "TempDir",
            });
        source.QueueEvent(MakeFrn(30, 1), FileCreateReason | CloseReason);
        source.AdvanceUsn(200);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(store.DirectoryCount, Is.EqualTo(3));

        source.RemoveRecord(30);
        source.QueueEvent(MakeFrn(30, 1), FileDeleteReason | CloseReason);
        source.AdvanceUsn(300);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        source.SetRecord(
            31,
            new MftUsnRecord
            {
                Exists = true,
                IsDirectory = true,
                Sequence = 1,
                PrimaryParent = MftLayout.RootRecord,
                PrimaryParentSequence = 1,
                PrimaryName = "ReusedDir",
            });
        source.QueueEvent(MakeFrn(31, 1), FileCreateReason | CloseReason);
        source.AdvanceUsn(400);
        var afterReuse = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(store.DirectoryCount, Is.EqualTo(3));
            Assert.That(afterReuse.Root.TotalDirectoryCount, Is.EqualTo(2));
            Assert.That(afterReuse.Root.SubDirectories.Select(static x => x.Name), Is.EquivalentTo(new[] { "Anchor", "ReusedDir" }));
        }
    }

    [Test]
    public void TryRefresh_SubfolderScanFromUsnCache_RehomesAlternativeHardlinksInsideSubfolder()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Other"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Sub"))
            .Record(22, new MftRecordBuilder().AsDirectory().FileName(21, 1, "Nested"))
            .Record(
                30,
                new MftRecordBuilder()
                    .FileName(20, 1, "primary.bin")
                    .FileName(22, 1, "rehomed.bin")
                    .NonResidentData(4096))
            .Build(64);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);

        var cold = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(cold.UsedUsn, Is.False);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        var subCached = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Sub"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(subCached.UsedUsn, Is.True);
            Assert.That(subCached.Root.AbsolutePath, Is.EqualTo(@"T:\Sub"));
            Assert.That(subCached.Root.TotalSize, Is.EqualTo(4096));
            Assert.That(subCached.Root.TotalFileCount, Is.EqualTo(1));
            Assert.That(subCached.Root.TotalDirectoryCount, Is.EqualTo(1));
            Assert.That(subCached.Root.SubDirectories.Single().Files.Single().Name, Is.EqualTo("rehomed.bin"));
            Assert.That(subCached.ExtraNames, Is.EqualTo(1));
            Assert.That(subCached.ExtraNameBytes, Is.EqualTo(4096));
            Assert.That(cold.Root.SubDirectories.Single(static x => x.Name == "Other").Files.Single().Name, Is.EqualTo("primary.bin"));
        }

        source.SetRecord(
            31,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "other2.bin",
                Alternates = [new MftUsnNameEntry(21, 1, "sub2.bin", 1)],
                Size = 1024,
                SizeKnown = true,
                TotalNameCount = 2,
            });
        source.QueueEvent(MakeFrn(31, 1), FileCreateReason | HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(200);

        var subAfterAdd = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Sub"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(subAfterAdd.UsedUsn, Is.True);
            Assert.That(subAfterAdd.Root.TotalSize, Is.EqualTo(5120));
            Assert.That(subAfterAdd.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(subAfterAdd.Root.Files.Single().Name, Is.EqualTo("sub2.bin"));
            Assert.That(subAfterAdd.ExtraNames, Is.EqualTo(2));
            Assert.That(subAfterAdd.ExtraNameBytes, Is.EqualTo(5120));
        }

        source.SetRecord(
            31,
            new MftUsnRecord
            {
                Exists = true,
                Sequence = 1,
                PrimaryParent = 20,
                PrimaryParentSequence = 1,
                PrimaryName = "other2.bin",
                Size = 1024,
                SizeKnown = true,
                TotalNameCount = 1,
            });
        source.QueueEvent(MakeFrn(31, 1), HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(300);

        var subAfterUnlink = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Sub"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(subAfterUnlink.UsedUsn, Is.True);
            Assert.That(subAfterUnlink.Root.TotalSize, Is.EqualTo(4096));
            Assert.That(subAfterUnlink.Root.TotalFileCount, Is.EqualTo(1));
            Assert.That(subAfterUnlink.Root.Files, Is.Empty);
            Assert.That(subAfterUnlink.ExtraNames, Is.EqualTo(1));
            Assert.That(subAfterUnlink.ExtraNameBytes, Is.EqualTo(4096));
        }
    }

    [TestCase("damaged")]
    [TestCase("partial")]
    [TestCase("nameless")]
    [TestCase("detached")]
    [TestCase("unknownSize")]
    public void TryRefresh_IncompleteMftScan_IsNotCachedInUsnCache(string mode)
    {
        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "good.bin").ResidentData(100));

        switch (mode)
        {
            case "damaged":
                builder.Record(21, new MftRecordBuilder().WithoutSignature().FileName(MftLayout.RootRecord, 1, "bad.bin"));
                break;
            case "partial":
                builder.Record(21, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "part.bin").BrokenAttribute());
                break;
            case "nameless":
                builder.Record(21, new MftRecordBuilder().ResidentData(50));
                break;
            case "detached":
                builder.Record(21, new MftRecordBuilder().FileName(45, 1, "orphan.bin").ResidentData(50));
                break;
            case "unknownSize":
                builder.Record(21, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "nosize.bin"));
                break;
        }

        var image = builder.Build(64);
        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var scanner = CreateScanner(source);

        var first = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        var second = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsComplete, Is.False);
            Assert.That(first.UsedUsn, Is.False);
            Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.False);
            Assert.That(second.UsedUsn, Is.False);
            Assert.That(second.IsComplete, Is.False);
        }
    }

    [Test]
    public void TryRefresh_NonV2DeltaAndIncompleteDeltaRecords_RejectWithoutMutatingTreeOrCursor()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Data"))
            .Record(21, new MftRecordBuilder().FileName(20, 1, "keep.bin").NonResidentData(8192))
            .Build(64);

        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 1, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var reachable = new ulong[(table.Entries.Length + 63) >> 6];
        MftLinks.SetReachable(reachable, MftLayout.RootRecord);
        var capture = new MftUsnMaterializeCapture(table.Entries.Length);
        var root = MftScanner.Materialize(table, links, MftLayout.RootRecord, new DirectoryInfo(@"T:\"), new MftSubtreeTotals(), reachable, null, CancellationToken.None, capture);

        var source = new FakeUsnSource(journalId: 100UL, serial: 1u, firstUsn: 0, nextUsn: 100);
        var index = new MftUsnVolumeIndex(root.Store!, new MftUsnJournalInfo(100UL, 0, 100, 0, 1u), table, capture);

        source.ReadError = (MftUsnErrorKind.NotSupported, 0);
        source.AdvanceUsn(200);
        var nonV2Ok = index.TryRefresh(source, out var nonV2Summary, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nonV2Ok, Is.False);
            Assert.That(nonV2Summary.ErrorKind, Is.EqualTo(MftUsnErrorKind.NotSupported));
            Assert.That(index.CursorUsn, Is.EqualTo(100));
            Assert.That(root.TotalSize, Is.EqualTo(8192));
            Assert.That(root.TotalFileCount, Is.EqualTo(1));
        }

        var nonResidentAttrListRaw = new MftRecordBuilder()
            .WithSequence(1)
            .AttributeListRuns(64, [0x11, 0x01, 0x05, 0x00])
            .FileName(20, 1, "keep.bin")
            .Build();
        MftLayout.ApplyFixup(nonResidentAttrListRaw, 512);
        var wrapped = new byte[12 + nonResidentAttrListRaw.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(wrapped, MakeFrn(21, 1));
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(wrapped.AsSpan(8), nonResidentAttrListRaw.Length);
        nonResidentAttrListRaw.CopyTo(wrapped.AsSpan(12));
        var parsedNonResident = MftUsnNativeSource.ParseFileRecordBuffer(wrapped, 21);

        source.SetRecord(21, parsedNonResident);
        source.QueueEvent(MakeFrn(21, 1), HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(300);

        var incompleteOk = index.TryRefresh(source, out var incompleteSummary, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(incompleteOk, Is.False);
            Assert.That(incompleteSummary.ErrorKind, Is.EqualTo(MftUsnErrorKind.IoError));
            Assert.That(index.CursorUsn, Is.EqualTo(100));
            Assert.That(root.TotalSize, Is.EqualTo(8192));
            Assert.That(root.TotalFileCount, Is.EqualTo(1));
            Assert.That(root.SubDirectories.Single().Files.Single().Name, Is.EqualTo("keep.bin"));
        }

        var scanner = CreateScanner(source);
        _ = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);
        Assert.That(scanner.HasUsnCacheFor(@"T:\"), Is.True);

        source.SetRecord(21, parsedNonResident);
        source.QueueEvent(MakeFrn(21, 1), HardlinkChangeReason | CloseReason);
        source.AdvanceUsn(400);

        var fallback = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 2, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fallback.UsedUsn, Is.False);
            Assert.That(fallback.Root.TotalSize, Is.EqualTo(8192));
            Assert.That(fallback.Root.TotalFileCount, Is.EqualTo(1));
        }
    }

    private static MftScanner CreateScanner(FakeUsnSource source, int maxDeltaRecords = MftUsnVolumeIndex.DefaultMaxDeltaRecords)
    {
        return new(
            null,
            (char _, out IMftUsnSource? opened, out MftUsnErrorKind error, out int win32) =>
            {
                opened = source;
                error = MftUsnErrorKind.None;
                win32 = 0;
                return true;
            },
            maxDeltaRecords);
    }

    private static byte[] BuildSimpleImage()
    {
        return new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "one.bin").ResidentData(123))
            .Build(64);
    }

    private static ulong MakeFrn(int recordNumber, ushort sequence)
    {
        return ((ulong)sequence << MftLayout.ReferenceMaskBits) | (uint)recordNumber;
    }

    private sealed class FakeUsnSource(ulong journalId, uint serial, long firstUsn, long nextUsn) : IMftUsnSource
    {
        private readonly Dictionary<int, MftUsnRecord> _records = [];
        private readonly Dictionary<int, (MftUsnErrorKind Error, int Win32)> _fetchErrors = [];
        private readonly Dictionary<ulong, long> _liveSizes = [];
        private readonly List<MftUsnEvent> _pendingEvents = [];

        public List<(ulong JournalId, long StartUsn)> ReadDeltaCalls { get; } = [];

        public List<MftUsnErrorKind> ReadDeltaErrors { get; } = [];

        public int PendingEventCount => _pendingEvents.Count;

        public ulong JournalId { get; set; } = journalId;

        public uint VolumeSerial { get; set; } = serial;

        public long FirstUsn { get; set; } = firstUsn;

        public long NextUsn { get; private set; } = nextUsn;

        public (MftUsnErrorKind Error, int Win32)? ReadError { get; set; }

        public void SetRecord(int rec, MftUsnRecord record)
        {
            _records[rec] = record;
            _fetchErrors.Remove(rec);
        }

        public void RemoveRecord(int rec)
        {
            _records.Remove(rec);
            _fetchErrors.Remove(rec);
        }

        public void SetFetchError(int rec, MftUsnErrorKind error, int win32)
        {
            _fetchErrors[rec] = (error, win32);
        }

        public void SetLiveSize(ulong frn, long size)
        {
            _liveSizes[frn] = size;
        }

        public void RemoveLiveSize(ulong frn)
        {
            _liveSizes.Remove(frn);
        }

        public void QueueEvent(ulong frn, uint reason)
        {
            _pendingEvents.Add(new(frn, reason));
        }

        public void AdvanceUsn(long newNextUsn)
        {
            NextUsn = newNextUsn;
        }

        public bool TryQueryJournal(out MftUsnJournalInfo info, out MftUsnErrorKind error, out int win32Error)
        {
            info = new(JournalId, FirstUsn, NextUsn, FirstUsn, VolumeSerial);
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryReadDelta(
            ulong journalId,
            long startUsn,
            int maxEvents,
            List<MftUsnEvent> destination,
            out long nextUsn,
            out MftUsnErrorKind error,
            out int win32Error,
            CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            ReadDeltaCalls.Add((journalId, startUsn));

            if (ReadError is { } err)
            {
                ReadError = null;
                nextUsn = startUsn;
                error = err.Error;
                win32Error = err.Win32;
                ReadDeltaErrors.Add(error);
                return false;
            }

            if (journalId != JournalId)
            {
                nextUsn = startUsn;
                error = MftUsnErrorKind.InvalidParameter;
                win32Error = 87;
                ReadDeltaErrors.Add(error);
                return false;
            }

            while (_pendingEvents.Count > 0)
            {
                destination.Add(_pendingEvents[0]);
                _pendingEvents.RemoveAt(0);

                if (maxEvents > 0 && destination.Count > maxEvents)
                {
                    nextUsn = startUsn;
                    error = MftUsnErrorKind.DeltaLimitExceeded;
                    win32Error = 0;
                    ReadDeltaErrors.Add(error);
                    return false;
                }
            }

            nextUsn = NextUsn;
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryFetchFileRecord(int recordNumber, out MftUsnRecord record, out MftUsnErrorKind error, out int win32Error)
        {
            if (_fetchErrors.TryGetValue(recordNumber, out var injected))
            {
                record = default;
                error = injected.Error;
                win32Error = injected.Win32;
                return false;
            }

            record = _records.TryGetValue(recordNumber, out var rec) ? rec : default;
            error = MftUsnErrorKind.None;
            win32Error = 0;
            return true;
        }

        public bool TryQueryLiveFileSize(ulong frn, out long size)
        {
            return _liveSizes.TryGetValue(frn, out size);
        }

        public void Dispose()
        {
        }
    }
}
