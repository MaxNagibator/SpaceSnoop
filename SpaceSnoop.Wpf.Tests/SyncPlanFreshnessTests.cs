using KeepShell.Testing;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class SyncPlanFreshnessTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-freshness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "left"));
        Directory.CreateDirectory(Path.Combine(_root, "right"));
        File.WriteAllText(Path.Combine(_root, "left", "a.txt"), "данные");
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
    public async Task Исполненный_план_повторно_не_запускается()
    {
        var page = await RunSyncAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "right", "a.txt")), Is.True);
        }

        await Assert.ThrowsAsync<SyncPlanStaleException>(() => ((ISyncAutomation)page).SyncFromAutomationAsync(CancellationToken.None));
    }

    [Test]
    public async Task Новое_сравнение_возвращает_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        await File.WriteAllTextAsync(Path.Combine(_root, "left", "b.txt"), "ещё данные");
        await ((ISyncAutomation)page).CompareFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Fresh));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.True);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task План_сменившийся_за_замер_места_не_доходит_до_подтверждения(bool replaceResult)
    {
        using var reader = new BlockingVolumeReader(1_000_000);
        var compare = new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance);
        var request = new CompareDirectoriesRequest(Path.Combine(_root, "left"), Path.Combine(_root, "right"), string.Empty, SyncMode.LeftToRight, SyncWinner.Newest, false);
        var result = compare.Execute(request, CancellationToken.None);
        var ledger = new SyncLedgerViewModel(
            new(new MemorySettings(), new NoopDialogs(), NullLogger.Instance),
            () => new(PackIconLucideKind.ArrowRight, "слева направо", false));

        ledger.Update(result, false);

        Assume.That(result.CountPlannedActions().RequiredRightBytes, Is.GreaterThan(0));

        var confirmation = ledger.BuildSyncConfirmationAsync(result, new("Сверить хеши", ConfirmChoiceKind.Secondary));
        reader.WaitEntered();

        if (replaceResult)
        {
            ledger.Update(compare.Execute(request, CancellationToken.None), false);
        }
        else
        {
            result.Root.Files.Single().Action = SyncAction.Skip;
        }

        reader.Release();

        Assert.That(await confirmation, Is.Null);
    }

    [Test]
    public async Task Правка_исключений_во_время_синхронизации_сбрасывает_сравнение_только_по_её_завершении()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        automation.Mode = SyncMode.LeftToRight;

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        var keptWhileBusy = false;
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Setup.Exclusions = "*.txt";
                keptWhileBusy = page.Operations.Result is not null;
            }
        };

        var run = await automation.SyncFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(keptWhileBusy, Is.True);
            Assert.That(run?.Report.SuccessCount, Is.EqualTo(1));
            Assert.That(page.Operations.Result, Is.Null);
        }
    }

    [Test]
    public async Task Правка_исключений_во_время_сравнения_отбрасывает_его_результат()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        page.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SyncSessionViewModel.IsBusy) && page.Session.IsBusy)
            {
                page.Setup.Exclusions = "*.txt";
            }
        };

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assert.That(page.Operations.Result, Is.Null);
    }

    [Test]
    public async Task Смена_режима_не_возвращает_исполненному_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        page.Operations.ReapplyMode();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public async Task Хеширование_не_возвращает_исполненному_плану_исполнимость()
    {
        var page = await RunSyncAsync();

        await page.Operations.HashCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Operations.PlanFreshness, Is.EqualTo(SyncPlanFreshness.Applied));
            Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.False);
        }
    }

    private async Task<SyncViewModel> RunSyncAsync()
    {
        var page = CreatePage();
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        automation.Mode = SyncMode.LeftToRight;

        await automation.CompareFromAutomationAsync(CancellationToken.None);

        Assert.That(page.Operations.SyncCommand.CanExecute(null), Is.True);

        await automation.SyncFromAutomationAsync(CancellationToken.None);

        return page;
    }

    private static SyncViewModel CreatePage()
    {
        var settings = new MemorySettings();
        var dialogs = new NoopDialogs();
        var shell = new ShellPreferences(settings);

        return new(
            settings,
            dialogs,
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
