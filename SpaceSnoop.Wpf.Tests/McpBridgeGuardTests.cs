using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class McpBridgeGuardTests
{
    private const string MutationsBlocked = "Изменяющие операции запрещены";
    private const string RunnableTargetId = "TempFiles";

    private string _root = string.Empty;
    private McpPreferences _preferences = null!;
    private ScanAutomationDouble _scan = null!;
    private SyncAutomationDouble _sync = null!;
    private CleanupAutomationDouble _cleanup = null!;
    private PerformanceMonitor _monitor = null!;
    private McpBridge _bridge = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_guard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "файл.txt"), "содержимое");

        var settings = new MemorySettings();
        _preferences = new(settings);
        _scan = new();
        _sync = new();
        _cleanup = new();
        _monitor = new(NullLogger<PerformanceMonitor>.Instance);

        var scanPreferences = new ScanPreferences(settings);

        _bridge = new(settings,
            _sync,
            _scan,
            _preferences,
            scanPreferences,
            new ScanRunner(new(), new(), scanPreferences, NullLogger<ScanRunner>.Instance),
            new DuplicateFinder(),
            new DockerService(),
            new CleanupService(),
            _cleanup,
            new ToastNotifier(new(), new ShellPreferences(settings)),
            _monitor,
            new PerformanceRunTracker(),
            new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance),
            new FakeAppNavigator(),
            NullLogger<McpBridge>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _monitor.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Пометка_на_удаление_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<McpException>(() => _bridge.Scan.MarkForDeletion([_root], true))?.Message, Does.Contain(MutationsBlocked));
            Assert.That(_scan.MarkCalls, Is.Zero);
        });
    }

    [Test]
    public void Поиск_дубликатов_без_сканирования_отбивается_с_причиной()
    {
        _scan.ScanRoot = null;

        Assert.That(
            Assert.ThrowsAsync<McpException>(() => _bridge.Scan.FindDuplicatesAsync(1, 10, CancellationToken.None))?.Message,
            Does.Contain("результата ещё нет"));
    }

    [Test]
    public void Пустой_список_путей_не_доходит_до_страницы()
    {
        AllowMutations(true);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<McpException>(() => _bridge.Scan.MarkForDeletion([], true))?.Message, Does.Contain("пуст"));
            Assert.That(_scan.MarkCalls, Is.Zero);
        });
    }

    [Test]
    public void Список_путей_сверх_лимита_не_доходит_до_страницы()
    {
        AllowMutations(true);
        var paths = Enumerable.Repeat(_root, AppDefaults.McpEntryLimitMax + 1).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Assert.Throws<McpException>(() => _bridge.Scan.MarkForDeletion(paths, true))?.Message, Does.Contain("не больше"));
            Assert.That(_scan.MarkCalls, Is.Zero);
        });
    }

    [Test]
    public void Упаковка_в_архив_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ArchiveDirectoryAsync(_root, false, false, CancellationToken.None))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_scan.ArchiveCalls, Is.Zero);
        });
    }

    [Test]
    public void Синхронизация_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);
        _sync.HasComparison = true;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_sync.SyncCalls, Is.Zero);
        });
    }

    [TestCase(false, false, false, "выполните сравнение")]
    [TestCase(true, true, false, "занята другой операцией")]
    [TestCase(true, false, true, "спорные элементы")]
    public void Состояние_страницы_синхронизации_отбивает_запуск(bool hasComparison, bool isBusy, bool hasConflicts, string expected)
    {
        AllowMutations(true);
        _sync.HasComparison = hasComparison;
        _sync.IsBusy = isBusy;
        _sync.HasPendingConflicts = hasConflicts;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None))?.Message, Does.Contain(expected));
            Assert.That(_sync.SyncCalls, Is.Zero);
        });
    }

    [Test]
    public void План_синхронизации_без_сравнения_отбивается()
    {
        Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(true, 20, CancellationToken.None))?.Message,
            Does.Contain("сравнение ещё не выполнялось"));
    }

    [Test]
    public void Состояние_MCP_помечает_исполненный_план()
    {
        _sync.HasComparison = true;
        _sync.PlanFreshness = SyncPlanFreshness.PartiallyApplied;

        var state = new McpStateReader(_scan, _sync, new McpNavigator(new FakeAppNavigator())).ReadSyncState();

        Assert.That(state.PlanFreshness, Is.EqualTo(SyncPlanFreshness.PartiallyApplied));
    }

    [Test]
    public async Task Сухой_прогон_помечает_исполненный_план_устаревшим()
    {
        _sync.HasComparison = true;
        _sync.PlanFreshness = SyncPlanFreshness.Applied;
        _sync.PlanBuilder = () => new SyncPlanExportModel
        {
            LeftPath = _root,
            RightPath = _root,
            Options = new(SyncMode.LeftToRight, SyncWinner.None, false, string.Empty),
            Actions = PlannedActions.Empty,
        };

        var json = await _bridge.Sync.SyncCurrentAsync(true, 20, CancellationToken.None);

        Assert.That(json, Does.Contain("\"planFreshness\": \"Applied\""));
    }

    [Test]
    public void MCP_переводит_отказ_устаревшего_плана_в_ошибку_с_тем_же_текстом()
    {
        const string message = "план уже исполнен, выполните сравнение заново.";

        AllowMutations(true);
        _sync.HasComparison = true;
        _sync.SyncException = new SyncPlanStaleException(message);

        var exception = Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None));

        Assert.That(exception?.Message, Is.EqualTo(message));
    }

    [Test]
    public void Занятая_страница_скана_отбивает_показ_до_обхода()
    {
        _scan.ScanningProbe = static () => true;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ScanAsync(_root, 2, 20, true, CancellationToken.None))?.Message,
                Does.Contain("сейчас занята"));

            Assert.That(_scan.ApplyCalls, Is.Zero);
        });
    }

    [Test]
    public void Страница_занявшаяся_во_время_обхода_не_получает_чужое_дерево()
    {
        var checks = 0;
        _scan.ScanningProbe = () => checks++ > 0;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ScanAsync(_root, 2, 20, true, CancellationToken.None))?.Message,
                Does.Contain("пока шёл обход"));

            Assert.That(_scan.ApplyCalls, Is.Zero);
            Assert.That(_scan.SelectCalls, Is.Zero);
        });
    }

    [Test]
    public async Task Обход_без_показа_идёт_мимо_занятой_страницы()
    {
        _scan.ScanningProbe = static () => true;

        var json = await _bridge.Scan.ScanAsync(_root, 2, 20, false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("файл.txt"));
            Assert.That(_scan.ApplyCalls, Is.Zero);
        });
    }

    [Test]
    public void Замер_неизвестной_корзины_называет_доступные()
    {
        var exception = Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.ScanAsync(["ЧужаяКорзина"], CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ЧужаяКорзина"));
            Assert.That(exception.Message, Does.Contain("TempFiles"));
        });
    }

    [Test]
    public async Task Замер_корзины_отдаёт_доступность_и_порог_возраста()
    {
        var json = await _bridge.Cleanup.ScanAsync(["RecycleBin"], CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"minimumAgeHours\": 24"));
            Assert.That(json, Does.Contain("RecycleBin"));
            Assert.That(json, Does.Contain("\"availabilityHint\""));
        });
    }

    [Test]
    public void Пустой_список_корзин_не_считается_запросом_всех()
    {
        var exception = Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.ScanAsync([" "], CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("TempFiles"));
    }

    [Test]
    public void Очистка_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync(["Prefetch"], false, CancellationToken.None))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_cleanup.CleanCalls, Is.Zero);
        });
    }

    [TestCase(true, false, "занята другой операцией")]
    [TestCase(false, true, "другой диалог")]
    public void Занятое_окно_отбивает_очистку_до_замера(bool pageBusy, bool modalBusy, string expected)
    {
        AllowMutations(true);
        _cleanup.IsBusy = pageBusy;
        _cleanup.IsModalBusy = modalBusy;

        Assert.Multiple(() =>
        {
            Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync(["Prefetch"], false, CancellationToken.None))?.Message,
                Does.Contain(expected));

            Assert.That(_cleanup.CleanCalls, Is.Zero);
        });
    }

    [Test]
    public void Очистка_без_целей_называет_доступные()
    {
        AllowMutations(true);

        Assert.That(Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync([], false, CancellationToken.None))?.Message,
            Does.Contain("TempFiles"));
    }

    [Test]
    public async Task План_очистки_ничего_не_запускает()
    {
        AllowMutations(true);

        var json = await _bridge.Cleanup.RunAsync(["Prefetch"], true, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Contain("\"dryRun\": true"));
            Assert.That(json, Does.Contain("Prefetch"));
            Assert.That(_cleanup.CleanCalls, Is.Zero);
        });
    }

    [TestCase(CleanupConsent.Declined, "Человек отказал в очистке.", "отказал")]
    [TestCase(CleanupConsent.TimedOut, "Человек не ответил на подтверждение вовремя.", "не ответил")]
    [TestCase(CleanupConsent.Nothing, "Очищать нечего: названные корзины пусты или недоступны.", "нечего")]
    [TestCase(CleanupConsent.Stale, "Порог возраста файлов меняли во время подготовки.", "Порог возраста")]
    public void Неподтверждённая_очистка_возвращает_ошибку_а_не_отчёт(CleanupConsent consent, string status, string expected)
    {
        var tools = ToolsOverRunnableTarget();
        _cleanup.Outcome = new(consent, 0, 0, 0, false, false, status);

        Assert.That(Assert.ThrowsAsync<McpException>(() => tools.RunAsync([RunnableTargetId], false, CancellationToken.None))?.Message,
            Does.Contain(expected));
    }

    [Test]
    public void Упавшая_очистка_возвращает_ошибку_а_не_отчёт()
    {
        var tools = ToolsOverRunnableTarget();
        _cleanup.Outcome = new(CleanupConsent.Granted, 0, 0, 1, false, true, "Ошибка: отказано в доступе.");

        Assert.That(Assert.ThrowsAsync<McpException>(() => tools.RunAsync([RunnableTargetId], false, CancellationToken.None))?.Message,
            Does.Contain("отказано в доступе"));
    }

    [Test]
    public async Task Подтверждённая_очистка_отчитывается_освобождённым()
    {
        var tools = ToolsOverRunnableTarget();

        var json = await tools.RunAsync([RunnableTargetId], false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_cleanup.CleanCalls, Is.EqualTo(1));
            Assert.That(_cleanup.LastIds, Is.EquivalentTo(new[] { RunnableTargetId }));
            Assert.That(json, Does.Contain("\"freedBytes\": 1024"));
            Assert.That(json, Does.Contain("\"consent\": \"Granted\""));
        });
    }

    private McpCleanupTools ToolsOverRunnableTarget()
    {
        var temp = Path.Combine(_root, "temp");
        Directory.CreateDirectory(temp);

        var file = Path.Combine(temp, "старый.tmp");
        File.WriteAllText(file, "мусор");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));

        AllowMutations(true);

        var settings = new MemorySettings();

        return new(settings,
            new CleanupService(),
            age =>
            [
                new()
                {
                    Id = RunnableTargetId,
                    Name = "Временные файлы",
                    Description = "Каталог теста",
                    Kind = CleanupTargetKind.Directory,
                    Path = temp,
                    MinimumAge = age,
                },
            ],
            _preferences,
            _cleanup,
            new McpNavigator(new FakeAppNavigator()),
            new ToastNotifier(new(), new ShellPreferences(settings)),
            NullLogger<McpBridge>.Instance);
    }

    private void AllowMutations(bool allowed)
    {
        _preferences.AllowMutations = allowed;
    }
}
