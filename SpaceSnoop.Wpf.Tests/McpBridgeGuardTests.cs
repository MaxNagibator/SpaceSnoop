using KeepShell.Diagnostics;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Text.Json;

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
    private PerformanceOperations _operations = null!;
    private NetworkDriveRestore _networkDrives = null!;
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
        _monitor = TestDiagnostics.Monitor();
        _operations = new(_monitor);
        _networkDrives = new();

        var scanPreferences = new ScanPreferences(settings);

        _bridge = new(
            settings,
            _sync,
            _scan,
            _preferences,
            scanPreferences,
            new(new(), new(), scanPreferences, NullLogger<ScanRunner>.Instance),
            new(),
            new(),
            new(),
            _cleanup,
            new(new(), new(settings)),
            _monitor,
            _operations,
            new(NullLogger<DirectoryComparer>.Instance),
            new FakeAppNavigator(),
            _networkDrives,
            NullLogger<McpBridge>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _networkDrives.Dispose();
        _monitor.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task MCP_переводит_отказ_устаревшего_плана_в_ошибку_с_тем_же_текстом()
    {
        const string message = "план уже исполнен, выполните сравнение заново.";

        AllowMutations(true);
        _sync.HasComparison = true;
        _sync.SyncException = new SyncPlanStaleException(message);

        var exception = await Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None));

        Assert.That(exception?.Message, Is.EqualTo(message));
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
    public async Task Замер_неизвестной_корзины_называет_доступные()
    {
        var exception = await Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.ScanAsync(["ЧужаяКорзина"], CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("ЧужаяКорзина"));
            Assert.That(exception.Message, Does.Contain("TempFiles"));
        });
    }

    [Test]
    public async Task Занятая_страница_скана_отбивает_показ_до_обхода()
    {
        _scan.ScanningProbe = static () => true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ScanAsync(_root, 2, 20, true, CancellationToken.None)))?.Message,
                Does.Contain("сейчас занята"));

            Assert.That(_scan.ApplyCalls, Is.Zero);
            Assert.That(_scan.PageCalls, Is.Empty);
        }
    }

    [TestCase(true, false, "занята другой операцией")]
    [TestCase(false, true, "другой диалог")]
    public async Task Занятое_окно_отбивает_очистку_до_замера(bool pageBusy, bool modalBusy, string expected)
    {
        AllowMutations(true);
        _cleanup.IsBusy = pageBusy;
        _cleanup.IsModalBusy = modalBusy;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync(["Prefetch"], false, CancellationToken.None)))?.Message,
                Does.Contain(expected));

            Assert.That(_cleanup.CleanCalls, Is.Zero);
        }
    }

    [Test]
    public async Task Корень_скана_ссылка_отбивается_понятным_текстом()
    {
        var link = Path.Combine(Path.GetTempPath(), "ss_guard_link_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateSymbolicLink(link, _root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("не удалось создать символьную ссылку");
        }

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ScanAsync(link, 2, 20, true, CancellationToken.None)))?.Message,
                    Does.Contain("ссылка на другое место"));

                Assert.That(_scan.PageCalls, Is.Empty);

                Assert.That(
                    (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.OpenScanAsync(link, true, CancellationToken.None)))?.Message,
                    Does.Contain("ссылка на другое место"));

                await Assert.ThatAsync(() => _bridge.Scan.OpenScanAsync(link, false, CancellationToken.None), Throws.Nothing);
                Assert.That(_scan.SelectCalls, Is.EqualTo(1));
            }
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [TestCase(CleanupConsent.Declined, "Человек отказал в очистке.", "отказал")]
    [TestCase(CleanupConsent.TimedOut, "Человек не ответил на подтверждение вовремя.", "не ответил")]
    [TestCase(CleanupConsent.Nothing, "Очищать нечего: названные корзины пусты или недоступны.", "нечего")]
    [TestCase(CleanupConsent.Stale, "Порог возраста файлов меняли во время подготовки.", "Порог возраста")]
    public async Task Неподтверждённая_очистка_возвращает_ошибку_а_не_отчёт(CleanupConsent consent, string status, string expected)
    {
        var tools = ToolsOverRunnableTarget();
        _cleanup.Outcome = new(consent, 0, 0, 0, false, false, status);

        Assert.That(
            (await Assert.ThrowsAsync<McpException>(() => tools.RunAsync([RunnableTargetId], false, CancellationToken.None)))?.Message,
            Does.Contain(expected));
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
            Assert.That(_scan.PageCalls, Is.Empty);
        });
    }

    [Test]
    public async Task Очистка_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync(["Prefetch"], false, CancellationToken.None)))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_cleanup.CleanCalls, Is.Zero);
        }
    }

    [Test]
    public async Task Очистка_без_целей_называет_доступные()
    {
        AllowMutations(true);

        Assert.That(
            (await Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.RunAsync([], false, CancellationToken.None)))?.Message,
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

    [Test]
    public async Task План_синхронизации_без_сравнения_отбивается()
    {
        Assert.That(
            (await Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(true, 20, CancellationToken.None)))?.Message,
            Does.Contain("сравнение ещё не выполнялось"));
    }

    [Test]
    public async Task Подтверждённая_очистка_отчитывается_освобождённым()
    {
        var tools = ToolsOverRunnableTarget();

        var json = await tools.RunAsync([RunnableTargetId], false, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_cleanup.CleanCalls, Is.EqualTo(1));
            Assert.That(
                _cleanup.LastIds,
                Is.EquivalentTo(
                    new[]
                    {
                        RunnableTargetId,
                    }));

            Assert.That(json, Does.Contain("\"freedBytes\": 1024"));
            Assert.That(json, Does.Contain("\"consent\": \"Granted\""));
        });
    }

    [Test]
    public async Task Поиск_дубликатов_без_сканирования_отбивается_с_причиной()
    {
        _scan.ScanRoot = null;

        Assert.That(
            (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.FindDuplicatesAsync(1, 10, CancellationToken.None)))?.Message,
            Does.Contain("результата ещё нет"));
    }

    [Test]
    public async Task Показ_снимает_прежний_корень_страницы_до_обхода()
    {
        await _bridge.Scan.ScanAsync(_root, 2, 20, true, CancellationToken.None);

        Assert.That(
            _scan.PageCalls,
            Is.EqualTo(
                new[]
                {
                    $"release {_root}", $"apply {_root}",
                }));
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
    public async Task Пустой_список_корзин_не_считается_запросом_всех()
    {
        var exception = await Assert.ThrowsAsync<McpException>(() => _bridge.Cleanup.ScanAsync([" "], CancellationToken.None));

        Assert.That(exception!.Message, Does.Contain("TempFiles"));
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
    public async Task Синхронизация_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);
        _sync.HasComparison = true;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None)))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_sync.SyncCalls, Is.Zero);
        }
    }

    [Test]
    public async Task Скан_MCP_дожидается_переподключения_своей_буквы_без_ответа_путь_не_найден()
    {
        var mounted = Path.Combine(_root, "mounted");
        var letter = $"{char.ToUpperInvariant(Path.GetPathRoot(_root)![0])}:";
        var otherLetter = letter == "Z:" ? "Y:" : "Z:";

        using var entered = new ManualResetEventSlim();
        using var allowFinish = new ManualResetEventSlim();
        using var releaseOther = new ManualResetEventSlim();

        _networkDrives.Start(
            true,
            () =>
            [
                new(letter, @"\\fast\share", null, null),
                new(otherLetter, @"\\slow\share", null, null),
            ],
            () => [],
            drive =>
            {
                if (drive.LocalName == letter)
                {
                    entered.Set();
                    allowFinish.Wait(TimeSpan.FromSeconds(10));
                    Directory.CreateDirectory(mounted);
                    File.WriteAllText(Path.Combine(mounted, "ready.txt"), "данные");
                    return 0;
                }

                releaseOther.Wait(TimeSpan.FromSeconds(10));
                return 53;
            },
            NullLogger.Instance);

        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

        var scanTask = _bridge.Scan.ScanAsync(mounted, 2, 20, false, CancellationToken.None);
        allowFinish.Set();

        var json = await scanTask.WaitAsync(TimeSpan.FromSeconds(5));
        releaseOther.Set();

        Assert.That(json, Does.Contain("ready.txt"));
    }

    [Test]
    public void Состояние_MCP_помечает_исполненный_план()
    {
        _sync.HasComparison = true;
        _sync.PlanFreshness = SyncPlanFreshness.PartiallyApplied;

        var state = new McpStateReader(_scan, _sync, new(new FakeAppNavigator())).ReadSyncState();

        Assert.That(state.PlanFreshness, Is.EqualTo(SyncPlanFreshness.PartiallyApplied));
    }

    [TestCase(false, false, false, "выполните сравнение")]
    [TestCase(true, true, false, "занята другой операцией")]
    [TestCase(true, false, true, "спорные элементы")]
    public async Task Состояние_страницы_синхронизации_отбивает_запуск(bool hasComparison, bool isBusy, bool hasConflicts, string expected)
    {
        AllowMutations(true);
        _sync.HasComparison = hasComparison;
        _sync.IsBusy = isBusy;
        _sync.HasPendingConflicts = hasConflicts;

        using (Assert.EnterMultipleScope())
        {
            Assert.That((await Assert.ThrowsAsync<McpException>(() => _bridge.Sync.SyncCurrentAsync(false, 20, CancellationToken.None)))?.Message, Does.Contain(expected));
            Assert.That(_sync.SyncCalls, Is.Zero);
        }
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
    public async Task Страница_занявшаяся_во_время_обхода_не_получает_чужое_дерево()
    {
        var checks = 0;
        _scan.ScanningProbe = () => checks++ > 0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ScanAsync(_root, 2, 20, true, CancellationToken.None)))?.Message,
                Does.Contain("пока шёл обход").And.Contain("результат не показан"));

            Assert.That(_scan.ApplyCalls, Is.Zero);
            Assert.That(_scan.SelectCalls, Is.Zero);
            Assert.That(
                _scan.PageCalls,
                Is.EqualTo(
                    new[]
                    {
                        $"release {_root}",
                    }));
        }
    }

    [Test]
    public async Task Сухой_прогон_помечает_исполненный_план_устаревшим()
    {
        _sync.HasComparison = true;
        _sync.PlanFreshness = SyncPlanFreshness.Applied;
        _sync.PlanBuilder = () => new()
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
    public void Текущая_операция_доезжает_до_get_performance_и_гаснет_вместе_со_слотом()
    {
        var operation = new PerformanceOperation("Сканирование", 1000, 2048, TimeSpan.FromSeconds(2));

        _operations.TryReport(operation, null);

        using var busy = JsonDocument.Parse(_bridge.Insight.GetPerformance(0, 0));

        _operations.Release(operation);

        using var idle = JsonDocument.Parse(_bridge.Insight.GetPerformance(0, 0));

        var reported = busy.RootElement.GetProperty("operation");

        Assert.Multiple(() =>
        {
            Assert.That(reported.GetProperty("name").GetString(), Is.EqualTo("Сканирование"));
            Assert.That(reported.GetProperty("items").GetInt64(), Is.EqualTo(1000));
            Assert.That(reported.GetProperty("elapsedSeconds").GetDouble(), Is.EqualTo(2));
            Assert.That(reported.GetProperty("summary").GetString(), Does.StartWith("Сканирование"));
            Assert.That(idle.RootElement.TryGetProperty("operation", out _), Is.False);
        });
    }

    [Test]
    public async Task Упавшая_очистка_возвращает_ошибку_а_не_отчёт()
    {
        var tools = ToolsOverRunnableTarget();
        _cleanup.Outcome = new(CleanupConsent.Granted, 0, 0, 1, false, true, "Ошибка: отказано в доступе.");

        Assert.That(
            (await Assert.ThrowsAsync<McpException>(() => tools.RunAsync([RunnableTargetId], false, CancellationToken.None)))?.Message,
            Does.Contain("отказано в доступе"));
    }

    [Test]
    public async Task Упаковка_в_архив_без_разрешения_не_доходит_до_страницы()
    {
        AllowMutations(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                (await Assert.ThrowsAsync<McpException>(() => _bridge.Scan.ArchiveDirectoryAsync(_root, false, false, CancellationToken.None)))?.Message,
                Does.Contain(MutationsBlocked));

            Assert.That(_scan.ArchiveCalls, Is.Zero);
        }
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

        return new(
            settings,
            new(),
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
            new(new FakeAppNavigator()),
            new(new(), new(settings)),
            NullLogger<McpBridge>.Instance);
    }

    private void AllowMutations(bool allowed)
    {
        _preferences.AllowMutations = allowed;
    }
}
