using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanNodeVisibilityTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private ScanViewModel _scan = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-visibility", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _scan = _services.GetRequiredService<ScanViewModel>();
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _logging?.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [TestCase(ScanSortField.Size, true)]
    [TestCase(ScanSortField.Size, false)]
    [TestCase(ScanSortField.Name, false)]
    [TestCase(ScanSortField.FileCount, true)]
    [TestCase(ScanSortField.CreationDate, true)]
    [TestCase(ScanSortField.LastAccessTime, false)]
    public void Скрытие_и_возврат_крайних_элементов_совпадают_с_ReloadChildren_без_сброса_коллекции(ScanSortField field, bool invert)
    {
        _scan.Sort.State.Field = field;
        _scan.Sort.State.Invert = invert;

        var rootDomain = BuildFlatTree("flat", 600, 300, 300, 100);
        _scan.ApplyScanResult(rootDomain, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var rootNode = _scan.Roots[0];
        var collectionEvents = 0;
        rootNode.Children.CollectionChanged += (_, _) => collectionEvents++;

        var beforeNames = rootNode.Children.Select(static c => c.Name).ToArray();
        var first = rootNode.Children[0];
        var last = rootNode.Children[^1];
        _scan.SelectSingle(last);

        first.IgnoreCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectionEvents, Is.Zero);
            Assert.That(rootNode.Children.Select(static c => c.Name), Is.EqualTo(beforeNames));
            Assert.That(first.IsIgnored, Is.True);
            Assert.That(first.Fraction, Is.Zero);
            Assert.That(first.Share, Is.Zero);
            Assert.That(_scan.SelectedNode, Is.SameAs(last));
            Assert.That(last.IsSelected, Is.True);
        }

        var expectedAfterIgnore = SnapshotByReload(rootDomain);
        AssertSnapshotMatches(rootNode, expectedAfterIgnore);

        last.IgnoreCommand.Execute(null);
        Assert.That(collectionEvents, Is.Zero);
        AssertSnapshotMatches(rootNode, SnapshotByReload(rootDomain));

        first.UnignoreCommand.Execute(null);
        Assert.That(collectionEvents, Is.Zero);
        AssertSnapshotMatches(rootNode, SnapshotByReload(rootDomain));

        _scan.ResetIgnoredCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectionEvents, Is.Zero);
            Assert.That(rootNode.Children.Select(static c => c.Name), Is.EqualTo(beforeNames));
            Assert.That(_scan.SelectedNode, Is.SameAs(last));
            Assert.That(last.IsSelected, Is.True);
        }

        AssertSnapshotMatches(rootNode, SnapshotByReload(rootDomain));
    }

    [Test]
    public void Групповое_скрытие_и_возврат_сохраняют_мультивыбор_доли_и_порядок()
    {
        var dirPath = Path.Combine(_root, "batch");
        Directory.CreateDirectory(dirPath);
        var rootDomain = new DirectorySpace(dirPath, null, DateTime.Today, DateTime.Today);
        int[] sizes = [500, 400, 300, 200, 100];

        for (var i = 0; i < sizes.Length; i++)
        {
            var filePath = Path.Combine(dirPath, $"file-{i:D2}.bin");
            File.WriteAllBytes(filePath, new byte[sizes[i]]);
            rootDomain.AddFile(new(filePath));
        }

        _scan.ApplyScanResult(rootDomain, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var rootNode = _scan.Roots[0];

        var first = rootNode.Children[0];
        var third = rootNode.Children[2];
        var last = rootNode.Children[^1];

        _scan.SelectSingle(first);
        _scan.ToggleSelection(third);
        _scan.ToggleSelection(last);

        var collectionEvents = 0;
        rootNode.Children.CollectionChanged += (_, _) => collectionEvents++;

        _scan.IgnoreSelected();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectionEvents, Is.Zero);
            Assert.That(first.IsIgnored, Is.True);
            Assert.That(third.IsIgnored, Is.True);
            Assert.That(last.IsIgnored, Is.True);
            Assert.That(rootNode.Weight, Is.EqualTo(600));
            Assert.That(rootNode.Children[1].Fraction, Is.EqualTo(1));
            Assert.That(rootNode.Children[1].Share, Is.EqualTo(400d / 600).Within(0.0001));
            Assert.That(rootNode.Children[3].Fraction, Is.EqualTo(200d / 400).Within(0.0001));
            Assert.That(_scan.SelectedNodes, Is.EqualTo(new[] { first, third, last }));
            Assert.That(_scan.SelectedNode, Is.SameAs(last));
            Assert.That(first.IsSelected, Is.True);
            Assert.That(third.IsSelected, Is.True);
            Assert.That(last.IsSelected, Is.True);
        }

        _scan.UnignoreSelected();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectionEvents, Is.Zero);
            Assert.That(rootNode.Weight, Is.EqualTo(1500));
            Assert.That(first.IsIgnored, Is.False);
            Assert.That(third.IsIgnored, Is.False);
            Assert.That(last.IsIgnored, Is.False);
            Assert.That(first.Fraction, Is.EqualTo(1));
            Assert.That(rootNode.Children[1].Fraction, Is.EqualTo(400d / 500).Within(0.0001));
            Assert.That(_scan.SelectedNodes, Is.EqualTo(new[] { first, third, last }));
            Assert.That(_scan.SelectedNode, Is.SameAs(last));
        }
    }

    [Test]
    public void Скрытие_глубокого_потомка_пересчитывает_предка_и_не_раскрывает_свёрнутые_ветви()
    {
        var rootPath = Path.Combine(_root, "nested");
        var bigBranchPath = Path.Combine(rootPath, "big-branch");
        var deepPath = Path.Combine(bigBranchPath, "deep");
        var siblingBranchPath = Path.Combine(rootPath, "sibling-branch");
        var collapsedBranchPath = Path.Combine(rootPath, "collapsed-branch");
        var emptyBranchPath = Path.Combine(rootPath, "empty-branch");
        Directory.CreateDirectory(deepPath);
        Directory.CreateDirectory(siblingBranchPath);
        Directory.CreateDirectory(collapsedBranchPath);
        Directory.CreateDirectory(emptyBranchPath);

        File.WriteAllBytes(Path.Combine(deepPath, "heavy.bin"), new byte[900]);
        File.WriteAllBytes(Path.Combine(deepPath, "light.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(siblingBranchPath, "mid.bin"), new byte[400]);
        File.WriteAllBytes(Path.Combine(collapsedBranchPath, "hidden.bin"), new byte[50]);

        var rootDomain = new DiskSpaceCalculator().Calculate(new(rootPath));
        _scan.ApplyScanResult(rootDomain, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var rootNode = _scan.Roots[0];

        var bigBranch = rootNode.Children.Single(static c => c.Name == "big-branch");
        var siblingBranch = rootNode.Children.Single(static c => c.Name == "sibling-branch");
        var collapsedBranch = rootNode.Children.Single(static c => c.Name == "collapsed-branch");
        var emptyBranch = rootNode.Children.Single(static c => c.Name == "empty-branch");

        emptyBranch.EnsureLoaded();
        bigBranch.IsExpanded = true;
        var deepNode = bigBranch.Children.Single(static c => c.Name == "deep");
        deepNode.IsExpanded = true;
        var heavy = deepNode.Children.Single(static c => c.Name == "heavy.bin");

        var rootEvents = 0;
        var bigEvents = 0;
        var deepEvents = 0;
        rootNode.Children.CollectionChanged += (_, _) => rootEvents++;
        bigBranch.Children.CollectionChanged += (_, _) => bigEvents++;
        deepNode.Children.CollectionChanged += (_, _) => deepEvents++;

        heavy.IgnoreCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootEvents, Is.Zero);
            Assert.That(bigEvents, Is.Zero);
            Assert.That(deepEvents, Is.Zero);
            Assert.That(rootNode.Weight, Is.EqualTo(550));
            Assert.That(bigBranch.Weight, Is.EqualTo(100));
            Assert.That(siblingBranch.Fraction, Is.EqualTo(1));
            Assert.That(bigBranch.Fraction, Is.EqualTo(100d / 400).Within(0.0001));
            Assert.That(bigBranch.Share, Is.EqualTo(100d / 550).Within(0.0001));
            Assert.That(emptyBranch.Children, Is.Empty);
            Assert.That(collapsedBranch.Children, Has.Count.EqualTo(1));
            Assert.That(collapsedBranch.Children[0].Space, Is.Null);
        }

        collapsedBranch.IgnoreCommand.Execute(null);
        Assert.That(rootNode.Weight, Is.EqualTo(500));

        collapsedBranch.EnsureLoaded();
        Assert.That(collapsedBranch.Children[0].IsIgnored, Is.True);

        _scan.ResetIgnoredCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootNode.Weight, Is.EqualTo(1450));
            Assert.That(bigBranch.Fraction, Is.EqualTo(1));
            Assert.That(siblingBranch.Fraction, Is.EqualTo(400d / 1000).Within(0.0001));
            Assert.That(collapsedBranch.Children[0].IsIgnored, Is.False);
            Assert.That(collapsedBranch.Children[0].Fraction, Is.EqualTo(1));
        }
    }

    [Test]
    public void Карта_и_инспектор_сохраняют_порядок_равных_элементов_поверх_порога_плиток()
    {
        var dirPath = Path.Combine(_root, "ties");
        Directory.CreateDirectory(dirPath);
        var rootDomain = new DirectorySpace(dirPath, null, DateTime.Today, DateTime.Today);

        for (var i = 0; i < AppDefaults.TreemapTileLimit + 20; i++)
        {
            var filePath = Path.Combine(dirPath, $"f{i:D3}.bin");
            File.WriteAllBytes(filePath, new byte[100 + (i % 3)]);
            rootDomain.AddFile(new(filePath));
        }

        _scan.ApplyScanResult(rootDomain, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var rootNode = _scan.Roots[0];
        _scan.SelectSingle(rootNode);

        var expectedTiles = rootNode.Children
            .Where(static c => c.Weight > 0)
            .OrderByDescending(static c => c.Weight)
            .Take(AppDefaults.TreemapTileLimit)
            .ToArray();

        Assert.That(_scan.Treemap.TreemapTiles, Is.EqualTo(expectedTiles));

        rootNode.Children[0].IgnoreCommand.Execute(null);

        var expectedAfterIgnore = rootNode.Children
            .Where(static c => c.Weight > 0)
            .OrderByDescending(static c => c.Weight)
            .Take(AppDefaults.TreemapTileLimit)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.Treemap.TreemapTiles, Is.EqualTo(expectedAfterIgnore));
            Assert.That(_scan.Treemap.TreemapTruncated, Is.True);
            Assert.That(_scan.Inspector.TopChildren, Has.Count.EqualTo(AppDefaults.TopChildrenLimit));
            Assert.That(_scan.Inspector.MoreChildrenCount, Is.EqualTo(rootNode.Children.Count - 1 - AppDefaults.TopChildrenLimit));
        }
    }

    private DirectorySpace BuildFlatTree(string folderName, params int[] sizes)
    {
        var dirPath = Path.Combine(_root, folderName);
        Directory.CreateDirectory(dirPath);
        var root = new DirectorySpace(dirPath, null, DateTime.Today, DateTime.Today);

        for (var i = 0; i < sizes.Length; i++)
        {
            var subPath = Path.Combine(dirPath, $"dir-{i:D2}");
            Directory.CreateDirectory(subPath);
            var sub = new DirectorySpace($"dir-{i:D2}", root, DateTime.Today.AddDays(-i), DateTime.Today.AddHours(-i));

            for (var f = 0; f <= i; f++)
            {
                var filePath = Path.Combine(subPath, $"item-{f}.bin");
                File.WriteAllBytes(filePath, new byte[f == 0 ? sizes[i] : 1]);
                sub.AddFile(new(filePath));
            }

            root.Add(sub);
        }

        for (var i = 0; i < sizes.Length; i++)
        {
            var filePath = Path.Combine(dirPath, $"file-{i:D2}.bin");
            File.WriteAllBytes(filePath, new byte[sizes[i]]);
            root.AddFile(new(filePath));
        }

        return root;
    }

    private (string Name, bool IsIgnored, double Weight, double Fraction, double Share)[] SnapshotByReload(DirectorySpace rootDomain)
    {
        var fresh = _services.GetRequiredService<ScanNodeFactory>().CreateRoot(rootDomain, _scan.Sort.State);
        fresh.ReloadChildren();
        return fresh.Children
            .Select(static c => (c.Name, c.IsIgnored, c.Weight, c.Fraction, c.Share))
            .ToArray();
    }

    private static void AssertSnapshotMatches(
        ScanNodeViewModel parent,
        (string Name, bool IsIgnored, double Weight, double Fraction, double Share)[] expected)
    {
        Assert.That(parent.Children, Has.Count.EqualTo(expected.Length));

        for (var i = 0; i < expected.Length; i++)
        {
            var actual = parent.Children[i];
            var target = expected[i];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(actual.Name, Is.EqualTo(target.Name));
                Assert.That(actual.IsIgnored, Is.EqualTo(target.IsIgnored));
                Assert.That(actual.Weight, Is.EqualTo(target.Weight));
                Assert.That(actual.Fraction, Is.EqualTo(target.Fraction).Within(0.000001));
                Assert.That(actual.Share, Is.EqualTo(target.Share).Within(0.000001));
            }
        }
    }
}
