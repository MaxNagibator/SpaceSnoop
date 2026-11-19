using KeepShell.Diagnostics;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.Globalization;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PerformanceTests
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
    public void Без_итога_остаток_неизвестен()
    {
        var operation = new PerformanceOperation("Сканирование", 400, 8192, TimeSpan.FromSeconds(10));

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Без_явной_основы_тысяча_мелких_файлов_прячет_один_огромный()
    {
        var operation = new PerformanceOperation(
            "Синхронизация",
            1000,
            1_000_000,
            TimeSpan.FromSeconds(10),
            1001,
            11_000_000);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(0.01)));
    }

    [Test]
    public void В_плитке_стоит_последний_завершившийся_прогон_а_сброс_её_обнуляет()
    {
        using var monitor = TestDiagnostics.Monitor();

        var operations = new PerformanceOperations(monitor);
        var changes = 0;

        operations.Changed += (_, _) => changes++;

        operations.ReportRun(new("Сканирование", 10, 20, TimeSpan.FromSeconds(3)));
        operations.ReportRun(new("Синхронизация", 5, 6, TimeSpan.FromSeconds(1)));

        var afterRuns = operations.Last;

        operations.ClearRun();

        var afterClear = operations.Last;

        operations.ClearRun();

        Assert.Multiple(() =>
        {
            Assert.That(afterRuns?.Name, Is.EqualTo("Синхронизация"));
            Assert.That(afterClear, Is.Null);
            Assert.That(changes, Is.EqualTo(3));
        });
    }

    [TestCase(0, 0, "0:00")]
    [TestCase(0, 75, "1:15")]
    [TestCase(1, 5, "1:00:05")]
    public void Длительность_переходит_на_часы_только_после_часа(int hours, int seconds, string expected)
    {
        var value = TimeSpan.FromHours(hours) + TimeSpan.FromSeconds(seconds);

        Assert.That(PerformanceText.Duration(value), Is.EqualTo(expected));
    }

    [TestCase(0, "< 0,1 с")]
    [TestCase(0.04, "< 0,1 с")]
    [TestCase(0.05, "0,1 с")]
    [TestCase(12.34, "12,3 с")]
    [TestCase(59.94, "59,9 с")]
    [TestCase(60, "1:00")]
    [TestCase(61.5, "1:01")]
    [TestCase(3599, "59:59")]
    [TestCase(3661, "61:01")]
    public void Длительность_прогона_держит_секунды_до_минуты_и_минуты_дальше(double seconds, string expected)
    {
        Assert.That(PerformanceText.Elapsed(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public void До_первого_прогона_плитка_операции_не_выдаёт_нули_за_замеры()
    {
        var tile = PerformanceText.TileOperation(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Последний прогон"));
            Assert.That(tile.Value, Is.EqualTo("нет прогонов"));
            Assert.That(tile.Volume, Does.Contain("ещё не запускались"));
            Assert.That(tile.Rate, Does.Not.Contain("0"));
        });
    }

    [Test]
    public void Зонд_агентского_скана_не_вытесняет_операцию_окна()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        var page = Page();

        operations.TryReport(page, null);

        using var probe = new BackgroundScanProbe(operations, null, 4);
        probe.Progress.AddFiles(5, 500);

        var published = probe.Publish();

        probe.Finish();

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.False);
            Assert.That(operations.Last?.Name, Is.EqualTo("Сканирование (агент)"));
            Assert.That(operations.TryReport(Page(), page), Is.True);
        });
    }

    [Test]
    public void Зонд_агентского_скана_описывает_обход_и_остаток()
    {
        var snapshot = new ScanProgressSnapshot(1200, 7, 48_000, 6_000_000_000, 0, 0, @"C:\Sources");
        var live = BackgroundScanProbe.Describe(snapshot, TimeSpan.FromSeconds(4), 12_000_000_000, 16);

        Assert.Multiple(() =>
        {
            Assert.That(live.Name, Is.EqualTo("Сканирование (агент)"));
            Assert.That(live.Items, Is.EqualTo(48_000));
            Assert.That(live.Traversal, Is.EqualTo(new PerformanceTraversal(1200, 7, 16)));
            Assert.That(live.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(PerformanceText.TraversalDetail(live), Does.Contain("7 каталогов без доступа"));
        });
    }

    [Test]
    public void Идущая_операция_вытесняет_итог_и_несёт_остаток()
    {
        var current = new PerformanceOperation("Сканирование", 1000, 2000, TimeSpan.FromSeconds(2), 2000, Basis: EtaBasis.Items);
        var last = new PerformanceOperation("Синхронизация", 10, 20, TimeSpan.FromSeconds(30));

        var tile = PerformanceText.TileOperation(current, last);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Сейчас идёт: Сканирование"));
            Assert.That(tile.Value, Is.EqualTo("2,0 с"));
            Assert.That(tile.Rate, Does.Contain("осталось ≈ 0:02"));
        });
    }

    [Test]
    public void Имя_операции_доезжает_до_монитора_меткой_фазы()
    {
        using var monitor = TestDiagnostics.Monitor();

        var operations = new PerformanceOperations(monitor);
        var page = Page();

        operations.TryReport(page, null);
        monitor.Start();

        var busy = monitor.Snapshot.Phase;

        operations.Release(page);
        monitor.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(busy, Is.EqualTo(page.Name));
            Assert.That(monitor.Snapshot.Phase, Is.Null);
        });
    }

    [Test]
    public void Итог_агентского_скана_доходит_до_плитки_и_освобождает_слот()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);

        PerformanceOperation run;
        bool published, heldWhileWalking;

        using (var probe = new BackgroundScanProbe(operations, null, 8))
        {
            probe.Progress.EnterDirectory(@"C:\Sources");
            probe.Progress.AddFiles(120, 4096);
            probe.Progress.FailDirectory();

            published = probe.Publish();
            heldWhileWalking = !operations.TryReport(Page(), null);

            run = probe.Finish();
        }

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(heldWhileWalking, Is.True);
            Assert.That(operations.Last?.Name, Is.EqualTo("Сканирование (агент)"));
            Assert.That(operations.Last?.Items, Is.EqualTo(120));
            Assert.That(run.Traversal, Is.EqualTo(new PerformanceTraversal(1, 1, 8)));
            Assert.That(operations.TryReport(Page(), null), Is.True);
        });
    }

    [Test]
    public void Итог_короткой_операции_обходится_без_скорости()
    {
        var report = new SyncReport
        {
            CopiedCount = 1,
        };

        report.AddApplied(SyncAction.CopyToRight, "a.txt", 512);

        Assert.That(SyncSessionViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromMilliseconds(80)), Is.Empty);
    }

    [Test]
    public void Итог_различает_проверено_и_проверка_прервана()
    {
        var report = new SyncReport();
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 1024);

        var interrupted = SyncPlanNarrative.DescribeVerify(SyncPlanNarrative.ResolveVerify(true, report), report.Mismatches.Count);
        report.MarkVerified();

        Assert.Multiple(() =>
        {
            Assert.That(interrupted, Does.Contain("проверка прервана"));
            Assert.That(SyncPlanNarrative.DescribeVerify(SyncPlanNarrative.ResolveVerify(true, report), report.Mismatches.Count), Is.EqualTo(", расхождений: 0"));
            Assert.That(SyncPlanNarrative.DescribeVerify(SyncPlanNarrative.ResolveVerify(false, report), report.Mismatches.Count), Is.Empty);
        });
    }

    [Test]
    public void Итог_считает_скорость_по_применённым_и_скопированным()
    {
        var report = new SyncReport
        {
            CopiedCount = 9,
        };

        report.Errors.Add(new("b.txt", SyncAction.CopyToRight, "нет доступа"));
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 2048);

        var text = SyncSessionViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromSeconds(2));

        Assert.That(text, Is.EqualTo($" Скорость: 5 файлов/с · {SizeFormatter.Format(1024)}/с."));
    }

    [Test]
    public void Карточка_диагностики_забывает_прогон_своей_командой()
    {
        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var card = new ScanOperationsCard(operations, new FakeUiDispatcher());

        var idle = card.Command!.CanExecute(null);

        operations.ReportRun(new("Синхронизация", 5, 6, TimeSpan.FromSeconds(1)));

        var armed = card.Command.CanExecute(null);

        card.Command.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(idle, Is.False);
            Assert.That(armed, Is.True);
            Assert.That(operations.Last, Is.Null);
            Assert.That(card.CommandCaption, Is.Not.Null.And.Not.Empty);
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Unknown));
        });
    }

    [Test]
    public void Карточка_диагностики_отмечает_предупреждением_пропуски_и_неполноту_Mft()
    {
        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var card = new ScanOperationsCard(operations, new FakeUiDispatcher());
        var notes = new ScanNotes(true, 1, 0, 3, 4096, 0, 0);

        operations.ReportRun(
            new(
                "Сканирование",
                900,
                1000,
                TimeSpan.FromSeconds(4),
                Traversal: new(120, 0, 1),
                Notes: notes));

        var mftRow = card.Rows.Single(static row => row.Key == "Неполнота $MFT");

        Assert.Multiple(() =>
        {
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Warning));
            Assert.That(mftRow.Value, Is.EqualTo("3 объекта"));
            Assert.That(mftRow.Hint, Does.Contain("нет в дереве: 3 объекта"));
            Assert.That(
                PerformanceText.TraversalDetail(operations.Last),
                Is.EqualTo("1 поток · неполных в $MFT: 3 объекта"));
        });
    }

    [Test]
    public void Карточка_диагностики_считает_только_на_открытой_странице()
    {
        var dispatcher = new FakeUiDispatcher();
        var operations = new PerformanceOperations(TestDiagnostics.Monitor());
        var card = new ScanOperationsCard(operations, dispatcher);
        var operation = new PerformanceOperation("Сканирование", 1000, 2048, TimeSpan.FromSeconds(2));

        var timer = dispatcher.Timers[0];
        var idle = card.State;

        card.SetActive(true);
        var runningWhenIdle = timer.IsRunning;

        operations.TryReport(operation, null);
        var started = timer.IsRunning;
        timer.Tick();

        var title = card.Title;
        var rows = card.Rows;

        operations.Release(operation);
        var stoppedOnRelease = !timer.IsRunning;

        operations.TryReport(operation, null);
        card.SetActive(false);

        Assert.Multiple(() =>
        {
            Assert.That(idle, Is.EqualTo(DiagnosticsCardState.Unknown));
            Assert.That(runningWhenIdle, Is.False);
            Assert.That(started, Is.True);
            Assert.That(stoppedOnRelease, Is.True);
            Assert.That(title, Is.EqualTo("Сейчас идёт: Сканирование"));
            Assert.That(card.State, Is.EqualTo(DiagnosticsCardState.Ok));
            Assert.That(
                rows.Select(static row => row.Key),
                Is.EqualTo(
                    new[]
                    {
                        "Длительность", "Объём", "Скорость",
                    }));

            Assert.That(timer.IsRunning, Is.False);
        });
    }

    [Test]
    public void Короткая_операция_показывается_одним_именем()
    {
        var operation = new PerformanceOperation("Сравнение", 5, 0, TimeSpan.FromMilliseconds(50));

        Assert.That(PerformanceText.Operation(operation), Is.EqualTo("Сравнение"));
    }

    [Test]
    public async Task Короткий_агентский_скан_держит_измерение_до_конца_выгрузки()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        using var probe = new BackgroundScanProbe(operations, null, 4);
        Assert.That(operations.Current?.Name, Is.EqualTo(BackgroundScanProbe.OperationName));

        probe.Finish(releaseMeasurement: false);
        probe.ReportPhase("Экспорт сканирования");
        Assert.That(operations.Current?.Name, Is.EqualTo("Экспорт сканирования"));

        probe.ReportPhase("Показ результата сканирования");
        Assert.That(operations.TryReport(Page(), null), Is.False);

        await probe.CompleteMeasurementAsync();
        Assert.That(operations.Current, Is.Null);
        probe.Dispose();
        probe.Dispose();
        probe.ReportPhase("Запоздавший отчёт");
        Assert.That(operations.Current, Is.Null);
    }

    [TestCase("Сканирование")]
    [TestCase("Сравнение")]
    public void Короткий_прогон_не_дублирует_подпись_скорости_в_карточке(string name)
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);
        var card = new ScanOperationsCard(operations, new FakeUiDispatcher());

        operations.ReportRun(new(name, 5, 1024, TimeSpan.FromSeconds(0.1)));

        var row = card.Rows.Single(row => row.Key == "Скорость");

        Assert.Multiple(() =>
        {
            Assert.That(card.Title, Does.Contain(name));
            Assert.That(row.Value, Is.EqualTo("прогон слишком короткий"));
        });
    }

    [Test]
    public void Логический_объём_не_выдаётся_за_пропускную_способность()
    {
        var scan = new PerformanceOperation("Сканирование", 100, 2048, TimeSpan.FromSeconds(2), LogicalBytes: true);

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceText.Rate(scan), Is.EqualTo("50 файлов/с"));
            Assert.That(scan.BytesPerSecond, Is.EqualTo(1024).Within(1));
        });
    }

    [Test]
    public void Неизвестный_остаток_не_даёт_строки()
    {
        var operation = new PerformanceOperation("Сканирование", 100, 2048, TimeSpan.FromSeconds(10));

        Assert.That(PerformanceText.Remaining(operation), Is.Null);
    }

    [Test]
    public void Несбыточный_остаток_не_показывается_вовсе()
    {
        var operation = new PerformanceOperation("Сканирование", 1, 0, TimeSpan.FromSeconds(1), 1_000_000_000, Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Нулевой_объём_не_даёт_скорости()
    {
        var operation = new PerformanceOperation("Сравнение", 40, 0, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(operation.ItemsPerSecond, Is.EqualTo(20));
            Assert.That(operation.BytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void Обход_без_пропусков_говорит_об_этом_прямо()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromSeconds(4), Traversal: new(120, 0, 1));

        Assert.That(PerformanceText.TraversalDetail(scan), Is.EqualTo("1 поток · пропусков нет"));
    }

    [Test]
    public void Основа_байтов_меряет_остаток_объёмом_а_не_штуками()
    {
        var operation = new PerformanceOperation(
            "Синхронизация",
            1000,
            1_000_000,
            TimeSpan.FromSeconds(10),
            1001,
            11_000_000,
            EtaBasis.Bytes);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(100)));
    }

    [Test]
    public void Основа_штук_не_падает_на_байты()
    {
        var operation = new PerformanceOperation(
            "Сравнение",
            100,
            1_000_000,
            TimeSpan.FromSeconds(10),
            TotalBytes: 11_000_000,
            Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Остановка_монитора_не_освобождает_занятый_слот_операции()
    {
        using var monitor = TestDiagnostics.Monitor();

        var operations = new PerformanceOperations(monitor);
        var page = Page();

        operations.TryReport(page, null);
        monitor.Start();
        monitor.Stop();

        var afterStop = operations.Current;
        var stillBusy = !operations.TryReport(Page(), null);

        operations.Release(page);
        monitor.Start();

        Assert.Multiple(() =>
        {
            Assert.That(afterStop, Is.SameAs(page));
            Assert.That(stillBusy, Is.True);
            Assert.That(monitor.Snapshot.Phase, Is.Null);
        });
    }

    [Test]
    public void Остаток_в_пределах_суток_остаётся_виден()
    {
        var operation = new PerformanceOperation("Сканирование", 100, 0, TimeSpan.FromSeconds(1), 1000, Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(9)));
    }

    [Test]
    public void Остаток_падает_на_байты_когда_итог_по_штукам_неизвестен()
    {
        var operation = new PerformanceOperation("Синхронизация", 0, 1024, TimeSpan.FromSeconds(1), TotalBytes: 4096);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void Остаток_подаётся_приблизительным()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 0, TimeSpan.FromSeconds(10), 300);

        Assert.That(PerformanceText.Remaining(operation), Is.EqualTo("≈ 0:20"));
    }

    [Test]
    public void Остаток_считается_по_штукам_когда_известен_их_итог()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 0, TimeSpan.FromSeconds(10), 300);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public void Отрицательная_длительность_показывается_нулём()
    {
        Assert.That(PerformanceText.Duration(TimeSpan.FromSeconds(-5)), Is.EqualTo("0:00"));
    }

    [Test]
    public void Отсутствие_операции_не_даёт_строки()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PerformanceText.Operation(null), Is.Null);
            Assert.That(PerformanceText.Rate(null), Is.Null);
            Assert.That(PerformanceText.Remaining(null), Is.Null);
        });
    }

    [Test]
    public void Перебор_итога_не_даёт_отрицательного_остатка()
    {
        var operation = new PerformanceOperation("Синхронизация", 400, 0, TimeSpan.FromSeconds(10), 300);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Плитка_операции_в_простое_показывает_итог_последнего_прогона()
    {
        var last = new PerformanceOperation("Синхронизация", 4200, 4_500_000_000, TimeSpan.FromSeconds(12));

        var tile = PerformanceText.TileOperation(null, last);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Последний прогон: Синхронизация"));
            Assert.That(tile.Value, Is.EqualTo("12,0 с"));
            Assert.That(tile.Volume, Does.Contain("файлов").And.Contain(SizeFormatter.Format(4_500_000_000)));
            Assert.That(tile.Rate, Does.Contain("/с"));
        });
    }

    [Test]
    public void Плитка_операции_показывает_обход_только_у_сканирования()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromSeconds(4), Traversal: new(120, 3, 8));
        var sync = new PerformanceOperation("Синхронизация", 900, 1000, TimeSpan.FromSeconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceText.TileOperation(null, scan).Traversal, Is.EqualTo("120 каталогов · 30 каталогов/с"));
            Assert.That(PerformanceText.TileOperation(null, scan).TraversalDetail, Is.EqualTo("8 потоков · 3 каталога без доступа"));
            Assert.That(PerformanceText.TileOperation(null, sync).Traversal, Is.Null);
            Assert.That(PerformanceText.TileOperation(null, sync).TraversalDetail, Is.Null);
        });
    }

    [Test]
    public void Прерванный_обход_агента_не_попадает_в_плитку_и_освобождает_слот()
    {
        using var monitor = TestDiagnostics.Monitor();
        var operations = new PerformanceOperations(monitor);

        using (var probe = new BackgroundScanProbe(operations, null, 2))
        {
            probe.Progress.AddFiles(9, 900);
            probe.Publish();
        }

        Assert.Multiple(() =>
        {
            Assert.That(operations.Last, Is.Null);
            Assert.That(operations.TryReport(Page(), null), Is.True);
        });
    }

    [Test]
    public void Признак_проверки_различает_выключено_прервано_и_пройдено()
    {
        var report = new SyncReport();
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 1024);

        var interrupted = SyncPlanNarrative.ResolveVerify(true, report);
        report.MarkVerified();

        Assert.Multiple(() =>
        {
            Assert.That(SyncPlanNarrative.ResolveVerify(false, report), Is.EqualTo(SyncVerifyState.None));
            Assert.That(interrupted, Is.EqualTo(SyncVerifyState.Interrupted));
            Assert.That(SyncPlanNarrative.ResolveVerify(true, report), Is.EqualTo(SyncVerifyState.Completed));
        });
    }

    [Test]
    public void Просадка_называет_пик_а_не_только_красит_строку()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            UiDelayMs = 3,
            UiPeakMs = 800,
        };

        Assert.That(PerformanceText.Summary(snapshot, null), Does.Contain("пик 800 мс"));
    }

    [Test]
    public void Сводка_без_операции_несёт_отклик_и_память()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            UiDelayMs = 12.4,
            ManagedBytes = 1024,
        };

        Assert.That(PerformanceText.Summary(snapshot, null), Is.EqualTo($"Отклик 12 мс · Память {SizeFormatter.Format(1024)}"));
    }

    [Test]
    public void Сводка_с_операцией_дописывает_её_в_конец()
    {
        var operation = new PerformanceOperation("Сканирование", 1000, 0, TimeSpan.FromSeconds(2));
        var snapshot = PerformanceSnapshot.Empty with
        {
            UiDelayMs = 3,
            ManagedBytes = 2048,
        };

        Assert.That(PerformanceText.Summary(snapshot, operation), Does.EndWith("Сканирование · 500 файлов/с"));
    }

    [Test]
    public void Скорость_не_считается_на_слишком_коротком_замере()
    {
        var operation = new PerformanceOperation("Сканирование", 1000, 2048, TimeSpan.FromMilliseconds(100));

        Assert.Multiple(() =>
        {
            Assert.That(operation.ItemsPerSecond, Is.Null);
            Assert.That(operation.BytesPerSecond, Is.Null);
        });
    }

    [Test]
    public void Скорость_считается_на_достаточном_замере()
    {
        var operation = new PerformanceOperation("Сканирование", 1000, 2048, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(operation.ItemsPerSecond, Is.EqualTo(500));
            Assert.That(operation.BytesPerSecond, Is.EqualTo(1024));
        });
    }

    [Test]
    public void Слишком_короткий_замер_не_даёт_строки_скорости()
    {
        var operation = new PerformanceOperation("Сравнение", 5, 500, TimeSpan.FromMilliseconds(50));

        Assert.That(PerformanceText.Rate(operation), Is.Null);
    }

    [Test]
    public void Слишком_короткий_прогон_не_выдаёт_скорость_обхода_за_измеренную()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromMilliseconds(50), Traversal: new(120, 0, 4));

        Assert.Multiple(() =>
        {
            Assert.That(scan.DirectoriesPerSecond, Is.Null);
            Assert.That(PerformanceText.Traversal(scan), Is.EqualTo("120 каталогов"));
        });
    }

    [Test]
    public void Слот_операции_гасит_только_тот_кто_его_занял()
    {
        using var monitor = TestDiagnostics.Monitor();

        var operations = new PerformanceOperations(monitor);
        var page = new PerformanceOperation("Сканирование", 10, 20, TimeSpan.FromSeconds(1));
        var agent = new PerformanceOperation(BackgroundScanProbe.OperationName, 3, 4, TimeSpan.FromSeconds(1));

        operations.TryReport(page, null);

        var agentBlocked = operations.TryReport(agent, null);

        operations.Release(agent);

        var stillBusy = !operations.TryReport(agent, null);
        var heldByPage = operations.Current;

        operations.Release(page);

        var freed = operations.TryReport(agent, null);

        Assert.Multiple(() =>
        {
            Assert.That(agentBlocked, Is.False);
            Assert.That(stillBusy, Is.True);
            Assert.That(heldByPage, Is.SameAs(page));
            Assert.That(freed, Is.True);
            Assert.That(operations.Current, Is.SameAs(agent));
        });
    }

    [Test]
    public void Строка_операции_собирает_имя_скорость_и_остаток()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 1024, TimeSpan.FromSeconds(10), 300);

        var text = PerformanceText.Operation(operation);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Синхронизация · "));
            Assert.That(text, Does.Contain("файлов/с"));
            Assert.That(text, Does.Contain("/с"));
            Assert.That(text, Does.Contain("осталось 0:20"));
        });
    }

    [Test]
    public void Строка_скорости_идёт_без_имени_операции()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 2048, TimeSpan.FromSeconds(2));

        Assert.That(PerformanceText.Rate(operation), Is.EqualTo($"50 файлов/с · {SizeFormatter.Format(1024)}/с"));
    }

    private static PerformanceOperation Page()
    {
        return new("Сканирование", 1, 1, TimeSpan.FromSeconds(1));
    }
}
