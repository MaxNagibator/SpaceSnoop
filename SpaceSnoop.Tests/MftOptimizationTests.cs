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

    [Test]
    public void MftScanner_AdaptiveLevel2Split_LargeSubtreeMatchesTotalsAndTopLevelProgress()
    {
        const int level3Count = 72;
        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Windows"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Users"))
            .Record(22, new MftRecordBuilder().AsDirectory().FileName(20, 1, "WinSxS"))
            .Record(23, new MftRecordBuilder().AsDirectory().FileName(20, 1, "System32"))
            .Record(24, new MftRecordBuilder().FileName(22, 1, "manifest.bin").ResidentData(111))
            .Record(25, new MftRecordBuilder().FileName(23, 1, "kernel.dll").ResidentData(222))
            .Record(26, new MftRecordBuilder().FileName(21, 1, "user.dat").ResidentData(333));

        var expectedWinSxSSize = 111L;
        var expectedWinSxSFiles = 1;
        var expectedWinSxSDirs = level3Count;
        var nextRecord = 30;

        for (var i = 0; i < level3Count; i++)
        {
            var l3Dir = nextRecord++;
            var l3File = nextRecord++;
            var l3Size = 100 + i;

            builder
                .Record(l3Dir, new MftRecordBuilder().AsDirectory().FileName(22, 1, $"pkg_{i:D3}"))
                .Record(l3File, new MftRecordBuilder().FileName(l3Dir, 1, $"f_{i:D3}.dll").ResidentData(l3Size));

            expectedWinSxSSize += l3Size;
            expectedWinSxSFiles++;

            if (i % 10 == 0)
            {
                var l4Dir = nextRecord++;
                var l4File = nextRecord++;
                var l4Size = 50 + i;

                builder
                    .Record(l4Dir, new MftRecordBuilder().AsDirectory().FileName(l3Dir, 1, "nested"))
                    .Record(l4File, new MftRecordBuilder().FileName(l4Dir, 1, "n.bin").ResidentData(l4Size));

                expectedWinSxSSize += l4Size;
                expectedWinSxSFiles++;
                expectedWinSxSDirs++;
            }
        }

        var image = builder.Build(nextRecord + 8);
        var scanner = new MftScanner();
        var rootProgress = new ScanProgress();
        var rootResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 4, 4096, rootProgress);
        var rootSnapshot = rootProgress.CreateSnapshot();

        var subProgress = new ScanProgress();
        var subResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Windows"), 'T', 4, 4096, subProgress);
        var subSnapshot = subProgress.CreateSnapshot();

        var windowsNode = rootResult.Root.SubDirectories.Single(static d => d.Name == "Windows");
        var winSxSNode = windowsNode.SubDirectories.Single(static d => d.Name == "WinSxS");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(winSxSNode.TotalSize, Is.EqualTo(expectedWinSxSSize));
            Assert.That(winSxSNode.TotalFileCount, Is.EqualTo(expectedWinSxSFiles));
            Assert.That(winSxSNode.TotalDirectoryCount, Is.EqualTo(expectedWinSxSDirs));

            Assert.That(windowsNode.TotalSize, Is.EqualTo(expectedWinSxSSize + 222));
            Assert.That(windowsNode.TotalFileCount, Is.EqualTo(expectedWinSxSFiles + 1));
            Assert.That(windowsNode.TotalDirectoryCount, Is.EqualTo(expectedWinSxSDirs + 2));

            Assert.That(rootResult.Root.TotalSize, Is.EqualTo(expectedWinSxSSize + 222 + 333));
            Assert.That(rootResult.Root.TotalFileCount, Is.EqualTo(expectedWinSxSFiles + 2));
            Assert.That(rootResult.Root.TotalDirectoryCount, Is.EqualTo(expectedWinSxSDirs + 4));

            Assert.That(rootSnapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(rootSnapshot.TopLevelCompleted, Is.EqualTo(2));
            Assert.That(rootSnapshot.DirectoriesScanned, Is.EqualTo(rootResult.Root.TotalDirectoryCount + 1));
            Assert.That(rootSnapshot.FilesScanned, Is.EqualTo(rootResult.Root.TotalFileCount));
            Assert.That(rootSnapshot.BytesScanned, Is.EqualTo(rootResult.Root.TotalSize));

            Assert.That(subResult.Root.TotalSize, Is.EqualTo(windowsNode.TotalSize));
            Assert.That(subResult.Root.TotalFileCount, Is.EqualTo(windowsNode.TotalFileCount));
            Assert.That(subResult.Root.TotalDirectoryCount, Is.EqualTo(windowsNode.TotalDirectoryCount));
            Assert.That(subSnapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(subSnapshot.TopLevelCompleted, Is.EqualTo(2));
        }
    }

    [Test]
    public void MftReader_SingleThreadDoubleBuffered_ReadsAllBlocksAndAwaitsInflightOnCancelAndError()
    {
        const int records = 40;
        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."));

        for (var i = MftLayout.FirstUserRecord; i < records; i++)
        {
            builder.Record(i, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, $"item_{i}.bin").ResidentData(i * 10));
        }

        var image = builder.Build(records);
        var asyncReads = 0;
        var okVolume = new AsyncTestMftVolume(
            image,
            async (_, _, _, _) =>
            {
                Interlocked.Increment(ref asyncReads);
                await Task.Yield();
            });

        var tableSingle = MftReader.Read(okVolume, 'T', threads: 1, blockBytes: 4096, progress: null, CancellationToken.None);
        var tableMulti = MftReader.Read(new MftMemoryVolume(image), 'T', threads: 4, blockBytes: 4096, progress: null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(asyncReads, Is.EqualTo(10));
            Assert.That(tableSingle.Entries, Has.Length.EqualTo(tableMulti.Entries.Length));

            for (var i = MftLayout.FirstUserRecord; i < records; i++)
            {
                Assert.That(tableSingle.GetName(i), Is.EqualTo(tableMulti.GetName(i)));
                Assert.That(tableSingle.Entries[i].Size, Is.EqualTo(tableMulti.Entries[i].Size));
            }
        }

        using var cancel = new CancellationTokenSource();
        var secondReadStarted = false;
        var secondReadDrained = false;
        var cancelVolume = new AsyncTestMftVolume(
            image,
            async (callIndex, _, _, token) =>
            {
                if (callIndex == 2)
                {
                    secondReadStarted = true;
                    var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    using var registration = token.Register(() => _ = Task.Run(async () =>
                    {
                        await Task.Delay(15).ConfigureAwait(false);
                        secondReadDrained = true;
                        tcs.TrySetResult();
                    }));
                    cancel.Cancel();
                    await tcs.Task.ConfigureAwait(false);
                }
            });

        Assert.Catch<OperationCanceledException>(() => MftReader.Read(cancelVolume, 'T', threads: 1, blockBytes: 4096, null, cancel.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondReadStarted, Is.True);
            Assert.That(secondReadDrained, Is.True);
        }

        var errorVolume = new AsyncTestMftVolume(
            image,
            async (callIndex, _, _, _) =>
            {
                await Task.Yield();

                if (callIndex == 3)
                {
                    throw new IOException("ошибка асинхронного чтения");
                }
            });

        Assert.Throws<IOException>(() => MftReader.Read(errorVolume, 'T', threads: 1, blockBytes: 4096, null, CancellationToken.None));
    }

    [Test]
    public void MftLinks_ReachableBitset_HandlesBoundaryIndicesAndConcurrentBitsInSameWord()
    {
        const int count = 130;
        var reachable = new ulong[(count + 63) >> 6];
        int[] boundaries = [0, 1, 5, 62, 63, 64, 65, 127, 128, 129];

        foreach (var index in boundaries)
        {
            Assert.That(MftLinks.IsReachable(reachable, index), Is.False);
            MftLinks.SetReachable(reachable, index);
            Assert.That(MftLinks.IsReachable(reachable, index), Is.True);
        }

        Assert.That(MftLinks.IsReachable(reachable, 2), Is.False);
        Assert.That(MftLinks.IsReachable(reachable, 61), Is.False);
        Assert.That(MftLinks.IsReachable(reachable, 66), Is.False);

        var concurrent = new ulong[(count + 63) >> 6];
        var wins = new int[128];

        Parallel.For(
            0,
            8,
            _ =>
            {
                for (var bit = 0; bit < 128; bit++)
                {
                    if (MftLinks.TryMarkReachable(concurrent, bit))
                    {
                        Interlocked.Increment(ref wins[bit]);
                    }
                }
            });

        using (Assert.EnterMultipleScope())
        {
            for (var bit = 0; bit < 128; bit++)
            {
                Assert.That(wins[bit], Is.EqualTo(1));
                Assert.That(MftLinks.IsReachable(concurrent, bit), Is.True);
            }

            Assert.That(MftLinks.IsReachable(concurrent, 128), Is.False);
        }

        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(63, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "b63.bin").ResidentData(10))
            .Record(64, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "b64.bin").ResidentData(20))
            .Record(65, new MftRecordBuilder().FileName(999, 1, "orphan65.bin").ResidentData(30))
            .Record(128, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "b128.bin").ResidentData(40));

        var result = new MftScanner().Calculate(new MftMemoryVolume(builder.Build(136)), new DirectoryInfo(@"T:\"), 'T', 4, 4096, null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.TotalSize, Is.EqualTo(70));
            Assert.That(result.Root.TotalFileCount, Is.EqualTo(3));
            Assert.That(result.DetachedRecords, Is.EqualTo(1));
            Assert.That(result.OrphanBytes, Is.EqualTo(30));
        }
    }

    [Test]
    public void MftFileVolume_SectorAlignedReads_AvoidRepeatedBounceAllocationsAndSupportSmallClusters()
    {
        var image = new MftVolumeBuilder(sectorsPerCluster: 1)
            .Fragment(33)
            .Fragment(31)
            .SplitSelf(1, 15, listOutside: false)
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "малый-кластер.bin").ResidentData(42))
            .Record(31, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "хвост.bin").ResidentData(84))
            .Build(32);

        using var file = new MftImageFile(image);
        using var volume = MftFileVolume.Open(file.Path, "T:");

        var table = MftReader.Read(volume, 'T', 1, 1024, null, CancellationToken.None);

        var scratch = new byte[16384];
        volume.ReadAt(513, scratch);
        Assert.That(volume.IsBuffered, Is.False);

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < 16; i++)
        {
            volume.ReadAt(513 + (i % 17), scratch);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(volume.IsBuffered, Is.False);
            Assert.That(table.GetName(20), Is.EqualTo("малый-кластер.bin"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(42));
            Assert.That(table.GetName(31), Is.EqualTo("хвост.bin"));
            Assert.That(table.Entries[31].Size, Is.EqualTo(84));
            Assert.That(table.Statistics.Damaged, Is.Zero);
            Assert.That(allocated, Is.LessThan(4096));
        }
    }

    [Test]
    public void MftReader_SinglePassAlternates_SkipsDosAndCollectsHardLinksDeterministically()
    {
        var names = new NameTable(1);
        var pending = new MftPending();
        var entries = new MftEntry[4];

        var dosAndBetterOrder = new MftRecordBuilder()
            .FileName(MftLayout.RootRecord, 1, "PROGRA~1", MftLayout.NamespaceDos)
            .FileName(MftLayout.RootRecord, 1, "CombinedName", space: 3)
            .FileName(20, 2, "PosixPrimary", space: 0)
            .FileName(30, 3, "Win32Link", space: 1)
            .ResidentData(256)
            .Build();

        MftReader.Parse(dosAndBetterOrder, 512, 0, entries, pending, new(), names);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names.GetString(entries[0].NameId), Is.EqualTo("PosixPrimary"));
            Assert.That(entries[0].Parent, Is.EqualTo(20));
            Assert.That(entries[0].ParentSequence, Is.EqualTo(2));
            Assert.That(pending.Alternates[0], Has.Count.EqualTo(2));
            Assert.That(
                pending.Alternates[0].Select(x => names.GetString(x.NameId)),
                Is.EqualTo(new[] { "CombinedName", "Win32Link" }));
        }

        var multiBlockImage = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(
                20,
                new MftRecordBuilder()
                    .FileName(MftLayout.RootRecord, 1, "SHORT~1", MftLayout.NamespaceDos)
                    .FileName(MftLayout.RootRecord, 1, "primary.dll", space: 1)
                    .FileName(MftLayout.RootRecord, 1, "same_record_link.dll", space: 3)
                    .ResidentData(512))
            .Record(24, new MftRecordBuilder().ExtensionOf(20).FileName(MftLayout.RootRecord, 1, "ext1.dll"))
            .Record(28, new MftRecordBuilder().ExtensionOf(20).FileName(MftLayout.RootRecord, 1, "ext2.dll"))
            .Build(32);

        for (var run = 0; run < 4; run++)
        {
            var table = MftReader.Read(new MftMemoryVolume(multiBlockImage), 'T', 4, 1024, null, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(table.GetName(20), Is.EqualTo("primary.dll"));
                Assert.That(table.Entries[20].Names, Is.EqualTo(4));
                Assert.That(
                    table.Alternates[20].Select(x => table.Names.GetString(x.NameId)),
                    Is.EqualTo(new[] { "same_record_link.dll", "ext1.dll", "ext2.dll" }));
            }
        }

        var displacedOrder = new MftRecordBuilder()
            .FileName(MftLayout.RootRecord, 1, "first_ns3.txt", space: 3)
            .FileName(MftLayout.RootRecord, 1, "second_ns3.txt", space: 3)
            .FileName(MftLayout.RootRecord, 1, "winner_ns1.txt", space: 1)
            .ResidentData(64)
            .Build();
        var orderPending = new MftPending();
        var orderEntries = new MftEntry[4];
        MftReader.Parse(displacedOrder, 512, 2, orderEntries, orderPending, new MftStatistics(), names);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names.GetString(orderEntries[2].NameId), Is.EqualTo("winner_ns1.txt"));
            Assert.That(
                orderPending.Alternates[2].Select(x => names.GetString(x.NameId)),
                Is.EqualTo(new[] { "first_ns3.txt", "second_ns3.txt" }));
        }
    }

    [Test]
    public void MftScanner_DynamicMaterialize_DeepHeavySubtreeMatchesTotalsAndSupportsCancellation()
    {
        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Sources"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(20, 1, "Org"))
            .Record(22, new MftRecordBuilder().AsDirectory().FileName(21, 1, "Repo"))
            .Record(23, new MftRecordBuilder().AsDirectory().FileName(22, 1, "Deep"))
            .Record(24, new MftRecordBuilder().FileName(20, 1, "root_file.bin").ResidentData(100));

        var expectedSize = 100L;
        var expectedFiles = 1;
        var expectedDirs = 4;
        var nextRecord = 30;

        for (var i = 0; i < 40; i++)
        {
            var l5Dir = nextRecord++;
            builder.Record(l5Dir, new MftRecordBuilder().AsDirectory().FileName(23, 1, $"shard_{i:D2}"));
            expectedDirs++;

            for (var j = 0; j < 6; j++)
            {
                var l6Dir = nextRecord++;
                var l6File = nextRecord++;
                var size = (i + 1) * 10 + j;
                builder
                    .Record(l6Dir, new MftRecordBuilder().AsDirectory().FileName(l5Dir, 1, $"sub_{j}"))
                    .Record(l6File, new MftRecordBuilder().FileName(l6Dir, 1, $"f_{j}.dat").ResidentData(size));
                expectedDirs++;
                expectedFiles++;
                expectedSize += size;
            }
        }

        var image = builder.Build(nextRecord + 8);
        var scanner = new MftScanner();
        var progress = new ScanProgress();
        var result = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 4, 4096, progress);
        var snapshot = progress.CreateSnapshot();

        var sources = result.Root.SubDirectories.Single(static x => x.Name == "Sources");
        var deep = sources.SubDirectories.Single().SubDirectories.Single().SubDirectories.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.TotalSize, Is.EqualTo(expectedSize));
            Assert.That(result.Root.TotalFileCount, Is.EqualTo(expectedFiles));
            Assert.That(result.Root.TotalDirectoryCount, Is.EqualTo(expectedDirs));
            Assert.That(sources.TotalSize, Is.EqualTo(expectedSize));
            Assert.That(deep.TotalSize, Is.EqualTo(expectedSize - 100));
            Assert.That(deep.TotalFileCount, Is.EqualTo(expectedFiles - 1));
            Assert.That(deep.TotalDirectoryCount, Is.EqualTo(expectedDirs - 4));
            Assert.That(snapshot.TopLevelTotal, Is.EqualTo(1));
            Assert.That(snapshot.TopLevelCompleted, Is.EqualTo(1));
            Assert.That(snapshot.DirectoriesScanned, Is.EqualTo(expectedDirs + 1));
            Assert.That(snapshot.FilesScanned, Is.EqualTo(expectedFiles));
            Assert.That(snapshot.BytesScanned, Is.EqualTo(expectedSize));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Catch<OperationCanceledException>(
            () => scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 4, 4096, null, cts.Token));
    }

    [TestCase(500_000L, 5_000L, 600_000)]
    [TestCase(8_400_000L, 67_000L, 8_870_000)]
    [TestCase(50_000_000L, 200_000L, 52_000_000)]
    public void MftScanner_ComputeMaterializeNoGcBudgets_StaysWithinSafeCaps(long inUse, long extensions, int totalRecords)
    {
        MftScanner.ComputeMaterializeNoGcBudgets(inUse, extensions, totalRecords, out var totalBudget, out var lohBudget);
        var sohBudget = totalBudget - lohBudget;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(lohBudget, Is.InRange(48L * 1024 * 1024, 304L * 1024 * 1024));
            Assert.That(sohBudget, Is.InRange(48L * 1024 * 1024, 208L * 1024 * 1024));
            Assert.That(totalBudget, Is.EqualTo(sohBudget + lohBudget));
        }
    }

    private sealed class AsyncTestMftVolume(byte[] image, Func<int, long, int, CancellationToken, Task> onAsyncRead) : IMftVolume
    {
        private readonly MftMemoryVolume _inner = new(image);
        private int _asyncCalls;

        public void ReadAt(long offset, Span<byte> buffer)
        {
            _inner.ReadAt(offset, buffer);
        }

        public async ValueTask ReadAtAsync(long offset, Memory<byte> buffer, CancellationToken cancel)
        {
            var call = Interlocked.Increment(ref _asyncCalls);
            await onAsyncRead(call, offset, buffer.Length, cancel).ConfigureAwait(false);
            _inner.ReadAt(offset, buffer.Span);
        }

        public void Dispose()
        {
        }
    }
}
