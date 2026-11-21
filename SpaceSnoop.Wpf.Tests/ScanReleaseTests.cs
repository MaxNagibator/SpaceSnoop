using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanReleaseTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private ScanViewModel _scan = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-release", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "target"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));

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

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог стенда {_root}: {exception.Message}");
        }
    }

    [Test]
    public void Завершение_кадра_можно_запросить_с_фонового_потока()
    {
        var operations = _services.GetRequiredService<PerformanceOperations>();
        var app = Application.Current;
        var previousWindow = app.MainWindow;
        using var host = VisualTestHost.Show(new(), 800, 600);
        app.MainWindow = host.Window;

        try
        {
            Assert.That(host.Window.IsVisible, Is.True);
            operations.ReportRun(new("Ожидание кадра", 0, 0, TimeSpan.Zero));
            var queued = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var background = Task.Run(() =>
            {
                try
                {
                    var completion = operations.FlushFramesAsync();
                    queued.SetResult(completion);
                    return completion;
                }
                catch (Exception exception)
                {
                    queued.SetException(exception);
                    throw;
                }
            });

            try
            {
                Assert.That(queued.Task.Wait(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(queued.Task.Result.IsCompleted, Is.False);
            }
            finally
            {
                host.Settle();
                Assert.That(background.Wait(TimeSpan.FromSeconds(5)), Is.True);
            }

            Assert.That(operations.Current, Is.Null);
        }
        finally
        {
            app.MainWindow = previousWindow;
        }
    }

    [Test]
    public async Task Ожидание_кадра_не_ждёт_свёрнутое_главное_окно()
    {
        var operations = _services.GetRequiredService<PerformanceOperations>();
        var app = Application.Current;
        var previousWindow = app.MainWindow;
        using var host = VisualTestHost.Show(new(), 800, 600);
        host.Window.WindowState = WindowState.Minimized;
        app.MainWindow = host.Window;

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var waited = await operations.WaitForRenderedFrameAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            stopwatch.Stop();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(waited, Is.False);
                Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromMilliseconds(500)));
            }
        }
        finally
        {
            app.MainWindow = previousWindow;
        }
    }

    [Test]
    public void Игнорирование_всех_детей_даёт_пустую_карту_и_сохраняет_узлы_дерева()
    {
        var root = ArrangeDisplayTree();
        foreach (var child in root.Children.ToArray())
        {
            child.IgnoreCommand.Execute(null);
        }

        Assert.That(root.Weight, Is.Zero);
        Assert.That(_scan.Treemap.HasTreemapTiles, Is.False);
        Assert.That(root.Children, Has.Count.EqualTo(2));
        Assert.That(root.Children.All(static child => child.IsIgnored), Is.True);
        root.IgnoreCommand.Execute(null);
        Assert.That(_scan.Roots, Has.Count.EqualTo(1));
        _scan.ResetIgnoredCommand.Execute(null);
        Assert.That(_scan.Treemap.HasTreemapTiles, Is.True);
        Assert.That(root.Weight, Is.EqualTo(1200));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Игнорирование_и_возврат_не_обновляют_потомков_соседней_загруженной_ветви(bool otherRoot)
    {
        var root = ArrangeDisplayTree();
        var domain = (DirectorySpace)root.Space!;
        var unrelatedRoot = otherRoot
            ? new(Path.Combine(_root, "other"), null, DateTime.Today, DateTime.Today)
            : domain;

        var branch = new DirectorySpace("unrelated", unrelatedRoot, DateTime.Today, DateTime.Today);
        var sample = new FileInfo(Path.Combine(_root, "target", "other.bin"));
        branch.AddFiles(Enumerable.Repeat(sample, 1000).ToArray().AsSpan());
        unrelatedRoot.Add(branch);
        ScanNodeViewModel unrelatedRootVm;

        if (otherRoot)
        {
            _scan.ApplyScanResult(unrelatedRoot, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
            unrelatedRootVm = _scan.Roots[0];
        }
        else
        {
            root.ReloadChildren();
            unrelatedRootVm = root;
        }

        var unrelated = unrelatedRootVm.Children.Single(static child => child.Name == "unrelated");
        unrelated.EnsureLoaded();
        _scan.SelectedNode = unrelated;
        _scan.Treemap.SetRoot(unrelatedRootVm);
        _scan.Treemap.DrillIntoCommand.Execute(unrelated);
        var events = 0;
        unrelated.Children.CollectionChanged += (_, _) => events++;
        unrelated.Children[0].PropertyChanged += (_, _) => events++;
        _scan.Inspector.TopChildren.CollectionChanged += (_, _) => events++;
        _scan.Treemap.TreemapTiles.CollectionChanged += (_, _) => events++;
        var beforeRootShare = _scan.Inspector.ShareOfRoot;
        var changed = root.Children.Single(static child => child.Name == "branch");
        changed.EnsureLoaded();
        changed.Children[0].IgnoreCommand.Execute(null);

        Assert.That(events, Is.Zero);
        Assert.That(_scan.Inspector.ShareOfRoot, otherRoot ? Is.EqualTo(beforeRootShare) : Is.GreaterThan(beforeRootShare));
        _scan.ResetIgnoredCommand.Execute(null);
        Assert.That(events, Is.Zero);
        Assert.That(_scan.Inspector.ShareOfRoot, Is.EqualTo(beforeRootShare));
    }

    [TestCase(ScanSortField.Size, true)]
    [TestCase(ScanSortField.Size, false)]
    [TestCase(ScanSortField.FileCount, true)]
    [TestCase(ScanSortField.FileCount, false)]
    public void Игнорирование_и_снятие_пометки_сохраняют_порядок_папок_и_файлов(ScanSortField field, bool invert)
    {
        var root = ArrangeDisplayTree();
        var domain = (DirectorySpace)root.Space!;
        var other = new DirectorySpace("other", domain, DateTime.Today, DateTime.Today);
        other.AddFiles(
            new[]
            {
                new FileInfo(Path.Combine(_root, "target", "other.bin")),
            }.AsSpan());

        domain.Add(other);
        _scan.Sort.State.Field = field;
        _scan.Sort.State.Invert = invert;
        root.ReloadChildren();
        var branch = root.Children.Single(static child => child.Name == "branch");
        branch.EnsureLoaded();
        var beforeFolders = root.Children.ToArray();
        var beforeFiles = branch.Children.ToArray();

        branch.IgnoreCommand.Execute(null);
        Assert.That(root.Children, Is.EqualTo(beforeFolders));
        branch.UnignoreCommand.Execute(null);
        Assert.That(root.Children, Is.EqualTo(beforeFolders));

        var large = branch.Children.Single(static child => child.Name == "large.bin");
        large.IgnoreCommand.Execute(null);
        Assert.That(root.Children, Is.EqualTo(beforeFolders));
        Assert.That(branch.Children, Is.EqualTo(beforeFiles));
        large.UnignoreCommand.Execute(null);
        Assert.That(root.Children, Is.EqualTo(beforeFolders));
        Assert.That(branch.Children, Is.EqualTo(beforeFiles));
    }

    [Test]
    public void Игнорирование_папки_после_её_файла_не_вычитает_размер_дважды_и_возвращает_карту_к_родителю()
    {
        var root = ArrangeDisplayTree();
        var branch = root.Children.Single(static node => node.Name == "branch");
        branch.IsExpanded = true;
        _scan.Treemap.DrillIntoCommand.Execute(branch);
        branch.Children[0].IgnoreCommand.Execute(null);
        branch.IgnoreCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Weight, Is.EqualTo(200));
            Assert.That(root.Children, Does.Contain(branch));
            Assert.That(branch.IsIgnored, Is.True);
            Assert.That(branch.Children.All(static child => child.IsIgnored), Is.True);
            Assert.That(_scan.Treemap.TreemapRoot, Is.SameAs(root));
            Assert.That(_scan.SelectedNode, Is.SameAs(branch));
            Assert.That(_scan.ResetIgnoredCaption, Is.EqualTo("Снять игнорирование (1)"));
        }

        _scan.ResetIgnoredCommand.Execute(null);
        Assert.That(root.Weight, Is.EqualTo(1200));
        Assert.That(root.Children, Has.Count.EqualTo(2));
        Assert.That(_scan.HasIgnoredItems, Is.False);
        Assert.That(branch.Children.All(static child => !child.IsIgnored), Is.True);
    }

    [Test]
    public void Игнорирование_сохраняет_пометки_на_удаление_а_удаление_родителя_сбрасывает_игнорирование()
    {
        var root = ArrangeDisplayTree();
        var branch = root.Children.Single(static node => node.Name == "branch");
        branch.IsExpanded = true;
        var large = branch.Children.Single(static node => node.Name == "large.bin");
        large.MarkDeletedCommand.Execute(null);
        large.IgnoreCommand.Execute(null);
        Assert.That(_scan.MarkedCount, Is.EqualTo(1));
        _scan.Marks.ApplyDeletionResult([branch.Space!]);

        Assert.That(_scan.HasIgnoredItems, Is.False);
        Assert.That(root.Weight, Is.EqualTo(200));
        _scan.ResetIgnoredCommand.Execute(null);
        Assert.That(
            root.Children.Select(static node => node.Name),
            Is.EqualTo(
                new[]
                {
                    "other.bin",
                }));
    }

    [Test]
    public void Игнорирование_файла_пересчитывает_дерево_карту_и_инспектор_без_изменения_скана()
    {
        var root = ArrangeDisplayTree();
        var branch = root.Children.Single(static node => node.Name == "branch");
        branch.IsExpanded = true;
        var large = branch.Children.Single(static node => node.Name == "large.bin");
        var small = branch.Children.Single(static node => node.Name == "small.bin");
        _scan.SelectedNode = branch;
        large.IgnoreCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                branch.Children.Select(static node => node.Name),
                Is.EqualTo(
                    new[]
                    {
                        "large.bin", "small.bin",
                    }));

            Assert.That(large.IsIgnored, Is.True);
            Assert.That(large.Fraction, Is.Zero);
            Assert.That(large.ShareText, Is.EqualTo("–"));
            Assert.That(large.SizeText, Is.EqualTo(SizeFormatter.Format(900)));
            Assert.That(branch.IsExpanded, Is.True);
            Assert.That(small.Fraction, Is.EqualTo(1));
            Assert.That(small.Share, Is.EqualTo(1));
            Assert.That(root.Weight, Is.EqualTo(300));
            Assert.That(branch.Weight, Is.EqualTo(100));
            Assert.That(branch.Share, Is.EqualTo(1d / 3).Within(0.0001));
            Assert.That(
                _scan.Treemap.TreemapTiles.Select(static node => node.Weight),
                Is.EqualTo(
                    new[]
                    {
                        200d, 100d,
                    }));

            Assert.That(_scan.Inspector.TotalSizeText, Is.EqualTo(branch.SizeText));
            Assert.That(_scan.Inspector.OwnSizeText, Is.EqualTo(SizeFormatter.Format(100)));
            Assert.That(
                _scan.Inspector.TopChildren.Select(static node => node.Name),
                Is.EqualTo(
                    new[]
                    {
                        "small.bin",
                    }));

            Assert.That(root.Space!.TotalSize, Is.EqualTo(1200));
            Assert.That(((DirectorySpace)root.Space).TotalFileCount, Is.EqualTo(3));
            Assert.That(root.FileCountText, Is.EqualTo("2"));
            Assert.That(File.Exists(large.AbsolutePath), Is.True);
        }
    }

    [TestCase(false, true, TestName = "Повторный скан папки с другим регистром имени снимает прежний корень в обычном каталоге")]
    [TestCase(true, false, TestName = "Повторный скан папки с другим регистром имени не трогает соседний корень в регистрозависимом каталоге")]
    public void Корень_с_другим_регистром_имени(bool caseSensitive, bool sameRoot)
    {
        var parent = Path.Combine(_root, "cased");
        PrepareCase(parent, caseSensitive);
        Directory.CreateDirectory(Path.Combine(parent, "Foo"));

        if (caseSensitive)
        {
            Directory.CreateDirectory(Path.Combine(parent, "foo"));
        }

        var upper = Tree(Path.Combine(parent, "Foo"), "branch");
        _scan.ApplyScanResult(upper, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        var lowerPath = Path.Combine(parent, "foo");

        var releasable = ScanTreeEditor.HasReleasableRoot(_scan.Roots, lowerPath, false);
        _scan.ApplyScanResult(Tree(lowerPath, "branch"), TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(releasable, Is.EqualTo(sameRoot));
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, upper)), Is.EqualTo(!sameRoot));
        }
    }

    [Test]
    public void Непомечаемый_узел_нового_дерева_не_попадает_в_счётчик_перенесённых_пометок()
    {
        var previous = Arrange("target");
        _scan.MarkForAutomation([previous.SubDirectories.Single()], true);
        var fresh = Tree(previous.AbsolutePath, "branch");
        fresh.SubDirectories.Single().Error();

        _scan.ApplyScanResult(fresh, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.Marks.MarkedCount, Is.Zero);
            Assert.That(_scan.Marks.MarkedSizeText, Is.Empty);
            Assert.That(_scan.Marks.HasMarked, Is.False);
            Assert.That(fresh.SubDirectories.Single().IsDeleted, Is.False);
        }
    }

    [TestCase(true, false, false, TestName = "Включённая настройка снимает прежний результат папки до скана")]
    [TestCase(true, true, true, TestName = "Прежний результат с пометками остаётся при включённой настройке")]
    [TestCase(false, false, true, TestName = "Выключенная настройка оставляет прежний результат до конца скана")]
    [TestCase(false, true, true, TestName = "Пометки переживают отменённый скан при выключенной настройке")]
    public async Task Отменённый_повторный_скан(bool release, bool marked, bool kept)
    {
        var other = Arrange("other");
        var target = Arrange("target");
        var branch = target.SubDirectories.Single();
        _scan.Preferences.ReleaseBeforeRescan = release;

        if (marked)
        {
            _scan.MarkForAutomation([branch], true);
        }

        await ScanPageNoticeTests.CancelWhenTraversalBeginsAsync(_scan, target.AbsolutePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.True);
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, other)), Is.True);
            Assert.That(_scan.Roots.Any(root => ReferenceEquals(root.Space, target)), Is.EqualTo(kept));
            Assert.That(_scan.HasResult, Is.EqualTo(kept));
            Assert.That(branch.IsDeleted, Is.EqualTo(marked));
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(marked ? 1 : 0));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Перенесённые_пометки_не_считают_потомка_помеченного_корня_повторно(bool rootFirst)
    {
        var previous = Arrange("target");
        _scan.MarkForAutomation([previous.SubDirectories.Single()], true);
        var branchRoot = _scan.Roots.Single();
        var previousWhole = Tree(previous.AbsolutePath, "branch");
        _scan.ApplyScanResult(previousWhole, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        _scan.MarkForAutomation([previousWhole], true);
        var wholeRoot = _scan.Roots.Single();
        var fresh = Tree(previous.AbsolutePath, "branch");

        var result = ScanTreeEditor.TransferMarks(rootFirst ? [wholeRoot, branchRoot] : [branchRoot, wholeRoot], fresh, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Transferred,
                Is.EqualTo(
                    new[]
                    {
                        fresh,
                    }));

            Assert.That(result.Lost, Is.Zero);
            Assert.That(fresh.SubDirectories.Single().IsDeleted, Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Пересчёт_после_переноса_учитывает_новые_размеры_и_пометки_другого_корня(bool wholeRoot)
    {
        var other = Arrange("other");
        var otherBranch = other.SubDirectories.Single();
        AddSizedFile(otherBranch, "other.bin", 19);
        var previous = Arrange("target");
        AddSizedFile(previous.SubDirectories.Single(), "inside.bin", 1);
        AddSizedFile(previous, "loose.bin", 1);
        _scan.MarkForAutomation(
            wholeRoot
                ? [otherBranch, previous]
                : [otherBranch, previous.SubDirectories.Single(), previous.Files.Single()],
            true);

        var fresh = Tree(previous.AbsolutePath, "branch");
        AddSizedFile(fresh.SubDirectories.Single(), "inside.bin", 37);
        AddSizedFile(fresh, "loose.bin", 11);
        AddSizedFile(fresh, "plain.bin", 99);
        _scan.ApplyScanResult(fresh, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(wholeRoot ? 2 : 3));
            Assert.That(_scan.Marks.MarkedSizeText, Is.EqualTo(SizeFormatter.Format(wholeRoot ? 166 : 67)));
            Assert.That(_scan.Marks.HasMarked, Is.True);
            Assert.That(otherBranch.IsDeleted, Is.True);
            Assert.That(fresh.SubDirectories.Single().Files.Single().IsDeleted, Is.True);
            Assert.That(Child(fresh, "plain.bin").IsDeleted, Is.EqualTo(wholeRoot));
        }
    }

    [TestCase(true, TestName = "Повторный скан переносит пометки на новое дерево и отбрасывает пропавшие пути при включённой настройке")]
    [TestCase(false, TestName = "Повторный скан переносит пометки на новое дерево и отбрасывает пропавшие пути при выключенной настройке")]
    public async Task Повторный_скан_переносит_пометки(bool release)
    {
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(Path.Combine(target, "branch", "leaf"));
        await File.WriteAllTextAsync(Path.Combine(target, "keep.bin"), "keep");
        await File.WriteAllTextAsync(Path.Combine(target, "gone.bin"), "gone");
        await File.WriteAllTextAsync(Path.Combine(target, "plain.bin"), "plain");
        _scan.Preferences.ReleaseBeforeRescan = release;
        var toasts = _services.GetRequiredService<ToastHostViewModel>();

        await SettleDriveLabelsAsync();
        _scan.SelectPathForAutomation(target);
        await SettleDriveLabelsAsync();

        await _scan.ScanFromAutomationAsync(target, CancellationToken.None);
        await SettleDriveLabelsAsync();
        var previous = (DirectorySpace)_scan.Roots.Single().Space!;
        _scan.MarkForAutomation([Child(previous, "branch"), Child(previous, "keep.bin"), Child(previous, "gone.bin")], true);
        File.Delete(Path.Combine(target, "gone.bin"));

        await _scan.ScanFromAutomationAsync(target, CancellationToken.None);
        await SettleDriveLabelsAsync();
        var fresh = (DirectorySpace)_scan.Roots.Single().Space!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh, Is.Not.SameAs(previous));
            Assert.That(Child(fresh, "branch").IsDeleted, Is.True);
            Assert.That(Child((DirectorySpace)Child(fresh, "branch"), "leaf").IsDeleted, Is.True);
            Assert.That(Child(fresh, "keep.bin").IsDeleted, Is.True);
            Assert.That(Child(fresh, "plain.bin").IsDeleted, Is.False);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(2));
            Assert.That(toasts.Toasts.Select(static toast => toast.Message), Has.Some.StartsWith("Не перенесено пометок на удаление: 1"));
        }
    }

    [Test]
    public void Повторный_скан_сбрасывает_игнорирование_заменённого_дерева()
    {
        var root = ArrangeDisplayTree();
        root.Children[0].IgnoreCommand.Execute(null);
        var fresh = new DiskSpaceCalculator().Calculate(new(root.AbsolutePath));
        _scan.ApplyScanResult(fresh, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        Assert.That(_scan.HasIgnoredItems, Is.False);
        Assert.That(_scan.Roots[0].Weight, Is.EqualTo(1200));
    }

    [TestCase(false, true, TestName = "Пометка переходит на узел, переименованный только регистром, в обычном каталоге")]
    [TestCase(true, false, TestName = "Пометка не переходит на узел, переименованный только регистром, в регистрозависимом каталоге")]
    public void Пометка_при_смене_регистра_имени(bool caseSensitive, bool transferred)
    {
        var target = Path.Combine(_root, "target");
        PrepareCase(target, caseSensitive);
        Directory.CreateDirectory(Path.Combine(target, "branch"));

        var previous = Tree(target, "Branch");
        _scan.ApplyScanResult(previous, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        _scan.MarkForAutomation([previous.SubDirectories.Single()], true);

        var fresh = Tree(target, "branch");
        _scan.ApplyScanResult(fresh, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fresh.SubDirectories.Single().IsDeleted, Is.EqualTo(transferred));
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(transferred ? 1 : 0));
        }
    }

    [TestCase(false, true, TestName = "Поиск узла и список целей сливают имена, различающиеся регистром, в обычном каталоге")]
    [TestCase(true, false, TestName = "Поиск узла и список целей различают имена, различающиеся регистром, в регистрозависимом каталоге")]
    public void Регистр_в_поиске_узла_и_списке_целей(bool caseSensitive, bool merged)
    {
        var parent = Path.Combine(_root, "cased");
        PrepareCase(parent, caseSensitive);
        Directory.CreateDirectory(Path.Combine(parent, "Foo"));
        var lowerPath = Path.Combine(parent, "foo");
        var root = Tree(parent, "Foo");

        var catalog = new DriveCatalog(NullLogger.Instance);
        catalog.AddDrive(Path.Combine(parent, "Foo"));
        catalog.AddDrive(lowerPath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanLookup.Find([root], lowerPath) is not null, Is.EqualTo(merged));
            Assert.That(catalog.RecentDirectories, Has.Count.EqualTo(merged ? 1 : 2));
        }
    }

    [Test]
    public void Снятие_пометки_у_унаследованного_потомка_явно_возвращает_его_папку_и_сохраняет_узлы()
    {
        var root = ArrangeDisplayTree();
        var branch = root.Children.Single(static child => child.Name == "branch");
        branch.IsExpanded = true;
        var large = branch.Children.Single(static child => child.Name == "large.bin");
        _scan.SelectedNode = large;
        branch.IgnoreCommand.Execute(null);

        Assert.That(large.IsIgnored, Is.True);
        Assert.That(large.CanIgnore, Is.False);
        Assert.That(large.UnignoreCaption, Is.EqualTo("Снять игнорирование папки «branch»"));
        Assert.That(_scan.SelectedNode, Is.SameAs(large));
        large.UnignoreCommand.Execute(null);

        Assert.That(branch.IsIgnored, Is.False);
        Assert.That(large.IsIgnored, Is.False);
        Assert.That(branch.Children.Single(static child => child.Name == "large.bin"), Is.SameAs(large));
        Assert.That(root.Weight, Is.EqualTo(1200));
        Assert.That(_scan.HasIgnoredItems, Is.False);
    }

    private async Task SettleDriveLabelsAsync()
    {
        await Task.WhenAll(_scan.Drives.Items.Select(static drive => drive.LoadAsync()));
    }

    private static void AddSizedFile(DirectorySpace directory, string name, int bytes)
    {
        Directory.CreateDirectory(directory.AbsolutePath);
        var path = Path.Combine(directory.AbsolutePath, name);
        File.WriteAllBytes(path, new byte[bytes]);
        directory.AddFile(new(path));
    }

    private ScanNodeViewModel ArrangeDisplayTree()
    {
        var path = Path.Combine(_root, "target");
        var branch = Path.Combine(path, "branch");
        Directory.CreateDirectory(branch);
        File.WriteAllBytes(Path.Combine(branch, "large.bin"), new byte[900]);
        File.WriteAllBytes(Path.Combine(branch, "small.bin"), new byte[100]);
        File.WriteAllBytes(Path.Combine(path, "other.bin"), new byte[200]);
        var space = new DiskSpaceCalculator().Calculate(new(path));
        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        return _scan.Roots[0];
    }

    private static SpaceBase Child(DirectorySpace dir, string name)
    {
        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files).Single(child => child.Name == name);
    }

    private static DirectorySpace Tree(string path, string branch)
    {
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new(branch, space, DateTime.Now, DateTime.Now));

        return space;
    }

    private static void PrepareCase(string directory, bool caseSensitive)
    {
        Directory.CreateDirectory(directory);

        if (caseSensitive)
        {
            Assume.That(TrySetCaseSensitive(directory), "fsutil не включает регистрозависимость каталога на этой машине");
        }
    }

    private static bool TrySetCaseSensitive(string directory)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo("fsutil", ["file", "setCaseSensitiveInfo", directory, "enable"])
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private DirectorySpace Arrange(string name)
    {
        var path = Path.Combine(_root, name);
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new("branch", space, DateTime.Now, DateTime.Now));

        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        return space;
    }
}
