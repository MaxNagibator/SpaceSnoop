using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Core.UseCases;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class McpBridgeGuardTests
{
    private const string MutationsBlocked = "Изменяющие операции запрещены";

    private string _root = string.Empty;
    private McpPreferences _preferences = null!;
    private ScanAutomationDouble _scan = null!;
    private SyncAutomationDouble _sync = null!;
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
        _monitor = new(NullLogger<PerformanceMonitor>.Instance);

        _bridge = new(settings,
            _sync,
            _scan,
            _preferences,
            new ScanPreferences(settings),
            new DiskSpaceCalculator(),
            new DockerService(),
            new ToastNotifier(new(), new ShellPreferences(settings)),
            _monitor,
            new PerformanceRunTracker(),
            new CompareDirectoriesUseCase(NullLogger<DirectoryComparer>.Instance),
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

    private void AllowMutations(bool allowed)
    {
        _preferences.AllowMutations = allowed;
    }
}
