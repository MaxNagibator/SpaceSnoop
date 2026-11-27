using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Globalization;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[NonParallelizable]
public class ScanProgressTests
{
    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [SetUp]
    public void SetUp()
    {
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("ru-RU");
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _culture;
    }

    [Test]
    public async Task Завершение_измерения_не_освобождает_чужой_слот()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        var owner = new PerformanceOperation("Синхронизация", 0, 0, TimeSpan.Zero);
        operations.TryReport(owner, null);
        var progress = new ScanProgressViewModel(operations, new FakeUiDispatcher());

        progress.Begin(new(Path.GetTempPath()), @"C:\", 1);
        progress.Finish(releaseMeasurement: false);
        progress.ReportPhase("Показ результата сканирования");
        await progress.CompleteMeasurementAsync();

        Assert.That(operations.Current, Is.SameAs(owner));
        operations.Release(owner);
    }

    [Test]
    public async Task Измерение_начинается_сразу_и_покрывает_показ_и_возврат_памяти()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        var progress = new ScanProgressViewModel(operations, new FakeUiDispatcher());
        progress.ReportPhase("Подготовка сканирования");
        Assert.That(operations.Current?.Name, Is.EqualTo("Подготовка сканирования"));

        progress.Begin(new(Path.GetTempPath()), @"C:\", 4);
        Assert.That(operations.Current?.Name, Is.EqualTo("Сканирование"));

        progress.Finish(releaseMeasurement: false);
        progress.ReportPhase("Показ результата сканирования");
        Assert.That(operations.Current?.Name, Is.EqualTo("Показ результата сканирования"));

        progress.ReportPhase("Возврат памяти после сканирования");
        await Task.Yield();
        Assert.That(operations.Current?.Name, Is.EqualTo("Возврат памяти после сканирования"));

        await progress.CompleteMeasurementAsync();
        Assert.That(operations.Current, Is.Null);
    }

    [Test]
    public void Неготовый_диск_не_даёт_оценку()
    {
        var used = DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVU".Cast<char?>().FirstOrDefault(letter => !used.Contains(letter!.Value));

        Assume.That(free, Is.Not.Null, "на машине нет свободной буквы диска");

        Assert.That(ScanProgressViewModel.EstimateTotalBytes(new($@"{free}:\")), Is.Null);
    }

    [Test]
    public void Оценка_объёма_есть_только_у_корня_диска()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-estimate");

        try
        {
            var root = new DirectoryInfo(Path.GetPathRoot(directory.FullName)!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(directory), Is.Null);
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(root), Is.Not.Null.And.GreaterThan(0));
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void Полоса_хода_неопределённа_только_во_время_скана_без_известной_доли()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-indeterminate");

        try
        {
            var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), new FakeUiDispatcher());
            var beforeBegin = viewModel.IsIndeterminate;

            viewModel.Begin(directory, directory.FullName, 1);
            var whileScanningSubfolder = viewModel.IsIndeterminate;

            viewModel.Finish();
            var afterFinish = viewModel.IsIndeterminate;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(beforeBegin, Is.False);
                Assert.That(whileScanningSubfolder, Is.True);
                Assert.That(afterFinish, Is.False);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public async Task Старт_прогресса_на_букве_сетевого_диска_не_блокирует_вызывающий_поток_и_отбрасывает_поздний_замер()
    {
        var previous = ScanProgressViewModel.VolumeUsedReader;
        var callerThread = Environment.CurrentManagedThreadId;
        var readerThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        ScanProgressViewModel.VolumeUsedReader = _ =>
        {
            Interlocked.Exchange(ref readerThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 10_000L;
        };

        try
        {
            var dispatcher = new FakeUiDispatcher();
            var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
            var timer = dispatcher.Timers.Single();

            var progress = viewModel.Begin(new(@"Z:\"), @"Z:\", 1);
            var pending = viewModel.PendingEstimate;

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Volatile.Read(ref readerThread), Is.Not.EqualTo(callerThread));
                Assert.That(viewModel.IsIndeterminate, Is.True);
            }

            release.Set();
            await pending.WaitAsync(TimeSpan.FromSeconds(5));

            progress.AddFiles(1, 5_000);
            timer.Tick();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.IsIndeterminate, Is.False);
                Assert.That(viewModel.ScanPercentText, Is.EqualTo("50 %"));
            }

            viewModel.Finish();
        }
        finally
        {
            release.Set();
            ScanProgressViewModel.VolumeUsedReader = previous;
        }
    }

    [Test]
    public void Строка_фазы_идёт_мимо_пути_и_не_переприсваивается_на_каждом_тике()
    {
        var dispatcher = new FakeUiDispatcher();
        var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
        var timer = dispatcher.Timers.Single();
        var progress = viewModel.Begin(new(Path.GetTempPath()), @"C:\", 1);
        var stageNotifications = 0;

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanProgressViewModel.ScanStageText))
            {
                stageNotifications++;
            }
        };

