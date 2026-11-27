using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Collections.Concurrent;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanSettingsFlushTests
{
    private string _root = null!;
    private string _target = null!;
    private KeepShellLogging _logging = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-flush", Guid.NewGuid().ToString("N"));
        _target = Path.Combine(_root, "target");
        Directory.CreateDirectory(_target);
        File.WriteAllBytes(Path.Combine(_target, "file.bin"), new byte[1024]);

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });
    }

    [TearDown]
    public void TearDown()
    {
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
    public async Task Повторный_скан_той_же_цели_не_пишет_и_не_сбрасывает_настройки()
    {
        var spy = new SpySettings();
        spy.SetValue(SettingsKeys.UpdateCheckOnStartup, "false");
        spy.SetValue(SettingsKeys.ScanLastDrive, _target);
        spy.ResetCounts();

        using var services = App.ConfigureServices(spy, _logging);
        var scan = ActivatorUtilities.CreateInstance<ScanViewModel>(services, new NoopDialogs());
        await SettleDriveLabelsAsync(scan);
        spy.ResetCounts();

        await scan.ScanFromAutomationAsync(_target, CancellationToken.None);
        await SettleDriveLabelsAsync(scan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spy.LastDriveWrites, Is.Zero);
            Assert.That(spy.FlushCount, Is.Zero);
        }
    }

    [Test]
    public async Task Смена_цели_скана_записывает_и_сбрасывает_настройки_вне_UI_потока_до_обхода()
    {
        var spy = new SpySettings();
        spy.SetValue(SettingsKeys.UpdateCheckOnStartup, "false");
        spy.SetValue(SettingsKeys.ScanLastDrive, Path.Combine(_root, "other"));

        using var services = App.ConfigureServices(spy, _logging);
        var scan = ActivatorUtilities.CreateInstance<ScanViewModel>(services, new NoopDialogs());
        await SettleDriveLabelsAsync(scan);
        spy.ResetCounts();

        var uiThread = Environment.CurrentManagedThreadId;

        scan.Progress.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanProgressViewModel.ScanCurrentPath)
                && string.Equals(scan.Progress.ScanCurrentPath, _target, StringComparison.Ordinal))
            {
                spy.Events.Enqueue("begin");
            }
        };

        await scan.ScanFromAutomationAsync(_target, CancellationToken.None);
        await SettleDriveLabelsAsync(scan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spy.LastDriveWrites, Is.EqualTo(1));
            Assert.That(spy.FlushCount, Is.EqualTo(1));
            Assert.That(spy.LastFlushThreadId, Is.Not.EqualTo(uiThread));
            Assert.That(spy.Events.ToArray(), Is.EqualTo(new[] { "set", "flush", "begin" }));
        }
    }

    [Test]
    public async Task Отмена_сразу_после_записи_цели_всё_равно_сбрасывает_настройки_перед_выходом()
    {
        var spy = new SpySettings();
        spy.SetValue(SettingsKeys.UpdateCheckOnStartup, "false");
        spy.SetValue(SettingsKeys.ScanLastDrive, Path.Combine(_root, "other"));

        using var services = App.ConfigureServices(spy, _logging);
        var scan = ActivatorUtilities.CreateInstance<ScanViewModel>(services, new NoopDialogs());
        await SettleDriveLabelsAsync(scan);
        spy.ResetCounts();

        using var cts = new CancellationTokenSource();
        var uiThread = Environment.CurrentManagedThreadId;

        spy.OnLastDriveSet = () => cts.Cancel();

        scan.Progress.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanProgressViewModel.ScanCurrentPath)
                && string.Equals(scan.Progress.ScanCurrentPath, _target, StringComparison.Ordinal))
            {
                spy.Events.Enqueue("begin");
            }
        };

        await scan.ScanFromAutomationAsync(_target, cts.Token);
        await SettleDriveLabelsAsync(scan);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.ScanWasCancelled, Is.True);
            Assert.That(spy.LastDriveWrites, Is.EqualTo(1));
            Assert.That(spy.FlushCount, Is.EqualTo(1));
            Assert.That(spy.LastFlushThreadId, Is.Not.EqualTo(uiThread));
            Assert.That(spy.Events.ToArray(), Is.EqualTo(new[] { "set", "flush" }));
        }
    }

    private static Task SettleDriveLabelsAsync(ScanViewModel scan)
    {
        return Task.WhenAll(scan.Drives.Items.Select(static drive => drive.LoadAsync()));
    }

    private sealed class SpySettings : ISettingsStore
    {
        private readonly MemorySettings _inner = new();
        private int _lastDriveWrites;
        private int _flushCount;
        private int _lastFlushThreadId;

        public ConcurrentQueue<string> Events { get; } = new();

        public Action? OnLastDriveSet { get; set; }

        public int LastDriveWrites => Volatile.Read(ref _lastDriveWrites);

        public int FlushCount => Volatile.Read(ref _flushCount);

        public int LastFlushThreadId => Volatile.Read(ref _lastFlushThreadId);

        public event EventHandler<string>? Changed
        {
            add => _inner.Changed += value;
            remove => _inner.Changed -= value;
        }

        public event EventHandler<SettingsWriteFailedEventArgs>? WriteFailed
        {
            add => _inner.WriteFailed += value;
            remove => _inner.WriteFailed -= value;
        }

        public string FilePath => _inner.FilePath;

        public string? GetStringValue(string key) => _inner.GetStringValue(key);

        public void SetValue(string key, string value)
        {
            _inner.SetValue(key, value);

            if (key == SettingsKeys.ScanLastDrive)
            {
                Interlocked.Increment(ref _lastDriveWrites);
                Events.Enqueue("set");
                OnLastDriveSet?.Invoke();
            }
        }

        public void Flush()
        {
            Interlocked.Exchange(ref _lastFlushThreadId, Environment.CurrentManagedThreadId);
            Interlocked.Increment(ref _flushCount);
            Events.Enqueue("flush");
            _inner.Flush();
        }

        public void ResetCounts()
        {
            Interlocked.Exchange(ref _lastDriveWrites, 0);
            Interlocked.Exchange(ref _flushCount, 0);
            Interlocked.Exchange(ref _lastFlushThreadId, 0);
            Events.Clear();
        }
    }
}
