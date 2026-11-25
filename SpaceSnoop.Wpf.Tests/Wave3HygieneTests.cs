using System.Diagnostics;
using System.IO;
using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class Wave3HygieneTests
{
    private string _tempDir = null!;
    private ScanNodeFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopWave3_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var settings = new MemorySettings();
        _factory = new(new(settings), new(settings), new FakeShellLauncher());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Отложенная_карта_не_строится_в_режиме_дерева_и_сбрасывает_устаревший_корень_при_повторном_скане()
    {
        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        using var logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_tempDir, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });
        using var services = App.ConfigureServices(settings, logging);
        var scan = services.GetRequiredService<ScanViewModel>();

        var first = BuildDirectory("first", 10);
        var second = BuildDirectory("second", 12);

        scan.ViewMode = ScanViewMode.Tree;
        scan.ApplyScanResult(first, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1), null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.Treemap.TreemapRoot, Is.Null);
            Assert.That(scan.Treemap.HasTreemapTiles, Is.False);
        }

        scan.ViewMode = ScanViewMode.Treemap;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.Treemap.TreemapRoot?.Space, Is.SameAs(first));
            Assert.That(scan.Treemap.HasTreemapTiles, Is.True);
        }

        scan.ViewMode = ScanViewMode.Tree;
        scan.ApplyScanResult(second, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1), null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.Treemap.TreemapRoot, Is.Null);
            Assert.That(scan.Treemap.HasTreemapTiles, Is.False);
        }

        scan.ViewMode = ScanViewMode.Treemap;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.Treemap.TreemapRoot?.Space, Is.SameAs(second));
            Assert.That(scan.Treemap.HasTreemapTiles, Is.True);
        }
    }

    [Test]
    public void PreviewTiles_и_Treemap_выбирают_topK_из_DirectorySpace_без_EnsureLoaded()
    {
        var rootSpace = BuildDirectory("root", 2);
        var wideSpace = new DirectorySpace("wide", rootSpace, DateTime.Now, DateTime.Now);
        var store = new ScanTreeStore(16, singleWriter: true);
        var writer = new FileSlabWriter(store);
        var stamp = TimestampEncoding.FromDateTime(DateTime.UtcNow);
        const int totalFiles = 500;
        var entries = new FileEntry[totalFiles];
        var totalSize = 0L;

        for (var i = 0; i < totalFiles; i++)
        {
            var size = (i + 1) * 100L;
            entries[i] = new FileEntry
            {
                Size = size,
                CreationRaw = stamp,
                LastAccessRaw = stamp,
                NameId = store.Names.Intern($"item_{i:D4}.bin"),
                State = SpaceState.Added,
                Flags = 0,
            };
            totalSize += size;
        }

        writer.WriteFiles(entries, out var start);
        wideSpace.SetScanned(store, start, totalFiles, totalSize, []);
        wideSpace.AggregateTotals();
        store.Freeze();
        rootSpace.Add(wideSpace);

        var rootNode = _factory.CreateRoot(rootSpace, new());
        rootNode.EnsureLoaded();
        var wideNode = rootNode.Children.Single(c => ReferenceEquals(c.Space, wideSpace));

        Assert.That(wideNode.IsLoaded, Is.False);

        var previews = wideNode.PreviewTiles;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wideNode.IsLoaded, Is.False);
            Assert.That(previews, Has.Count.EqualTo(AppDefaults.TreemapPreviewLimit));
            Assert.That(previews[0].Weight, Is.EqualTo(totalFiles * 100L));
            Assert.That(previews[^1].Weight, Is.EqualTo((totalFiles - AppDefaults.TreemapPreviewLimit + 1) * 100L));
        }

        var treemap = new ScanTreemapViewModel([rootNode]);
        treemap.SetRoot(rootNode);
        treemap.DrillIntoCommand.Execute(wideNode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(wideNode.IsLoaded, Is.False);
            Assert.That(treemap.TreemapTiles, Has.Count.EqualTo(AppDefaults.TreemapTileLimit));
            Assert.That(treemap.TreemapTruncated, Is.True);
            Assert.That(treemap.TreemapTiles[0].Weight, Is.EqualTo(totalFiles * 100L));
        }

        var topTile = treemap.TreemapTiles[0];
        topTile.Space!.Delete();
        treemap.RefreshMarks();

        Assert.That(topTile.IsMarkedDeleted, Is.True);
    }

    [Test]
    public void ScanTreeEditor_не_загружает_нераскрытые_узлы_при_удалении_и_добавлении()
    {
        var rootSpace = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        var branch = new DirectorySpace("branch", rootSpace, DateTime.Now, DateTime.Now);
        var filePath = Path.Combine(_tempDir, "only.bin");
        File.WriteAllBytes(filePath, new byte[256]);
        branch.AddFile(new(filePath));
        rootSpace.Add(branch);

        var emptyBranch = new DirectorySpace("empty", rootSpace, DateTime.Now, DateTime.Now);
        var nested = new DirectorySpace("nested", emptyBranch, DateTime.Now, DateTime.Now);
        emptyBranch.Add(nested);
        rootSpace.Add(emptyBranch);

        var rootNode = _factory.CreateRoot(rootSpace, new());
        rootNode.EnsureLoaded();
        var branchNode = rootNode.Children.Single(c => ReferenceEquals(c.Space, branch));
        var emptyNode = rootNode.Children.Single(c => ReferenceEquals(c.Space, emptyBranch));

        Assert.That(branchNode.IsLoaded, Is.False);
        Assert.That(emptyNode.IsLoaded, Is.False);

        var onlyFile = branch.GetFile(0);
        branch.Remove(onlyFile);
        ScanTreeEditor.RefreshNodeAfterDeletion(rootNode, [onlyFile]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(branchNode.IsLoaded, Is.False);
            Assert.That(branchNode.HasChildren, Is.False);
            Assert.That(branchNode.Children, Is.Empty);
        }

        nested.AddFile("archive.zip", 1024, DateTime.Now, DateTime.Now);
        ScanTreeEditor.RefreshNodeAfterAddition(rootNode, nested);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(emptyNode.IsLoaded, Is.False);
            Assert.That(emptyNode.HasChildren, Is.True);
            Assert.That(emptyNode.Children, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public async Task CheckForScanAsync_выполняет_RootRefusal_только_после_Exists_и_соблюдает_таймаут_и_отмену()
    {
        var prevExists = ScanTargetProbe.DirectoryExists;
        var prevRefusal = ScanTargetProbe.RootRefusal;

        try
        {
            var refusalCalls = 0;
            ScanTargetProbe.DirectoryExists = _ => false;
            ScanTargetProbe.RootRefusal = _ =>
            {
                Interlocked.Increment(ref refusalCalls);
                return "unexpected";
            };

            var missing = await ScanTargetProbe.CheckForScanAsync(@"\\wave3-missing\share\dir", new(_tempDir), 500, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(missing.Status, Is.EqualTo(PathProbeStatus.Missing));
                Assert.That(missing.Refusal, Is.Null);
                Assert.That(refusalCalls, Is.Zero);
            }

            ScanTargetProbe.DirectoryExists = _ => true;
            ScanTargetProbe.RootRefusal = _ => "root-refused";
            var refused = await ScanTargetProbe.CheckForScanAsync(@"\\wave3-refused\share\dir", new(_tempDir), 500, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(refused.Status, Is.EqualTo(PathProbeStatus.Exists));
                Assert.That(refused.Refusal, Is.EqualTo("root-refused"));
            }

            using var releaseTimeout = new ManualResetEventSlim();
            ScanTargetProbe.RootRefusal = _ =>
            {
                releaseTimeout.Wait(TimeSpan.FromSeconds(5));
                return null;
            };

            var timedOut = await ScanTargetProbe.CheckForScanAsync(@"\\wave3-timeout\share\dir", new(_tempDir), 60, CancellationToken.None);
            releaseTimeout.Set();
            Assert.That(timedOut.Status, Is.EqualTo(PathProbeStatus.TimedOut));

            using var entered = new ManualResetEventSlim();
            using var releaseCancel = new ManualResetEventSlim();
            using var cts = new CancellationTokenSource();
            ScanTargetProbe.RootRefusal = _ =>
            {
                entered.Set();
                releaseCancel.Wait(TimeSpan.FromSeconds(5));
                return null;
            };

            var cancelTask = ScanTargetProbe.CheckForScanAsync(@"\\wave3-cancel\share\dir", new(_tempDir), 5000, cts.Token);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            await cts.CancelAsync();
            var cancelled = await cancelTask;
            releaseCancel.Set();
            Assert.That(cancelled.Status, Is.EqualTo(PathProbeStatus.Cancelled));
        }
        finally
        {
            ScanTargetProbe.DirectoryExists = prevExists;
            ScanTargetProbe.RootRefusal = prevRefusal;
        }
    }

    [Test]
    public async Task CheckForScanAsync_отклоняет_junction_и_пропускает_обычный_каталог()
    {
        var target = Path.Combine(_tempDir, "junc-target");
        var link = Path.Combine(_tempDir, "junc-link");
        Directory.CreateDirectory(target);

        using var mk = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        });
        mk?.WaitForExit(3000);

        try
        {
            var juncResult = await ScanTargetProbe.CheckForScanAsync(link, new(link), 3000, CancellationToken.None);
            var normalResult = await ScanTargetProbe.CheckForScanAsync(target, new(target), 3000, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(juncResult.Status, Is.EqualTo(PathProbeStatus.Exists));
                Assert.That(juncResult.Refusal, Is.Not.Null);
                Assert.That(normalResult.Status, Is.EqualTo(PathProbeStatus.Exists));
                Assert.That(normalResult.Refusal, Is.Null);
            }
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link, false);
            }
        }
    }

    private DirectorySpace BuildDirectory(string name, int fileCount)
    {
        var dirPath = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(dirPath);
        var root = new DirectorySpace(dirPath, null, DateTime.Now, DateTime.Now);

        for (var i = 0; i < fileCount; i++)
        {
            var filePath = Path.Combine(dirPath, $"f{i:D2}.bin");
            File.WriteAllBytes(filePath, new byte[(i + 1) * 128]);
            root.AddFile(new(filePath));
        }

        return root;
    }
}
