using KeepShell.Testing;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanTreemapViewModelTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTreemap_{Guid.NewGuid():N}");
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

    private string _tempDir = null!;
    private ScanNodeFactory _factory = null!;

    [Test]
    public void Корень_карты_даёт_одну_крошку_и_плитки_по_убыванию_веса()
    {
        var (treemap, root) = Arrange();

        treemap.SetRoot(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapBreadcrumbs.Select(static crumb => crumb.Node.Name), Is.EqualTo(new[] { _tempDir }));
            Assert.That(treemap.TreemapBreadcrumbs[0].ShowSeparator, Is.False);
            Assert.That(treemap.TreemapTiles.Select(static tile => tile.Name), Is.EqualTo(new[] { "big", "mid", "small", "loose.bin" }));
            Assert.That(treemap.HasTreemapTiles, Is.True);
            Assert.That(treemap.TreemapTruncated, Is.False);
        }
    }

    [Test]
    public void Погружение_добавляет_крошку_с_разделителем_и_сообщает_о_выборе()
    {
        var (treemap, root) = Arrange();
        var drilled = new List<ScanNodeViewModel>();
        treemap.DrilledInto += drilled.Add;

        treemap.SetRoot(root);
        var big = treemap.TreemapTiles.First(static tile => tile.Name == "big");
        treemap.DrillIntoCommand.Execute(big);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapRoot, Is.SameAs(big));
            Assert.That(treemap.TreemapBreadcrumbs.Select(static crumb => crumb.Node.Name), Is.EqualTo(new[] { _tempDir, "big" }));
            Assert.That(treemap.TreemapBreadcrumbs[1].ShowSeparator, Is.True);
            Assert.That(drilled, Is.EqualTo(new[] { big }));
        }
    }

    [Test]
    public void Погружаться_некуда_ни_в_пустоту_ни_в_файл_ни_в_бездетный_каталог()
    {
        var (treemap, root) = Arrange();
        treemap.SetRoot(root);

        var file = root.Children.First(static child => !child.IsDirectory);
        var empty = root.Children.First(static child => child.IsDirectory && !child.HasChildren);

        foreach (var target in new ScanNodeViewModel?[] { null, file, empty })
        {
            treemap.DrillIntoCommand.Execute(target);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapRoot, Is.SameAs(root));
            Assert.That(treemap.TreemapBreadcrumbs, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Крошка_обрезает_путь_а_чужой_узел_ничего_не_меняет()
    {
        var (treemap, root) = Arrange();
        treemap.SetRoot(root);

        var big = treemap.TreemapTiles.First(static tile => tile.Name == "big");
        treemap.DrillIntoCommand.Execute(big);
        var deep = treemap.TreemapTiles.First(static tile => tile.Name == "deep");
        treemap.DrillIntoCommand.Execute(deep);
        Assert.That(treemap.TreemapBreadcrumbs, Has.Count.EqualTo(3));

        treemap.DrillToCrumbCommand.Execute(big);
        var outsider = deep.Children[0];
        treemap.DrillToCrumbCommand.Execute(outsider);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapRoot, Is.SameAs(big));
            Assert.That(treemap.TreemapBreadcrumbs.Select(static crumb => crumb.Node.Name), Is.EqualTo(new[] { _tempDir, "big" }));
        }
    }

    [Test]
    public void Удаление_ветки_поднимает_карту_к_уцелевшему_предку()
    {
        var (treemap, root) = Arrange();
        treemap.SetRoot(root);

        var big = treemap.TreemapTiles.First(static tile => tile.Name == "big");
        treemap.DrillIntoCommand.Execute(big);

        treemap.RefreshAfterDeletion([big.Space!]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapRoot, Is.SameAs(root));
            Assert.That(treemap.TreemapBreadcrumbs, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Удаление_корня_гасит_карту_когда_дерева_не_осталось()
    {
        var (treemap, root) = Arrange(withRoot: false);
        treemap.SetRoot(root);

        treemap.RefreshAfterDeletion([root.Space!]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapRoot, Is.Null);
            Assert.That(treemap.TreemapBreadcrumbs, Is.Empty);
            Assert.That(treemap.TreemapTiles, Is.Empty);
            Assert.That(treemap.HasTreemapTiles, Is.False);
        }
    }

    [Test]
    public void Плитки_обрезаются_до_предела_а_подпись_называет_остаток()
    {
        const int extra = 3;

        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);

        for (var i = 0; i < AppDefaults.TreemapTileLimit + extra; i++)
        {
            space.AddFile(MakeFile($"tile{i}", i + 1));
        }

        var root = _factory.CreateRoot(space, new());
        var treemap = new ScanTreemapViewModel([root]);

        treemap.SetRoot(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(treemap.TreemapTiles, Has.Count.EqualTo(AppDefaults.TreemapTileLimit));
            Assert.That(treemap.TreemapTruncated, Is.True);
            Assert.That(treemap.TreemapTruncatedText,
                Is.EqualTo($"Показаны крупнейшие {AppDefaults.TreemapTileLimit} из {AppDefaults.TreemapTileLimit + extra}"));
        }
    }

    private (ScanTreemapViewModel Treemap, ScanNodeViewModel Root) Arrange(bool withRoot = true)
    {
        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        space.AddFile(MakeFile("loose", 50));

        var big = MakeDir(space, "big", 3000);
        var deep = MakeDir(big, "deep", 1500);
        MakeDir(deep, "leaf", 700);
        MakeDir(space, "mid", 200);
        MakeDir(space, "small", 100);
        MakeDir(space, "empty", 0);

        var root = _factory.CreateRoot(space, new());
        root.EnsureLoaded();

        IReadOnlyList<ScanNodeViewModel> roots = withRoot ? [root] : [];

        return (new(roots), root);
    }

    private DirectorySpace MakeDir(DirectorySpace parent, string name, long size)
    {
        var dir = new DirectorySpace(name, parent, DateTime.Now, DateTime.Now);

        if (size > 0)
        {
            dir.AddFile(MakeFile($"{name}-payload", size));
        }

        parent.Add(dir);
        return dir;
    }

    private FileInfo MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, $"{name}.bin");
        File.WriteAllBytes(path, new byte[size]);

        return new(path);
    }
}
