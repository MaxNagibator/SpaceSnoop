using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;
using System.Runtime.CompilerServices;

namespace SpaceSnoop.Tests;

[TestFixture]
public class FlatArenaTests
{
    [Test]
    public void DirectoryEntry_Is72BytesAndReferenceFree()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Unsafe.SizeOf<DirectoryEntry>(), Is.EqualTo(72));
            Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<DirectoryEntry>(), Is.False);
        }
    }

    [Test]
    public void CanonicalWeakFacade_PreservesIdentityAndCollectsWhenUnreferenced()
    {
        var (store, root, childIndex) = BuildTwoLevelArena();

        var weakChild = GetAndReleaseChildFacade(store, childIndex, out var sameIdentity);
        Assert.That(sameIdentity, Is.True);

#pragma warning disable S1215
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
#pragma warning restore S1215

        using (Assert.EnterMultipleScope())
        {
            Assert.That(weakChild.TryGetTarget(out _), Is.False);
            Assert.That(root.TotalSize, Is.EqualTo(300));
            Assert.That(root.SubDirectories[0].Name, Is.EqualTo("sub"));
        }

        GC.KeepAlive(store);
    }

    [Test]
    public void ContainsChild_ChecksParentIndexWithoutMaterializingSiblings()
    {
        var store = new ScanTreeStore(1);
        var rootIdx = store.AllocateDirectorySlot();
        var sib0Idx = store.AllocateDirectorySlot();
        var sib1Idx = store.AllocateDirectorySlot();
        var sib2Idx = store.AllocateDirectorySlot();

        store.GetDirectoryRef(rootIdx) = new()
        {
            NameId = store.Names.Intern("root"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalFileCount = 0,
            TotalDirCount = 3,
            State = SpaceState.Added,
        };
        InitChildDir(store, sib0Idx, "sib0", 10);
        InitChildDir(store, sib1Idx, "sib1", 20);
        InitChildDir(store, sib2Idx, "sib2", 30);

        store.LinkChildDirectory(rootIdx, sib2Idx);
        store.LinkChildDirectory(rootIdx, sib1Idx);
        store.LinkChildDirectory(rootIdx, sib0Idx);
        store.GetDirectoryRef(rootIdx).TotalSize = 60;

        var root = store.GetOrCreateDirectoryFacade(rootIdx);
        var sib1 = store.GetOrCreateDirectoryFacade(sib1Idx);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.ContainsChild(sib1), Is.True);
            Assert.That(store.MaterializedDirectoryFacadeCount, Is.EqualTo(2));
        }

        root.Remove(sib1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.ContainsChild(sib1), Is.False);
            Assert.That(sib1.Parent, Is.SameAs(root));
            Assert.That(sib1.AbsolutePath, Is.EqualTo(Path.Combine("root", "sib1")));
            Assert.That(root.TotalSize, Is.EqualTo(40));
            Assert.That(root.TotalDirectoryCount, Is.EqualTo(2));
        }

        root.Remove(sib1);
        Assert.That(root.TotalSize, Is.EqualTo(40));
    }

    [Test]
    public void ArenaMutations_RemoveAddFileDeleteRestoreCollectMarkedAndFixAbsolutePathWorkCorrectly()
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var rootIdx = store.AllocateDirectorySlot();
        var subAIdx = store.AllocateDirectorySlot();
        var subBIdx = store.AllocateDirectorySlot();
        var leafBIdx = store.AllocateDirectorySlot();

        writer.WriteFiles(
            [new FileEntry { Size = 100, NameId = store.Names.Intern("a.bin"), State = SpaceState.Added }],
            out var aStart);
        writer.WriteFiles(
            [new FileEntry { Size = 250, NameId = store.Names.Intern("b.bin"), State = SpaceState.Added }],
            out var bStart);

        store.GetDirectoryRef(rootIdx) = new()
        {
            NameId = store.Names.Intern("ScanRoot"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = 350,
            TotalFileCount = 2,
            TotalDirCount = 3,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(subAIdx) = new()
        {
            NameId = store.Names.Intern("SubA"),
            FirstChildDir = -1,
            NextSiblingDir = -1,
            FileStart = aStart,
            FileCount = 1,
            Size = 100,
            TotalSize = 100,
            TotalFileCount = 1,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(subBIdx) = new()
        {
            NameId = store.Names.Intern("SubB"),
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = 250,
            TotalFileCount = 1,
            TotalDirCount = 1,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(leafBIdx) = new()
        {
            NameId = store.Names.Intern("LeafB"),
            FirstChildDir = -1,
            NextSiblingDir = -1,
            FileStart = bStart,
            FileCount = 1,
            Size = 250,
            TotalSize = 250,
            TotalFileCount = 1,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };

        store.LinkChildDirectory(rootIdx, subBIdx);
        store.LinkChildDirectory(rootIdx, subAIdx);
        store.LinkChildDirectory(subBIdx, leafBIdx);

        var root = store.GetOrCreateDirectoryFacade(rootIdx);
        var tempRoot = Path.Combine(Path.GetTempPath(), $"FlatArenaFix_{Guid.NewGuid():N}", "ScanRoot");
        root.FixAbsolutePath(new DirectoryInfo(tempRoot));
        var syntheticParent = (DirectorySpace)root.Parent!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(syntheticParent.ContainsChild(root), Is.False);
            Assert.That(root.AbsolutePath, Is.EqualTo(tempRoot));
            Assert.That(root.MaxTotalSize, Is.EqualTo(350));
            Assert.That(root.TotalSizeText, Is.EqualTo(SizeFormatter.Format(350)));
        }

        var leafB = store.GetOrCreateDirectoryFacade(leafBIdx);
        var bFile = leafB.GetFile(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(bFile.AbsolutePath, Is.EqualTo(Path.Combine(tempRoot, "SubB", "LeafB", "b.bin")));
            Assert.That(root.HasAddedChild(), Is.True);
            Assert.That(root.HasMarkedSelfOrChild(), Is.False);
        }

        leafB.Delete();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.HasMarkedSelfOrChild(), Is.True);
            Assert.That(root.HasMarkedChild(), Is.True);
            Assert.That(bFile.IsDeleted, Is.True);
        }

        var marked = new List<SpaceBase>();
        root.CollectMarked(marked);
        Assert.That(marked, Is.EqualTo(new SpaceBase[] { leafB }));

        bFile.Restore();
        Assert.That(leafB.IsDeleted, Is.False);

        leafB.Delete();
        root.RestoreRecursive();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.HasMarkedSelfOrChild(), Is.False);
            Assert.That(bFile.IsDeleted, Is.False);
        }

        var added = leafB.AddFile("extra.bin", 50, DateTime.Now, DateTime.Now);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(leafB.TotalSize, Is.EqualTo(300));
            Assert.That(root.TotalSize, Is.EqualTo(400));
            Assert.That(root.TotalFileCount, Is.EqualTo(3));
            Assert.That(syntheticParent.TotalSize, Is.Zero);
        }

        leafB.Remove(added);
        Assert.That(root.TotalSize, Is.EqualTo(350));

        var subB = store.GetOrCreateDirectoryFacade(subBIdx);
        root.Remove(subB);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.ContainsChild(subB), Is.False);
            Assert.That(root.TotalSize, Is.EqualTo(100));
            Assert.That(root.TotalFileCount, Is.EqualTo(1));
            Assert.That(root.TotalDirectoryCount, Is.EqualTo(1));
            Assert.That(subB.Parent, Is.SameAs(root));
            Assert.That(subB.AbsolutePath, Is.EqualTo(Path.Combine(tempRoot, "SubB")));
            Assert.That(syntheticParent.TotalSize, Is.Zero);
        }
    }

    [Test]
    public void MftScanner_FlatArenaMaterialize_VolumeRootWideBfsAndSubdirectoryParity()
    {
        const int wideDirs = 300;
        var builder = new MftVolumeBuilder()
            .Record(MftLayout.RootRecord, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "."))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Alpha"))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Beta"))
            .Record(22, new MftRecordBuilder().FileName(20, 1, "alpha.bin").ResidentData(100));

        var expectedTotalSize = 100L;
        var expectedTotalFiles = 1;
        var expectedTotalDirs = 2 + wideDirs;
        var nextRec = 30;

        for (var i = 0; i < wideDirs; i++)
        {
            var dirRec = nextRec++;
            var fileRec = nextRec++;
            var size = 10 + i;
            builder
                .Record(dirRec, new MftRecordBuilder().AsDirectory().FileName(20, 1, $"d_{i:D3}"))
                .Record(fileRec, new MftRecordBuilder().FileName(dirRec, 1, $"f_{i:D3}.bin").ResidentData(size));
            expectedTotalSize += size;
            expectedTotalFiles++;
        }

        var image = builder.Build(nextRec + 16);
        var scanner = new MftScanner();
        var rootProgress = new ScanProgress();
        var rootResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\"), 'T', 4, 4096, rootProgress);
        var rootSnapshot = rootProgress.CreateSnapshot();

        var subResult = scanner.Calculate(new MftMemoryVolume(image), new DirectoryInfo(@"T:\Alpha"), 'T', 4, 4096, null);

        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 2, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var reachable = new ulong[(table.Entries.Length + 63) >> 6];
        MftLinks.SetReachable(reachable, MftLayout.RootRecord);
        var capture = new MftUsnMaterializeCapture(table.Entries.Length);
        var capturedRoot = MftScanner.Materialize(
            table,
            links,
            MftLayout.RootRecord,
            new DirectoryInfo(@"T:\"),
            new MftSubtreeTotals(),
            reachable,
            null,
            CancellationToken.None,
            capture);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootResult.Root.DirIndex, Is.Zero);
            Assert.That(rootResult.Root.TotalSize, Is.EqualTo(expectedTotalSize));
            Assert.That(rootResult.Root.TotalFileCount, Is.EqualTo(expectedTotalFiles));
            Assert.That(rootResult.Root.TotalDirectoryCount, Is.EqualTo(expectedTotalDirs));
            Assert.That(rootSnapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(rootSnapshot.TopLevelCompleted, Is.EqualTo(2));
            Assert.That(rootSnapshot.DirectoriesScanned, Is.EqualTo(expectedTotalDirs + 1));
            Assert.That(rootSnapshot.FilesScanned, Is.EqualTo(expectedTotalFiles));
            Assert.That(rootSnapshot.BytesScanned, Is.EqualTo(expectedTotalSize));

            Assert.That(subResult.Root.DirIndex, Is.Zero);
            Assert.That(subResult.Root.TotalSize, Is.EqualTo(expectedTotalSize));
            Assert.That(subResult.Root.TotalFileCount, Is.EqualTo(expectedTotalFiles));
            Assert.That(subResult.Root.TotalDirectoryCount, Is.EqualTo(wideDirs));
            Assert.That(subResult.Root.AbsolutePath, Is.EqualTo(@"T:\Alpha"));

            Assert.That(capturedRoot.TotalSize, Is.EqualTo(expectedTotalSize));
            Assert.That(capture.FrnLocation[MftLayout.RootRecord], Is.Zero);
            Assert.That(capture.FrnLocation[20], Is.GreaterThan(0));
            Assert.That(capture.FrnLocation[22], Is.EqualTo(~capture.FrnLocation[20]));
            Assert.That(capture.FrnSequence[22], Is.EqualTo(table.Entries[22].Sequence));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<DirectorySpace> GetAndReleaseChildFacade(
        ScanTreeStore store,
        int childIndex,
        out bool sameIdentity)
    {
        var first = store.GetOrCreateDirectoryFacade(childIndex);
        var second = store.GetOrCreateDirectoryFacade(childIndex);
        var viaParent = store.GetOrCreateDirectoryFacade(0).SubDirectories[0];
        sameIdentity = ReferenceEquals(first, second) && ReferenceEquals(first, viaParent);
        return new(first);
    }

    private static (ScanTreeStore Store, DirectorySpace Root, int ChildIndex) BuildTwoLevelArena()
    {
        var store = new ScanTreeStore(1);
        var writer = new FileSlabWriter(store);
        var rootIdx = store.AllocateDirectorySlot();
        var childIdx = store.AllocateDirectorySlot();

        writer.WriteFiles(
            [new FileEntry { Size = 300, NameId = store.Names.Intern("f.bin"), State = SpaceState.Added }],
            out var fStart);

        store.GetDirectoryRef(rootIdx) = new()
        {
            NameId = store.Names.Intern("root"),
            ParentIndex = -1,
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = 300,
            TotalFileCount = 1,
            TotalDirCount = 1,
            State = SpaceState.Added,
        };
        store.GetDirectoryRef(childIdx) = new()
        {
            NameId = store.Names.Intern("sub"),
            FirstChildDir = -1,
            NextSiblingDir = -1,
            FileStart = fStart,
            FileCount = 1,
            Size = 300,
            TotalSize = 300,
            TotalFileCount = 1,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };
        store.LinkChildDirectory(rootIdx, childIdx);
        return (store, store.GetOrCreateDirectoryFacade(rootIdx), childIdx);
    }

    private static void InitChildDir(ScanTreeStore store, int dirIndex, string name, long size)
    {
        store.GetDirectoryRef(dirIndex) = new()
        {
            NameId = store.Names.Intern(name),
            FirstChildDir = -1,
            NextSiblingDir = -1,
            TotalSize = size,
            TotalFileCount = 0,
            TotalDirCount = 0,
            State = SpaceState.Added,
        };
    }
}
