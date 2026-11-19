using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncStaleResultTests
{
    private string _root = string.Empty;
    private string _left = string.Empty;
    private string _right = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-stale", Guid.NewGuid().ToString("N"));
        _left = Path.Combine(_root, "left");
        _right = Path.Combine(_root, "right");
        Directory.CreateDirectory(_left);
        Directory.CreateDirectory(_right);
        File.WriteAllText(Path.Combine(_left, "a.txt"), "слева");
        File.WriteAllText(Path.Combine(_right, "a.txt"), "справа, длиннее");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine(exception.Message);
        }
    }

    [Test]
    public async Task История_git_прежней_загрузки_не_применяется_после_нового_чтения_тех_же_путей()
    {
        var git = await GitWithRepoAsync();
        var repo = TestContext.CurrentContext.TestDirectory;
        await git.ReadAsync(repo, repo, CancellationToken.None);

        var load = git.ToggleGitHistoryCommand.ExecuteAsync(null);
        await git.ToggleGitHistoryCommand.ExecuteAsync(null);
        var reread = git.ReadAsync(repo, repo, CancellationToken.None);
        await load;
        var logAfterStaleLoad = git.LeftGitLog.Count;
        await reread;

        Assert.That(logAfterStaleLoad, Is.Zero);
    }

    [Test]
    public async Task Перенос_сравнения_сбрасывает_отчёт_прежней_синхронизации()
    {
        var page = await ComparedPageAsync(SyncMode.LeftToRight);
        await ((ISyncAutomation)page).SyncFromAutomationAsync(CancellationToken.None);
        Assume.That(page.Operations.LastReport, Is.Not.Null);

        page.Operations.AdoptComparison(new(_left, _right, new(string.Empty, string.Empty)));

        Assert.That(page.Operations.LastReport, Is.Null);
    }

    [Test]
    public async Task Прежнее_чтение_git_не_перекрывает_следующее()
    {
        var git = await GitWithRepoAsync();
        var repo = TestContext.CurrentContext.TestDirectory;
        var missing = Path.Combine(_root, "нет");

        var first = git.ReadAsync(repo, repo, CancellationToken.None);
        var second = git.ReadAsync(missing, missing, CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.That(git.HasGit, Is.False);
    }

    [TestCase(true, SyncPlanFreshness.Applied)]
    [TestCase(false, SyncPlanFreshness.Fresh)]
    public async Task Синхронизация_во_время_переноса_сравнения_трогает_его_план_только_при_общем_корне(bool sameRoots, SyncPlanFreshness expected)
    {
        var page = await ComparedPageAsync(SyncMode.LeftToRight);
        var left = sameRoots ? _left : Directory.CreateDirectory(Path.Combine(_root, "другой-слева")).FullName;
        var right = sameRoots ? _right : Directory.CreateDirectory(Path.Combine(_root, "другой-справа")).FullName;
        var adopted = new ComparisonResult(left, right, new(string.Empty, string.Empty));
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Setup.LeftPath = left;
                page.Setup.RightPath = right;
                page.Operations.AdoptComparison(adopted);
            }
        };

        await ((ISyncAutomation)page).SyncFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.Result, Is.SameAs(adopted));
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(expected));
            Assert.That(page.Operations.LastReport, Is.Null);
            Assert.That(page.Operations.LastSyncElapsed, Is.EqualTo(TimeSpan.Zero));
        }
    }

    [Test]
    public async Task Состояние_git_не_возвращается_после_сброса_во_время_чтения()
    {
        var git = await GitWithRepoAsync();
        var repo = TestContext.CurrentContext.TestDirectory;

        var read = git.ReadAsync(repo, repo, CancellationToken.None);
        git.Clear();
        await read;

        Assert.That(git.HasGit, Is.False);
    }

    [Test]
    public async Task Сравнение_не_затирает_перенесённое_во_время_него()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = _left;
        automation.RightPath = _right;
        var adopted = new ComparisonResult(_left, _right, new(string.Empty, string.Empty));
        AdoptWhenBusy(page, adopted);

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assert.That(page.Operations.Result, Is.SameAs(adopted));
    }

    [Test]
    public async Task Хеши_прежнего_сравнения_не_применяются_к_перенесённому_во_время_хеширования()
    {
        var page = await ComparedPageAsync();
        var adopted = new ComparisonResult(_left, _right, new(string.Empty, string.Empty));
        AdoptWhenBusy(page, adopted);

        await page.Operations.HashCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.Result, Is.SameAs(adopted));
            Assert.That(page.Operations.HashesCompared, Is.False);
        }
    }

    private static async Task<SyncGitViewModel> GitWithRepoAsync()
    {
        var git = new SyncGitViewModel(new MemorySettings(), new NoopDialogs(), NullLogger.Instance);
        var repo = TestContext.CurrentContext.TestDirectory;

        await git.ReadAsync(repo, repo, CancellationToken.None);
        Assume.That(git.HasGit, Is.True, "каталог тестов не в git-репозитории или git недоступен");
        git.Clear();

        return git;
    }

    private static void AdoptWhenBusy(SyncViewModel page, ComparisonResult adopted)
    {
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Operations.AdoptComparison(adopted);
            }
        };
    }

    private async Task<SyncViewModel> ComparedPageAsync(SyncMode? mode = null)
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = _left;
        automation.RightPath = _right;

        if (mode is { } chosen)
        {
            automation.Mode = chosen;
        }

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assume.That(page.Operations.HashCommand.CanExecute(null), Is.True);

        return page;
    }

    private static SyncViewModel CreatePage()
    {
        var settings = new MemorySettings();
        var shell = new ShellPreferences(settings);

        return new(
            settings,
            new NoopDialogs(),
            new(settings),
            new(settings),
            NullLogger<SyncViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new(new(), shell),
            new(TestDiagnostics.Monitor()),
            new FakeFilePicker(),
            new FakeUiDispatcher(),
            new FakeAppNavigator());
    }
}
