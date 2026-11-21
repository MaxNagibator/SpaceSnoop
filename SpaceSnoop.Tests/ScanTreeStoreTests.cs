using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ScanTreeStoreTests
{
    [Test]
    public void FileEntry_SizeIsExactly32Bytes()
    {
        Assert.That(Unsafe.SizeOf<FileEntry>(), Is.EqualTo(32));
    }

    [Test]
    public void TimestampEncoding_PreservesExactValuesAndBoundaryEpochs()
    {
        var modernUtc = new DateTimeOffset(2026, 11, 21, 10, 30, 15, 123, TimeSpan.Zero);
        var fileTimeEpoch = new DateTimeOffset(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ancientUtc = new DateTimeOffset(1500, 5, 1, 12, 0, 0, TimeSpan.Zero);

        TimestampEncoding.FromDateTimeOffsets(modernUtc, modernUtc, out var cModern, out var aModern, out var fModern);
        TimestampEncoding.FromDateTimeOffsets(fileTimeEpoch, fileTimeEpoch, out var cEpoch, out var aEpoch, out var fEpoch);
        TimestampEncoding.FromDateTimeOffsets(ancientUtc, ancientUtc, out var cAncient, out var aAncient, out var fAncient);

        var dtLocal = new DateTime(2025, 3, 14, 9, 26, 53, DateTimeKind.Local);
        var entryDefaultMft = new FileEntry
        {
            CreationRaw = TimestampEncoding.FromMftFileTime(0),
            LastAccessRaw = TimestampEncoding.FromMftFileTime(-1),
            Flags = FileEntry.UtcFileTimeFlag,
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fModern, Is.EqualTo(FileEntry.UtcFileTimeFlag));
            Assert.That(TimestampEncoding.ToDateTime(cModern, fModern), Is.EqualTo(modernUtc.LocalDateTime));
            Assert.That(TimestampEncoding.ToDateTime(aModern, fModern), Is.EqualTo(modernUtc.LocalDateTime));

            Assert.That(fEpoch, Is.Zero);
            Assert.That(TimestampEncoding.ToDateTime(cEpoch, fEpoch), Is.EqualTo(fileTimeEpoch.LocalDateTime));
            Assert.That(TimestampEncoding.ToDateTime(aEpoch, fEpoch), Is.EqualTo(fileTimeEpoch.LocalDateTime));

            Assert.That(fAncient, Is.Zero);
            Assert.That(TimestampEncoding.ToDateTime(cAncient, fAncient), Is.EqualTo(ancientUtc.LocalDateTime));
            Assert.That(TimestampEncoding.ToDateTime(aAncient, fAncient), Is.EqualTo(ancientUtc.LocalDateTime));

            Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(dtLocal), 0), Is.EqualTo(dtLocal));
            Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(DateTime.MinValue), 0), Is.EqualTo(DateTime.MinValue));

            Assert.That(entryDefaultMft.GetCreationDate(), Is.EqualTo(default(DateTime)));
            Assert.That(entryDefaultMft.GetLastAccessTime(), Is.EqualTo(default(DateTime)));
        }
    }

    [Test]
    public void ScanTreeStore_SupportsLargeDirectoryOverflowAboveChunkSize()
    {
        const int count = ScanTreeStore.ChunkSize + 150;
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);

        var buffer = new FileEntry[count];
        var expectedTotal = 0L;

        for (var i = 0; i < count; i++)
        {
            var size = (i % 100) + 1L;
            buffer[i] = new()
            {
                Size = size,
                NameId = store.Names.Intern($"f_{i}.bin"),
                State = SpaceState.Added,
            };
            expectedTotal += size;
        }

        writer.WriteFiles(buffer, out var fileStart);
        root.SetScanned(store, fileStart, count, expectedTotal, []);
        store.Freeze();
        root.AggregateTotals();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.FileCount, Is.EqualTo(count));
            Assert.That(root.TotalFileCount, Is.EqualTo(count));
            Assert.That(root.TotalSize, Is.EqualTo(expectedTotal));
            Assert.That(root.GetFileName(0), Is.EqualTo("f_0.bin"));
            Assert.That(root.GetFileName(count - 1), Is.EqualTo($"f_{count - 1}.bin"));
            Assert.That(root.FileEntries[count - 1].Size, Is.EqualTo(((count - 1) % 100) + 1L));
        }
    }

    [Test]
    public void DirectorySpace_LazyFacadeIdentityDeleteRestoreAndDetachShiftWorkCorrectly()
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var parent = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);
        var sub = new DirectorySpace(store, store.Names.Intern("sub"), parent, 0, 0, 0);

        var stamp = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Local);
        var rawStamp = TimestampEncoding.FromDateTime(stamp);

        FileEntry[] entries =
        [
            new()
            {
                Size = 100,
                CreationRaw = rawStamp,
                LastAccessRaw = rawStamp,
                NameId = store.Names.Intern("alpha.txt"),
                State = SpaceState.Added,
            },
            new()
            {
                Size = 200,
                CreationRaw = rawStamp,
                LastAccessRaw = rawStamp,
                NameId = store.Names.Intern("beta.txt"),
                State = SpaceState.Added,
            },
            new()
            {
                Size = 300,
                CreationRaw = rawStamp,
                LastAccessRaw = rawStamp,
                NameId = store.Names.Intern("gamma.txt"),
                State = SpaceState.Added,
            },
        ];

        writer.WriteFiles(entries, out var subStart);
        sub.SetScanned(store, subStart, entries.Length, 600, []);
        parent.SetScanned(store, 0, 0, 0, [sub]);
        store.Freeze();
        parent.AggregateTotals();

        var betaFirst = sub.Files[1];
        var betaSecond = sub.GetFile(1);
        var gamma = sub.GetFile(2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReferenceEquals(betaFirst, betaSecond), Is.True);
            Assert.That(sub.ContainsChild(betaFirst), Is.True);
            Assert.That(sub.FileNameEquals(0, "ALPHA.TXT", StringComparison.OrdinalIgnoreCase), Is.True);
            Assert.That(sub.NameEquals("sub", StringComparison.Ordinal), Is.True);
        }

        parent.Delete();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sub.FileEntries[0].IsDeleted, Is.True);
            Assert.That(betaFirst.IsDeleted, Is.True);
            Assert.That(gamma.IsDeleted, Is.True);
        }

        betaFirst.Restore();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parent.IsDeleted, Is.False);
            Assert.That(sub.IsDeleted, Is.False);
            Assert.That(betaFirst.IsDeleted, Is.False);
            Assert.That(sub.FileEntries[1].IsDeleted, Is.False);
            Assert.That(sub.FileEntries[0].IsDeleted, Is.True);
            Assert.That(gamma.IsDeleted, Is.True);
        }

        sub.Restore();
        Assert.That(gamma.IsDeleted, Is.True);

        parent.Delete();
        parent.Restore();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sub.FileEntries[0].IsDeleted, Is.False);
            Assert.That(betaFirst.IsDeleted, Is.False);
            Assert.That(gamma.IsDeleted, Is.False);
        }

        var alpha = sub.GetFile(0);
        sub.Remove(alpha);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sub.ContainsChild(alpha), Is.False);
            Assert.That(alpha.Parent, Is.SameAs(sub));
            Assert.That(alpha.Name, Is.EqualTo("alpha.txt"));
            Assert.That(alpha.AbsolutePath, Is.EqualTo(Path.Combine("root", "sub", "alpha.txt")));
            Assert.That(alpha.Size, Is.EqualTo(100));
            Assert.That(alpha.CreationDate, Is.EqualTo(stamp));
            Assert.That(alpha.LastAccessTime, Is.EqualTo(stamp));
            Assert.That(alpha.State, Is.EqualTo(SpaceState.Added));

            Assert.That(sub.FileCount, Is.EqualTo(2));
            Assert.That(ReferenceEquals(sub.GetFile(0), betaFirst), Is.True);
            Assert.That(ReferenceEquals(sub.GetFile(1), gamma), Is.True);
            Assert.That(betaFirst.Name, Is.EqualTo("beta.txt"));
            Assert.That(gamma.Name, Is.EqualTo("gamma.txt"));
            Assert.That(parent.TotalSize, Is.EqualTo(500));
            Assert.That(parent.TotalFileCount, Is.EqualTo(2));
        }
    }

    [Test]
    public void FullScanExport_And_ScanExport_HandleRootAndNestedErrorsWithoutErrors()
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);
        var badChild = new DirectorySpace(store, store.Names.Intern("bad"), root, 0, 0, 0);
        var okChild = new DirectorySpace(store, store.Names.Intern("ok"), root, 0, 0, 0);

        FileEntry[] okFiles =
        [
            new()
            {
                Size = 1234,
                NameId = store.Names.Intern("data.bin"),
                State = SpaceState.Added,
            },
        ];

        writer.WriteFiles(okFiles, out var okStart);
        okChild.SetScanned(store, okStart, okFiles.Length, 1234, []);
        badChild.SetScanned(store, 0, 0, 0, []);
        badChild.Error();

        root.SetScanned(store, 0, 0, 0, [badChild, okChild]);
        root.Error();
        store.Freeze();
        root.AggregateTotals();

        var export = ScanExport.Build(root, @"C:\root", new(2, false, 1));

        using var stream = new MemoryStream();
        FullScanExport.WriteJson(stream, root, @"C:\root", new(2, false, 1), "test", null, null, CancellationToken.None);
        var fullJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.Totals.Errors, Is.EqualTo(2));
            Assert.That(export.ErrorPaths, Is.EquivalentTo(new[] { ".", "bad" }));
            Assert.That(export.Files.Single().Path, Is.EqualTo(Path.Combine("ok", "data.bin")));
            Assert.That(fullJson, Does.Contain("data.bin"));
            Assert.That(fullJson, Does.Contain("\"rootError\":true"));
        }
    }

    [Test]
    public void DirectorySpace_EnsureStoreInheritsAncestorStoreAcrossMultiLevelTrees()
    {
        var now = DateTime.Now;
        var syntheticParent = new DirectorySpace(@"C:\RootParent", null, now, now);
        var root = new DirectorySpace("root", syntheticParent, now, now);
        var child = new DirectorySpace("child", root, now, now);
        var grandChild = new DirectorySpace("grandchild", child, now, now);
        var tempDir = Directory.CreateTempSubdirectory("spacesnoop-store-test-");

        try
        {
            var f1 = Path.Combine(tempDir.FullName, "a.bin");
            var f2 = Path.Combine(tempDir.FullName, "b.bin");
            var f3 = Path.Combine(tempDir.FullName, "c.bin");
            File.WriteAllBytes(f1, new byte[10]);
            File.WriteAllBytes(f2, new byte[20]);
            File.WriteAllBytes(f3, new byte[30]);

            root.AddFiles([new FileInfo(f1)]);
            child.AddFiles([new FileInfo(f2)]);
            grandChild.AddFile(new FileInfo(f3));

            root.Add(child);
            child.Add(grandChild);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(syntheticParent.Store, Is.Null);
                Assert.That(root.Store, Is.Not.Null);
                Assert.That(child.Store, Is.SameAs(root.Store));
                Assert.That(grandChild.Store, Is.SameAs(root.Store));
                Assert.That(grandChild.GetFileName(0), Is.EqualTo("c.bin"));
                Assert.That(root.TotalFileCount, Is.EqualTo(3));
            }
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void FileSlabWriter_WritesAcrossMultiple16KChunksCleanly()
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);
        const int dirCount = 40;
        const int filesPerDir = 1000;
        var dirs = new DirectorySpace[dirCount];

        for (var d = 0; d < dirCount; d++)
        {
            var dir = new DirectorySpace(store, store.Names.Intern($"d_{d}"), root, 0, 0, 0);
            var batch = new FileEntry[filesPerDir];

            for (var f = 0; f < filesPerDir; f++)
            {
                batch[f] = new()
                {
                    Size = (d * filesPerDir) + f + 1,
                    NameId = store.Names.Intern($"file_{d}_{f}.dat"),
                    State = SpaceState.Added,
                };
            }

            writer.WriteFiles(batch, out var start);
            dir.SetScanned(store, start, filesPerDir, batch.Sum(static x => x.Size), []);
            dirs[d] = dir;
        }

        root.SetScanned(store, 0, 0, 0, dirs);
        store.Freeze();
        root.AggregateTotals();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.TotalFileCount, Is.EqualTo(dirCount * filesPerDir));
            Assert.That(dirs[0].GetFileName(0), Is.EqualTo("file_0_0.dat"));
            Assert.That(dirs[16].GetFileName(384), Is.EqualTo("file_16_384.dat"));
            Assert.That(dirs[^1].GetFileName(filesPerDir - 1), Is.EqualTo($"file_{dirCount - 1}_{filesPerDir - 1}.dat"));
        }
    }

    [Test]
    public void DirectorySpace_RemoveFile_ConcurrentReadsThroughFacadeNeverThrowOrCorrupt()
    {
        var store = new ScanTreeStore();
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);
        const int fileCount = 200;
        var entries = new FileEntry[fileCount];

        for (var i = 0; i < fileCount; i++)
        {
            entries[i] = new()
            {
                Size = i + 1L,
                NameId = store.Names.Intern($"item_{i:D3}.bin"),
                State = SpaceState.Added,
            };
        }

        writer.WriteFiles(entries, out var start);
        root.SetScanned(store, start, fileCount, entries.Sum(static x => x.Size), []);
        store.Freeze();
        root.AggregateTotals();

        var facades = root.Files.ToArray();
        using var stop = new CancellationTokenSource();
        var readerTask = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                for (var i = 0; i < facades.Length; i++)
                {
                    var facade = facades[i];
                    var name = facade.Name;
                    var size = facade.Size;
                    var expectedName = $"item_{i:D3}.bin";
                    Assert.That(name, Is.EqualTo(expectedName));
                    Assert.That(size, Is.EqualTo(i + 1L));
                }
            }
        });

        try
        {
            for (var i = 0; i < facades.Length; i++)
            {
                root.Remove(facades[i]);
            }
        }
        finally
        {
            stop.Cancel();
            readerTask.GetAwaiter().GetResult();
        }

        Assert.That(root.FileCount, Is.Zero);
    }

    [Test]
    public void DirectorySpace_AddFiles_ConcurrentSiblingCallsOnInheritedStoreAreThreadSafe()
    {
        var now = DateTime.Now;
        var root = new DirectorySpace("root", null, now, now);
        var siblingCount = 4;
        var siblings = new DirectorySpace[siblingCount];
        var tempDir = Directory.CreateTempSubdirectory("spacesnoop-sibling-concurrent-");

        try
        {
            var rootFile = Path.Combine(tempDir.FullName, "root.bin");
            File.WriteAllBytes(rootFile, new byte[8]);
            root.AddFiles([new FileInfo(rootFile)]);

            for (var s = 0; s < siblingCount; s++)
            {
                siblings[s] = new($"sib_{s}", root, now, now);
                root.Add(siblings[s]);
            }

            const int filesPerSibling = 100;
            var siblingInfos = new FileInfo[siblingCount][];

            for (var s = 0; s < siblingCount; s++)
            {
                siblingInfos[s] = new FileInfo[filesPerSibling];

                for (var i = 0; i < filesPerSibling; i++)
                {
                    var filePath = Path.Combine(tempDir.FullName, $"s{s}_f{i:D3}.bin");
                    File.WriteAllBytes(filePath, new byte[1]);
                    siblingInfos[s][i] = new(filePath);
                }
            }

            Parallel.For(0, siblingCount, s => siblings[s].AddFiles(siblingInfos[s]));

            using (Assert.EnterMultipleScope())
            {
                for (var s = 0; s < siblingCount; s++)
                {
                    Assert.That(siblings[s].Store, Is.SameAs(root.Store));
                    Assert.That(siblings[s].FileCount, Is.EqualTo(filesPerSibling));
                    Assert.That(siblings[s].GetFileName(0), Is.EqualTo($"s{s}_f000.bin"));
                    Assert.That(siblings[s].GetFileName(filesPerSibling - 1), Is.EqualTo($"s{s}_f{filesPerSibling - 1:D3}.bin"));
                }
            }
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void TimestampEncoding_PreservesLocalSpringForwardGapTimestampInDirectoryAndFileSpace()
    {
        var gapLocal = FindLocalSpringForwardGap() ?? new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Local);
        var utc = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Utc);
        var unspecified = new DateTime(2024, 3, 10, 2, 30, 0, DateTimeKind.Unspecified);

        var dir = new DirectorySpace("dir", null, gapLocal, gapLocal);
        var tempDir = Directory.CreateTempSubdirectory("spacesnoop-dst-test-");

        try
        {
            var path = Path.Combine(tempDir.FullName, "gap.bin");
            File.WriteAllBytes(path, [1, 2, 3]);
            var store = new ScanTreeStore(1, singleWriter: true);
            var writer = new FileSlabWriter(store);
            var rawGap = TimestampEncoding.FromDateTime(gapLocal);
            writer.WriteFiles(
                [
                    new FileEntry
                    {
                        Size = 3,
                        CreationRaw = rawGap,
                        LastAccessRaw = rawGap,
                        NameId = store.Names.Intern("gap.bin"),
                        State = SpaceState.Added,
                    },
                ],
                out var start);
            var slabDir = new DirectorySpace(store, store.Names.Intern("slab"), null, rawGap, rawGap, 0);
            slabDir.SetScanned(store, start, 1, 3, []);
            store.Freeze();
            var facade = slabDir.GetFile(0);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(gapLocal), 0), Is.EqualTo(gapLocal));
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(gapLocal), 0).Kind, Is.EqualTo(DateTimeKind.Local));
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(utc), 0), Is.EqualTo(utc));
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(utc), 0).Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(unspecified), 0), Is.EqualTo(unspecified));
                Assert.That(TimestampEncoding.ToDateTime(TimestampEncoding.FromDateTime(unspecified), 0).Kind, Is.EqualTo(DateTimeKind.Unspecified));

                Assert.That(dir.CreationDate, Is.EqualTo(gapLocal));
                Assert.That(dir.LastAccessTime, Is.EqualTo(gapLocal));
                Assert.That(slabDir.CreationDate, Is.EqualTo(gapLocal));
                Assert.That(facade.CreationDate, Is.EqualTo(gapLocal));
                Assert.That(facade.LastAccessTime, Is.EqualTo(gapLocal));
            }
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Test]
    public void DirectorySpace_Files_ReturnsSnapshotUnaffectedBySubsequentRemoval()
    {
        var store = new ScanTreeStore(1, singleWriter: true);
        var writer = new FileSlabWriter(store);
        var dir = new DirectorySpace(store, store.Names.Intern("dir"), null, 0, 0, 0);

        writer.WriteFiles(
            [
                new FileEntry { Size = 10, NameId = store.Names.Intern("a.txt"), State = SpaceState.Added },
                new FileEntry { Size = 20, NameId = store.Names.Intern("b.txt"), State = SpaceState.Added },
                new FileEntry { Size = 30, NameId = store.Names.Intern("c.txt"), State = SpaceState.Added },
            ],
            out var start);
        dir.SetScanned(store, start, 3, 60, []);
        store.Freeze();

        var snapshot = dir.Files;
        var seen = new List<string>();

        foreach (var file in snapshot)
        {
            seen.Add(file.Name);

            if (seen.Count == 1)
            {
                dir.Remove(file);
            }
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot, Has.Count.EqualTo(3));
            Assert.That(seen, Is.EqualTo(new[] { "a.txt", "b.txt", "c.txt" }));
            Assert.That(dir.Files, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void DirectorySpace_RemoveSubdirectory_AllowsRemovedSubtreeAndFacadesToBeCollected()
    {
        var root = CreateRootWithDetachedSubtreeFacades(out var weakChild, out var weakFacade);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Name, Is.EqualTo("root"));
            Assert.That(weakChild.TryGetTarget(out _), Is.False);
            Assert.That(weakFacade.TryGetTarget(out _), Is.False);
        }

        GC.KeepAlive(root);
    }

    [Test]
    public void ScanTreeStore_AppendFiles_ReusesSlotInPlaceAndGrowsGeometrically()
    {
        var store = new ScanTreeStore(1, singleWriter: true);
        var fileStart = 0;
        var fileCount = 0;

        FileEntry[] batch1 =
        [
            new() { Size = 1, NameId = store.Names.Intern("f0.bin"), State = SpaceState.Added },
            new() { Size = 2, NameId = store.Names.Intern("f1.bin"), State = SpaceState.Added },
        ];
        store.AppendFiles(ref fileStart, ref fileCount, batch1);
        var stableHandle = fileStart;

        for (var round = 0; round < 6; round++)
        {
            FileEntry[] next =
            [
                new() { Size = round + 10, NameId = store.Names.Intern($"g_{round}_a.bin"), State = SpaceState.Added },
                new() { Size = round + 20, NameId = store.Names.Intern($"g_{round}_b.bin"), State = SpaceState.Added },
            ];
            store.AppendFiles(ref fileStart, ref fileCount, next);
            Assert.That(fileStart, Is.EqualTo(stableHandle));
        }

        var span = store.GetSpan(fileStart, fileCount);
        Assert.That(span.Length, Is.EqualTo(14));
        Assert.That(store.Names.GetString(span[^1].NameId), Is.EqualTo("g_5_b.bin"));
    }

    [Test]
    public void FileSlabWriter_FallsBackToWriteLargeWhenMaxRegularChunksReached()
    {
        Assert.That(ScanTreeStore.MaxRegularChunks, Is.EqualTo(131_072));

        var store = new ScanTreeStore(1, singleWriter: true, maxRegularChunks: 2);
        var writer = new FileSlabWriter(store);
        var chunkPayload = new FileEntry[ScanTreeStore.ChunkSize];

        for (var i = 0; i < chunkPayload.Length; i++)
        {
            chunkPayload[i] = new() { Size = i + 1, NameId = store.Names.Intern("c.bin"), State = SpaceState.Added };
        }

        writer.WriteFiles(chunkPayload, out var start0);
        writer.WriteFiles(chunkPayload, out var start1);
        FileEntry[] overflowBatch =
        [
            new() { Size = 777, NameId = store.Names.Intern("overflow.bin"), State = SpaceState.Added },
        ];
        writer.WriteFiles(overflowBatch, out var start2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(start0, Is.GreaterThanOrEqualTo(0));
            Assert.That(start1, Is.GreaterThanOrEqualTo(0));
            Assert.That(start2, Is.LessThan(0));
            Assert.That(store.GetSpan(start2, 1)[0].Size, Is.EqualTo(777));
            Assert.That(store.Names.GetString(store.GetSpan(start2, 1)[0].NameId), Is.EqualTo("overflow.bin"));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static DirectorySpace CreateRootWithDetachedSubtreeFacades(
        out WeakReference<DirectorySpace> weakChild,
        out WeakReference<FileSpace> weakFacade)
    {
        var store = new ScanTreeStore();
        var writer = new FileSlabWriter(store);
        var root = new DirectorySpace(store, store.Names.Intern("root"), null, 0, 0, 0);
        var child = new DirectorySpace(store, store.Names.Intern("child"), root, 0, 0, 0);

        writer.WriteFiles(
            [new FileEntry { Size = 42, NameId = store.Names.Intern("leaf.bin"), State = SpaceState.Added }],
            out var start);
        child.SetScanned(store, start, 1, 42, []);
        root.SetScanned(store, 0, 0, 0, [child]);
        store.Freeze();
        root.AggregateTotals();

        var facade = child.GetFile(0);
        weakChild = new(child);
        weakFacade = new(facade);

        root.Remove(child);
        return root;
    }

    private static DateTime? FindLocalSpringForwardGap()
    {
        var tz = TimeZoneInfo.Local;

        if (!tz.SupportsDaylightSavingTime)
        {
            return null;
        }

        for (var year = 2005; year <= 2030; year++)
        {
            var probe = new DateTime(year, 1, 1, 0, 30, 0, DateTimeKind.Local);
            var end = new DateTime(year + 1, 1, 1, 0, 30, 0, DateTimeKind.Local);

            while (probe < end)
            {
                if (tz.IsInvalidTime(probe))
                {
                    return probe;
                }

                probe = probe.AddHours(1);
            }
        }

        return null;
    }
}