        progress.Announce("Чтение $MFT тома C: 40 %");
        timer.Tick();
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ScanShowsStage, Is.True);
            Assert.That(viewModel.ScanStageText, Is.EqualTo("Чтение $MFT тома C: 40 %"));
            Assert.That(viewModel.ScanCurrentPath, Is.EqualTo(@"C:\"));
            Assert.That(stageNotifications, Is.EqualTo(1));
        }

        progress.EnterDirectory(@"C:\Windows");
        timer.Tick();
        progress.Reset();
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.ScanShowsStage, Is.False);
            Assert.That(viewModel.ScanCurrentPath, Is.EqualTo(@"C:\Windows"));
        }
    }

    [Test]
    public void Строка_хода_различает_отсутствие_замера_ноль_и_накопленный_объём()
    {
        var dispatcher = new FakeUiDispatcher();
        var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
        var timer = dispatcher.Timers.Single();
        var progress = viewModel.Begin(new(Path.GetTempPath()), @"C:\", 1);

        var atStart = viewModel.ScanBytesText;

        progress.EnterDirectory(@"C:\Windows");
        timer.Tick();

        var beforeFiles = viewModel.ScanBytesText;

        progress.AddFiles(1, 0);
        timer.Tick();
        var afterEmptyFile = viewModel.ScanBytesText;

        progress.AddFiles(1, 1536);
        timer.Tick();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(atStart, Is.EqualTo("считаем…"));
            Assert.That(beforeFiles, Is.EqualTo("считаем…"));
            Assert.That(afterEmptyFile, Is.EqualTo("0 байт"));
            Assert.That(viewModel.ScanBytesText, Is.EqualTo("1,5 КБ"));
        }
    }

    [Test]
    public void Отложенный_таймер_прогресса_запускается_после_готовности_тома_и_игнорируется_после_финиша()
    {
        var dispatcher = new FakeUiDispatcher();
        var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), dispatcher);
        var timer = dispatcher.Timers.Single();

        var progress = viewModel.Begin(new(@"C:\"), @"C:\", 4, estimatedTotalBytes: 4096, deferTimer: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.Starts, Is.Zero);
            Assert.That(viewModel.PendingEstimate.IsCompleted, Is.True);
        }

        progress.NotifyVolumeReady();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timer.IsRunning, Is.True);
            Assert.That(timer.Starts, Is.EqualTo(1));
        }

        viewModel.Finish();
        Assert.That(timer.IsRunning, Is.False);

        progress.NotifyVolumeReady();
        Assert.That(timer.IsRunning, Is.False);
        Assert.That(timer.Starts, Is.EqualTo(1));

        var second = viewModel.Begin(new(@"C:\"), @"C:\", 4, estimatedTotalBytes: 4096, deferTimer: true);
        viewModel.Finish();
        second.NotifyVolumeReady();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(timer.IsRunning, Is.False);
            Assert.That(timer.Starts, Is.EqualTo(1));
        }
    }

    [Test]
    public void Отключение_фонового_замера_в_Begin_не_дёргает_чтение_тома()
    {
        var previous = ScanProgressViewModel.VolumeUsedReader;
        var readerCalls = 0;

        ScanProgressViewModel.VolumeUsedReader = _ =>
        {
            Interlocked.Increment(ref readerCalls);
            return 10_000L;
        };

        try
        {
            var viewModel = new ScanProgressViewModel(new(TestDiagnostics.Monitor()), new FakeUiDispatcher());
            viewModel.Begin(new(@"C:\"), @"C:\", 1, estimatedTotalBytes: null, deferTimer: true, probeEstimate: false);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Volatile.Read(ref readerCalls), Is.Zero);
                Assert.That(viewModel.PendingEstimate.IsCompleted, Is.True);
            }

            viewModel.Finish();
        }
        finally
        {
            ScanProgressViewModel.VolumeUsedReader = previous;
        }
    }

    [Test]
    public async Task Асинхронная_оценка_тома_переиспользует_идущий_замер_и_отменяется_сразу()
    {
        var previous = ScanProgressViewModel.VolumeUsedReader;
        var readerCalls = 0;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        ScanProgressViewModel.VolumeUsedReader = _ =>
        {
            Interlocked.Increment(ref readerCalls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return 12_345L;
        };

        try
        {
            using var cancelFirst = new CancellationTokenSource();
            var first = ScanProgressViewModel.EstimateTotalBytesAsync(new(@"Z:\"), cancelFirst.Token);

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);

            var second = ScanProgressViewModel.EstimateTotalBytesAsync(new(@"z:\"), CancellationToken.None);
            await cancelFirst.CancelAsync();

            await Assert.CatchAsync<OperationCanceledException>(() => first);
            Assert.That(Volatile.Read(ref readerCalls), Is.EqualTo(1));

            release.Set();
            var measured = await second.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.That(measured, Is.EqualTo(12_345L));
            Assert.That(Volatile.Read(ref readerCalls), Is.EqualTo(1));
        }
        finally
        {
            release.Set();
            ScanProgressViewModel.VolumeUsedReader = previous;
        }
    }

    [TestCase(0L, 0L, false)]
    [TestCase(100L, 0L, false)]
    [TestCase(0L, 100L, false)]
    [TestCase(99L, 100L, false)]
    [TestCase(100L, 100L, true)]
    [TestCase(101L, 100L, true)]
    public void Уплотнение_после_скана_требует_высокой_нагрузки_памяти(long load, long threshold, bool expected)
    {
        Assert.That(ScanViewModel.ShouldReturnScanMemory(load, threshold), Is.EqualTo(expected));
    }

    [TestCase(false, false, 0L, 100L, nameof(ScanMemoryAction.None))]
    [TestCase(false, false, 100L, 100L, nameof(ScanMemoryAction.None))]
    [TestCase(false, true, 0L, 100L, nameof(ScanMemoryAction.ReclaimMft))]
    [TestCase(false, true, 100L, 100L, nameof(ScanMemoryAction.ReclaimMft))]
    [TestCase(true, false, 99L, 100L, nameof(ScanMemoryAction.None))]
    [TestCase(true, false, 100L, 100L, nameof(ScanMemoryAction.Compact))]
    [TestCase(true, false, 100L, 0L, nameof(ScanMemoryAction.None))]
    [TestCase(true, true, 0L, 100L, nameof(ScanMemoryAction.Compact))]
    [TestCase(true, true, 100L, 100L, nameof(ScanMemoryAction.Compact))]
    public void Выбор_действия_возврата_памяти_учитывает_настройку_MFT_и_давление(
        bool returnMemoryAfterScan,
        bool usedMft,
        long loadBytes,
        long thresholdBytes,
        string expected)
    {
        Assert.That(
            ScanViewModel.SelectScanMemoryAction(returnMemoryAfterScan, usedMft, loadBytes, thresholdBytes),
            Is.EqualTo(Enum.Parse<ScanMemoryAction>(expected)));
    }
}
