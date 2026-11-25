using SpaceSnoop.Core;
using SpaceSnoop.Core.Mft;
using System.Buffers.Binary;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftRawDiskTests
{
    private const int Records = 32;
    private const char Letter = 'T';

    [Test]
    public void MftReadMode_HasExpectedValues()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That((int)MftReadMode.None, Is.Zero);
            Assert.That((int)MftReadMode.Volume, Is.EqualTo(1));
            Assert.That((int)MftReadMode.RawDisk, Is.EqualTo(2));
        }
    }

    [TestCase(0u, 32, 0L, 4096L, false, TestName = "ValidateExtents_RejectsZeroExtents")]
    [TestCase(2u, 32, 0L, 4096L, false, TestName = "ValidateExtents_RejectsMultipleExtents")]
    [TestCase(1u, 31, 0L, 4096L, false, TestName = "ValidateExtents_RejectsShortBuffer")]
    [TestCase(1u, 32, -512L, 4096L, false, TestName = "ValidateExtents_RejectsNegativeOffset")]
    [TestCase(1u, 32, 0L, 0L, false, TestName = "ValidateExtents_RejectsZeroLength")]
    [TestCase(1u, 32, 256L, 4096L, false, TestName = "ValidateExtents_RejectsUnalignedOffset")]
    [TestCase(1u, 32, 512L, 4097L, false, TestName = "ValidateExtents_RejectsUnalignedLength")]
    [TestCase(1u, 32, 1048576L, 67108864L, true, TestName = "ValidateExtents_AcceptsSingleAlignedExtent")]
    public void ValidateExtents_EnforcesAllBounds(
        uint extentCount,
        int bytesReturned,
        long startingOffset,
        long extentLength,
        bool expected)
    {
        var buffer = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, extentCount);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(8), 3u);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(16), startingOffset);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(24), extentLength);

        var ok = MftRawDiskGuard.ValidateExtents(buffer, bytesReturned, 512, out var extent, out var reason);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.EqualTo(expected));

            if (expected)
            {
                Assert.That(reason, Is.Null);
                Assert.That(extent.DiskNumber, Is.EqualTo(3u));
                Assert.That(extent.StartingOffset, Is.EqualTo(startingOffset));
                Assert.That(extent.ExtentLength, Is.EqualTo(extentLength));
            }
            else
            {
                Assert.That(reason, Is.Not.Null.And.Not.Empty);
            }
        }
    }

    [TestCase(71, 512u, 4096u, 1024u, 65536L, 4L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsShortBuffer")]
    [TestCase(96, 1024u, 4096u, 1024u, 65536L, 4L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsSectorMismatch")]
    [TestCase(96, 512u, 8192u, 1024u, 65536L, 4L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsClusterMismatch")]
    [TestCase(96, 512u, 4096u, 4096u, 65536L, 4L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsRecordSizeMismatch")]
    [TestCase(96, 512u, 4096u, 1024u, 65536L, 5L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsMftStartMismatch")]
    [TestCase(96, 512u, 4096u, 1024u, 0L, 4L, 1_048_576L, false, TestName = "ValidateNtfsVolumeData_RejectsNonPositiveValidLength")]
    [TestCase(96, 512u, 4096u, 1024u, 65536L, 4L, 16384L, false, TestName = "ValidateNtfsVolumeData_RejectsMftOutsideExtent")]
    [TestCase(96, 512u, 4096u, 1024u, 65536L, 4L, 4 * 4096L + 512L, false, TestName = "ValidateNtfsVolumeData_RejectsSelfRecordStraddlingExtentEnd")]
    [TestCase(96, 512u, 4096u, 1024u, 65536L, 4L, 1_048_576L, true, TestName = "ValidateNtfsVolumeData_AcceptsConsistentVolume")]
    public void ValidateNtfsVolumeData_EnforcesExactAgreementAndExtentBounds(
        int bytesReturned,
        uint bytesPerSector,
        uint bytesPerCluster,
        uint recordSize,
        long mftValidDataLength,
        long mftStartLcn,
        long extentLength,
        bool expected)
    {
        var boot = new MftBootInfo(512, 4096, 4, 1024);
        var buffer = new byte[96];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x28), bytesPerSector);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x2C), bytesPerCluster);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(0x30), recordSize);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(0x38), mftValidDataLength);
        BinaryPrimitives.WriteInt64LittleEndian(buffer.AsSpan(0x40), mftStartLcn);

        var ok = MftRawDiskGuard.ValidateNtfsVolumeData(buffer, bytesReturned, boot, extentLength, out var reason);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.EqualTo(expected));
            Assert.That(reason is null, Is.EqualTo(expected));
        }
    }

    [Test]
    public void ValidateBootSectorMatch_RejectsEncryptedOrOffsetMismatch()
    {
        var volumeBoot = new byte[512];
        Random.Shared.NextBytes(volumeBoot);
        var matchingBoot = (byte[])volumeBoot.Clone();
        var encryptedBoot = (byte[])volumeBoot.Clone();
        encryptedBoot[3] ^= 0xFF;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(MftRawDiskGuard.ValidateBootSectorMatch(matchingBoot, volumeBoot, out var okReason), Is.True);
            Assert.That(okReason, Is.Null);

            Assert.That(MftRawDiskGuard.ValidateBootSectorMatch(encryptedBoot, volumeBoot, out var diffReason), Is.False);
            Assert.That(diffReason, Is.Not.Null.And.Not.Empty);

            Assert.That(MftRawDiskGuard.ValidateBootSectorMatch(matchingBoot.AsSpan(0, 511), volumeBoot, out var shortReason), Is.False);
            Assert.That(shortReason, Is.Not.Null.And.Not.Empty);
        }
    }

    [TestCase(0L, 10L, 100L, true, TestName = "ValidateRunsWithinPartition_AcceptsExactFit")]
    [TestCase(90L, 10L, 100L, true, TestName = "ValidateRunsWithinPartition_AcceptsTailCluster")]
    [TestCase(-1L, 1L, 100L, false, TestName = "ValidateRunsWithinPartition_RejectsNegativeCluster")]
    [TestCase(5L, 0L, 100L, false, TestName = "ValidateRunsWithinPartition_RejectsZeroCount")]
    [TestCase(95L, 6L, 100L, false, TestName = "ValidateRunsWithinPartition_RejectsPastExtentEnd")]
    [TestCase(long.MaxValue - 2, 10L, 100L, false, TestName = "ValidateRunsWithinPartition_RejectsOverflowingCluster")]
    public void ValidateRunsWithinPartition_RejectsOutOfBoundsAndOverflow(
        long cluster,
        long count,
        long totalClusters,
        bool expected)
    {
        var runs = new MftRun[] { new(4, 2), new(cluster, count) };
        var ok = MftRawDiskGuard.ValidateRunsWithinPartition(runs, totalClusters * 4096L, 4096, out var reason);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.EqualTo(expected));
            Assert.That(reason is null, Is.EqualTo(expected));
        }
    }

    [Test]
    public void MftRawDiskVolume_TranslatesOffsetAndRejectsReadsOutsidePartition()
    {
        const long baseOffset = 8192;
        const long extentLength = 16384;
        var diskImage = new byte[baseOffset + extentLength + 4096];
        Random.Shared.NextBytes(diskImage);

        using var file = new MftImageFile(diskImage);
        using var fileVolume = MftFileVolume.Open(file.Path, "PhysicalDrive9");
        using var rawVolume = new MftRawDiskVolume(fileVolume, baseOffset, extentLength);

        var slice = new byte[1024];
        rawVolume.ReadAt(512, slice);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(slice, Is.EqualTo(diskImage.AsSpan((int)baseOffset + 512, 1024).ToArray()));
            Assert.Throws<InvalidDataException>(() => rawVolume.ReadAt(-1, new byte[512]));
            Assert.Throws<InvalidDataException>(() => rawVolume.ReadAt(extentLength, new byte[1]));
            Assert.Throws<InvalidDataException>(() => rawVolume.ReadAt(extentLength - 512, new byte[1024]));
            Assert.Throws<InvalidDataException>(() => rawVolume.ReadAt(long.MaxValue, new byte[512]));
        }
    }

    [Test]
    public void MftScanner_RawDisk_SucceedsFromOffsetPartitionAndMatchesVolumeTree()
    {
        var partitionImage = BuildSampleImage();
        const long baseOffset = 1_048_576;
        var diskImage = new byte[baseOffset + partitionImage.Length + 8192];
        Random.Shared.NextBytes(diskImage.AsSpan(0, (int)baseOffset));
        partitionImage.CopyTo(diskImage, (int)baseOffset);

        var scanner = new MftScanner();
        var directory = new DirectoryInfo(@"T:\");

        var volumeResult = scanner.Calculate(
            new MftMemoryVolume(partitionImage),
            directory,
            Letter,
            1,
            4096,
            null,
            MftReadMode.None,
            rawDiskOpener: null);

        var rawResult = scanner.Calculate(
            new MftMemoryVolume(partitionImage),
            directory,
            Letter,
            2,
            4096,
            null,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Success(
                new MftRawDiskVolume(new MftMemoryVolume(diskImage), baseOffset, partitionImage.Length),
                partitionImage.Length));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(volumeResult.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(volumeResult.FallbackReason, Is.Null);

            Assert.That(rawResult.ReadMode, Is.EqualTo(MftReadMode.RawDisk));
            Assert.That(rawResult.FallbackReason, Is.Null);
            Assert.That(rawResult.Root.TotalSize, Is.EqualTo(volumeResult.Root.TotalSize));
            Assert.That(rawResult.Root.TotalFileCount, Is.EqualTo(volumeResult.Root.TotalFileCount));
            Assert.That(rawResult.Root.TotalDirectoryCount, Is.EqualTo(volumeResult.Root.TotalDirectoryCount));
            Assert.That(rawResult.IsComplete, Is.True);
        }
    }

    [Test]
    public void MftScanner_RawDisk_FallsBackToVolumeWhenGuardFails()
    {
        var partitionImage = BuildSampleImage();
        var scanner = new MftScanner();
        var directory = new DirectoryInfo(@"T:\");
        var readyCalls = 0;
        var progress = new ScanProgress();
        progress.OnVolumeReady(() => Interlocked.Increment(ref readyCalls));

        var volumeReads = 0;
        var result = scanner.Calculate(
            new MftCountingVolume(partitionImage, _ => Interlocked.Increment(ref volumeReads)),
            directory,
            Letter,
            1,
            4096,
            progress,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Fail("том занимает экстентов: 2 вместо одного"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(result.FallbackReason, Is.EqualTo("том занимает экстентов: 2 вместо одного"));
            Assert.That(result.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(result.Root.TotalSize, Is.EqualTo(1500));
            Assert.That(readyCalls, Is.EqualTo(1));
            Assert.That(volumeReads, Is.GreaterThan(1));
        }
    }

    [TestCase(typeof(UnauthorizedAccessException), TestName = "RawDisk_UnauthorizedAccessException_FallsBackToVolume")]
    [TestCase(typeof(IOException), TestName = "RawDisk_IOException_FallsBackToVolume")]
    [TestCase(typeof(InvalidDataException), TestName = "RawDisk_InvalidDataException_FallsBackToVolume")]
    public void MftScanner_RawDisk_FallsBackToVolumeOnReadFailure(Type exceptionType)
    {
        var partitionImage = BuildSampleImage();
        var scanner = new MftScanner();
        var directory = new DirectoryInfo(@"T:\");
        var readyCalls = 0;
        var progress = new ScanProgress();
        progress.OnVolumeReady(() => Interlocked.Increment(ref readyCalls));

        var rawReads = 0;
        var result = scanner.Calculate(
            new MftMemoryVolume(partitionImage),
            directory,
            Letter,
            1,
            1024,
            progress,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Success(
                new MftCountingVolume(
                    partitionImage,
                    read =>
                    {
                        rawReads = read;

                        if (read == 3)
                        {
                            throw (Exception)Activator.CreateInstance(exceptionType, "ошибка чтения диска")!;
                        }
                    }),
                partitionImage.Length));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(result.FallbackReason, Is.EqualTo("ошибка чтения диска"));
            Assert.That(result.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(result.Root.TotalSize, Is.EqualTo(1500));
            Assert.That(rawReads, Is.EqualTo(3));
            Assert.That(readyCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public void MftScanner_RawDisk_RunlistOutsidePartition_FallsBackBeforeNotifyVolumeReady()
    {
        var partitionImage = BuildSampleImage();
        var scanner = new MftScanner();
        var directory = new DirectoryInfo(@"T:\");
        var readyCalls = 0;
        var readyAtVolumeFirstRead = -1;
        var progress = new ScanProgress();
        progress.OnVolumeReady(() => Interlocked.Increment(ref readyCalls));

        var result = scanner.Calculate(
            new MftCountingVolume(
                partitionImage,
                read =>
                {
                    if (read == 1)
                    {
                        readyAtVolumeFirstRead = Volatile.Read(ref readyCalls);
                    }
                }),
            directory,
            Letter,
            1,
            4096,
            progress,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Success(
                new MftMemoryVolume(partitionImage),
                extentLength: 4 * 4096L + 2048L));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readyAtVolumeFirstRead, Is.Zero);
            Assert.That(readyCalls, Is.EqualTo(1));
            Assert.That(result.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(result.FallbackReason, Does.Contain("выходит за границы раздела"));
            Assert.That(result.Root.TotalSize, Is.EqualTo(1500));
        }
    }

    [TestCase(false, TestName = "RawDisk_DamagedRecord_FallsBackToVolume")]
    [TestCase(true, TestName = "RawDisk_PartialRecord_FallsBackToVolume")]
    public void MftScanner_RawDisk_FallsBackToVolumeWhenDamagedOrPartialRecordsObserved(bool partial)
    {
        var cleanImage = BuildSampleImage();
        var badRecord = partial
            ? new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "неполный.bin").BrokenAttribute()
            : new MftRecordBuilder().WithoutSignature().FileName(MftLayout.RootRecord, 1, "битый.bin").NonResidentData(1000);

        var damagedRawImage = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "отчёт.bin").ResidentData(500))
            .Record(21, badRecord)
            .Build(Records);

        var scanner = new MftScanner();
        var result = scanner.Calculate(
            new MftMemoryVolume(cleanImage),
            new DirectoryInfo(@"T:\"),
            Letter,
            1,
            4096,
            null,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Success(new MftMemoryVolume(damagedRawImage), damagedRawImage.Length));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(result.FallbackReason, Does.Contain(partial ? "неполных 1" : "повреждено 1"));
            Assert.That(result.DamagedRecords, Is.Zero);
            Assert.That(result.PartialRecords, Is.Zero);
            Assert.That(result.Root.TotalFileCount, Is.EqualTo(2));
            Assert.That(result.Root.TotalSize, Is.EqualTo(1500));
        }
    }

    [Test]
    public void MftScanner_RawDisk_DoesNotSwallowCancellation()
    {
        var partitionImage = BuildSampleImage();
        var scanner = new MftScanner();
        using var cancel = new CancellationTokenSource();

        Assert.Catch<OperationCanceledException>(() =>
            scanner.Calculate(
                new MftMemoryVolume(partitionImage),
                new DirectoryInfo(@"T:\"),
                Letter,
                1,
                1024,
                null,
                MftReadMode.RawDisk,
                _ => MftRawDiskAttempt.Success(
                    new MftCountingVolume(
                        partitionImage,
                        read =>
                        {
                            if (read == 3)
                            {
                                cancel.Cancel();
                            }
                        }),
                    partitionImage.Length),
                cancel.Token));
    }

    [Test]
    public void MftScanner_TryCalculate_NetworkPathReturnsNotFixedVolume()
    {
        var scanner = new MftScanner();

        var result = scanner.TryCalculate(new DirectoryInfo(@"\\server\share\folder"), null, out var availability, MftReadMode.RawDisk);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.Null);
            Assert.That(availability, Is.EqualTo(MftAvailability.NotFixedVolume));
        }
    }

    [Test]
    public void MftReader_Read_WithPrefetchedBootDoesNotReReadOffsetZero()
    {
        var image = BuildSampleImage();
        var boot = image.AsSpan(0, 512).ToArray();
        var readZero = false;
        var volume = new MftOffsetCheckingVolume(
            image,
            offset =>
            {
                if (offset == 0)
                {
                    readZero = true;
                }
            });

        var table = MftReader.Read(volume, Letter, 1, 4096, null, boot, partitionExtentLength: null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(readZero, Is.False);
            Assert.That(table.Entries, Has.Length.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void MftScanner_RawDisk_FallsBackToVolumeWhenRawMftShorterThanNtfsValidDataLength()
    {
        var image = BuildSampleImage();
        var scanner = new MftScanner();
        var expectedMftBytes = (long)Records * 1024;

        var result = scanner.Calculate(
            new MftMemoryVolume(image),
            new DirectoryInfo(@"T:\"),
            Letter,
            1,
            4096,
            null,
            MftReadMode.RawDisk,
            _ => MftRawDiskAttempt.Success(
                new MftMemoryVolume(image),
                image.Length,
                mftValidDataLength: expectedMftBytes + 4096));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.ReadMode, Is.EqualTo(MftReadMode.Volume));
            Assert.That(result.FallbackReason, Does.Contain("меньше длины NTFS"));
            Assert.That(result.Root.TotalSize, Is.EqualTo(1500));
            Assert.That(result.IsComplete, Is.True);
        }
    }

    [Test]
    public void MftScanner_SubdirectoryScan_UsesCompactStoreAndDeterministicCaseInsensitiveLocate()
    {
        var image = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Alpha"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "alpha"))
            .Record(22, new MftRecordBuilder().FileName(20, 1, "first.bin").ResidentData(111))
            .Record(23, new MftRecordBuilder().FileName(21, 1, "second.bin").ResidentData(222))
            .Build(Records);

        var table = MftReader.Read(new MftMemoryVolume(image), Letter, 1, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var located = MftScanner.Locate(table, links, new DirectoryInfo(@"T:\ALPHA"), Letter);
        var totals = new MftSubtreeTotals();
        var subRoot = MftScanner.Materialize(table, links, 20, new DirectoryInfo(@"T:\Alpha"), totals, reachable: null, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(located, Is.EqualTo(20));
            Assert.That(subRoot.Store, Is.Not.Null);
            Assert.That(subRoot.Store, Is.Not.SameAs(table.Store));
            Assert.That(subRoot.TotalSize, Is.EqualTo(111));
            Assert.That(subRoot.Files.Single().Name, Is.EqualTo("first.bin"));
        }
    }

    private static byte[] BuildSampleImage()
    {
        return new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "отчёт.bin").ResidentData(500))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Папка"))
            .Record(22, new MftRecordBuilder().FileName(21, 1, "данные.dat").NonResidentData(1000))
            .Build(Records);
    }

    private sealed class MftOffsetCheckingVolume(byte[] image, Action<long> onOffset) : IMftVolume
    {
        private readonly MftMemoryVolume _inner = new(image);

        public void ReadAt(long offset, Span<byte> buffer)
        {
            onOffset(offset);
            _inner.ReadAt(offset, buffer);
        }

        public void Dispose()
        {
        }
    }
}
