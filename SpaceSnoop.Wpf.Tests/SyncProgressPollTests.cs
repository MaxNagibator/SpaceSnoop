using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Sync;

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

    [Test]
    public async Task Таймер_опроса_заводится_один_и_останавливается_после_сравнения()
    {
        var dispatcher = new FakeUiDispatcher();
        var page = CreatePage(dispatcher);

        await CompareAsync(page);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].Interval, Is.EqualTo(UiProgressPoller.DefaultInterval));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
        }
    }

    [Test]
    public async Task Таймер_опроса_останавливается_после_отмены()
    {
        var dispatcher = new FakeUiDispatcher();
        var page = CreatePage(dispatcher);
        using var cancel = new CancellationTokenSource();

        await page.Session.RunAsync<object>(
            "Синхронизация:",
            (token, _) =>
            {
                cancel.Cancel();
                token.ThrowIfCancellationRequested();
                return new();
            },
            external: cancel.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(page.Session.LastOperationCancelled, Is.True);
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил отмену.");
        }
    }

    [Test]
    public async Task Сравнение_объединяет_обновления_прогресса_и_показывает_полный_счёт()
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
            Assert.That(updates, Is.Positive.And.LessThan(Directories / 2), $"Экран обновился {updates} раз на {Directories} каталогах.");
            Assert.That(page.Session.ProgressDetail, Does.StartWith("Сравнение каталогов: 61"), "Последний срез прогресса до экрана не доехал.");
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
            dispatcher,
            new FakeAppNavigator());
    }
}
