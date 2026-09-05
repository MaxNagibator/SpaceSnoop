using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncProgressPollTests
{
    private const int Directories = 60;

    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSyncPoll_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_root, "right"));

        for (var index = 0; index < Directories; index++)
        {
            var directory = Path.Combine(_root, "left", $"каталог-{index:D3}");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "файл.txt"), "данные");
        }
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
    public async Task Экран_сравнения_обновляется_тиком_таймера_а_не_каждым_каталогом()
    {
        var dispatcher = new FakeUiDispatcher();
        var page = CreatePage(dispatcher);
        var updates = 0;

        page.Session.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(page.Session.ProgressDetail))
            {
                updates++;
            }
        };

        await CompareAsync(page);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updates, Is.EqualTo(2), $"Экран обновился {updates} раз на {Directories} каталогах – прогресс снова пушится на каждый объект.");
            Assert.That(page.Session.ProgressDetail, Does.StartWith($"{SyncOperationsViewModel.CompareCaption} {Directories + 1}"), "Последний срез прогресса до экрана не доехал.");
        }
    }

    [Test]
    public async Task Таймер_опроса_заводится_один_и_останавливается_после_сравнения()
    {
        var dispatcher = new FakeUiDispatcher();
        var page = CreatePage(dispatcher);

        await CompareAsync(page);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].Interval, Is.EqualTo(SyncSessionViewModel.ProgressPollInterval));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
        }
    }

    [Test]
    public async Task Перенос_файлов_опросом_не_накрывается()
    {
        var dispatcher = new FakeUiDispatcher();
        var page = CreatePage(dispatcher);
        var automation = (ISyncAutomation)page;

        await CompareAsync(page);

        var afterCompare = dispatcher.Timers[0].Starts;

        await automation.SyncFromAutomationAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(afterCompare, Is.EqualTo(1), "Сравнение не завело опрос.");
            Assert.That(dispatcher.Timers[0].Starts, Is.EqualTo(1), "Перенос коалесцирует отчёты сам – второго дросселя ему не надо.");
            Assert.That(File.Exists(Path.Combine(_root, "right", "каталог-000", "файл.txt")), Is.True);
        }
    }

    private async Task CompareAsync(SyncViewModel page)
    {
        var automation = (ISyncAutomation)page;
        automation.LeftPath = Path.Combine(_root, "left");
        automation.RightPath = Path.Combine(_root, "right");
        automation.Mode = SyncMode.LeftToRight;

        await automation.CompareFromAutomationAsync(CancellationToken.None);
    }

    private static SyncViewModel CreatePage(FakeUiDispatcher dispatcher)
    {
        var settings = new MemorySettings();
        var shell = new ShellPreferences(settings);

        return new(settings,
            new NoopDialogs(),
            new OperationPreferences(settings),
            new AgentPreferences(settings),
            NullLogger<SyncViewModel>.Instance,
            new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance),
            new ExecuteSyncUseCase(NullLogger<SyncEngine>.Instance),
            new ToastNotifier(new ToastHostViewModel(), shell),
            new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance),
            new PerformanceRunTracker(),
            new FakeFilePicker(),
            dispatcher,
            new FakeAppNavigator());
    }
}
