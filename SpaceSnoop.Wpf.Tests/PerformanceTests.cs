using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Converters;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.Diagnostics;
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
    public void Пустой_буфер_не_даёт_ни_пика_ни_среднего()
    {
        var samples = new PerformanceSamples(4);

        Assert.Multiple(() =>
        {
            Assert.That(samples.Count, Is.Zero);
            Assert.That(samples.Peak(), Is.Zero);
            Assert.That(samples.Average(), Is.Zero);
        });
    }

    [Test]
    public void Буфер_вытесняет_старые_значения_и_забывает_ушедший_пик()
    {
        var samples = new PerformanceSamples(3);

        samples.Add(900);
        samples.Add(10);
        samples.Add(20);
        samples.Add(30);

        Assert.Multiple(() =>
        {
            Assert.That(samples.Count, Is.EqualTo(3));
            Assert.That(samples.Peak(), Is.EqualTo(30));
            Assert.That(samples.Last, Is.EqualTo(30));
            Assert.That(samples.Average(), Is.EqualTo(20));
        });
    }

    [Test]
    public void Очистка_возвращает_буфер_в_исходное_состояние()
    {
        var samples = new PerformanceSamples(2);
        samples.Add(500);
        samples.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(samples.Count, Is.Zero);
            Assert.That(samples.Peak(), Is.Zero);
            Assert.That(samples.Last, Is.Zero);
        });
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
    public void Остаток_считается_по_штукам_когда_известен_их_итог()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 0, TimeSpan.FromSeconds(10), TotalItems: 300);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(20)));
    }

    [Test]
    public void Остаток_падает_на_байты_когда_итог_по_штукам_неизвестен()
    {
        var operation = new PerformanceOperation("Синхронизация", 0, 1024, TimeSpan.FromSeconds(1), TotalBytes: 4096);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(3)));
    }

    [Test]
    public void Основа_байтов_меряет_остаток_объёмом_а_не_штуками()
    {
        var operation = new PerformanceOperation("Синхронизация", 1000, 1_000_000, TimeSpan.FromSeconds(10),
            TotalItems: 1001,
            TotalBytes: 11_000_000,
            Basis: EtaBasis.Bytes);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(100)));
    }

    [Test]
    public void Без_явной_основы_тысяча_мелких_файлов_прячет_один_огромный()
    {
        var operation = new PerformanceOperation("Синхронизация", 1000, 1_000_000, TimeSpan.FromSeconds(10),
            TotalItems: 1001,
            TotalBytes: 11_000_000);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(0.01)));
    }

    [Test]
    public void Основа_штук_не_падает_на_байты()
    {
        var operation = new PerformanceOperation("Сравнение", 100, 1_000_000, TimeSpan.FromSeconds(10),
            TotalBytes: 11_000_000,
            Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Перебор_итога_не_даёт_отрицательного_остатка()
    {
        var operation = new PerformanceOperation("Синхронизация", 400, 0, TimeSpan.FromSeconds(10), TotalItems: 300);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Без_итога_остаток_неизвестен()
    {
        var operation = new PerformanceOperation("Сканирование", 400, 8192, TimeSpan.FromSeconds(10));

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Отсутствие_операции_не_даёт_строки()
    {
        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.Operation(null), Is.Null);
            Assert.That(PerformanceFormat.Rate(null), Is.Null);
            Assert.That(PerformanceFormat.Remaining(null), Is.Null);
        });
    }

    [Test]
    public void Строка_скорости_идёт_без_имени_операции()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 2048, TimeSpan.FromSeconds(2));

        Assert.That(PerformanceFormat.Rate(operation), Is.EqualTo($"50 файлов/с · {SizeFormatter.Format(1024)}/с"));
    }

    [Test]
    public void Слишком_короткий_замер_не_даёт_строки_скорости()
    {
        var operation = new PerformanceOperation("Сравнение", 5, 500, TimeSpan.FromMilliseconds(50));

        Assert.That(PerformanceFormat.Rate(operation), Is.Null);
    }

    [Test]
    public void Остаток_подаётся_приблизительным()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 0, TimeSpan.FromSeconds(10), TotalItems: 300);

        Assert.That(PerformanceFormat.Remaining(operation), Is.EqualTo("≈ 0:20"));
    }

    [Test]
    public void Неизвестный_остаток_не_даёт_строки()
    {
        var operation = new PerformanceOperation("Сканирование", 100, 2048, TimeSpan.FromSeconds(10));

        Assert.That(PerformanceFormat.Remaining(operation), Is.Null);
    }

    [Test]
    public void Строка_операции_собирает_имя_скорость_и_остаток()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 1024, TimeSpan.FromSeconds(10), TotalItems: 300);

        var text = PerformanceFormat.Operation(operation);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Синхронизация · "));
            Assert.That(text, Does.Contain("файлов/с"));
            Assert.That(text, Does.Contain("/с"));
            Assert.That(text, Does.Contain("осталось 0:20"));
        });
    }

    [Test]
    public void Короткая_операция_показывается_одним_именем()
    {
        var operation = new PerformanceOperation("Сравнение", 5, 0, TimeSpan.FromMilliseconds(50));

        Assert.That(PerformanceFormat.Operation(operation), Is.EqualTo("Сравнение"));
    }

    [Test]
    public void Сводка_без_операции_несёт_отклик_и_память()
    {
        var snapshot = PerformanceSnapshot.Empty with { UiDelayMs = 12.4, ManagedBytes = 1024 };

        Assert.That(PerformanceFormat.Summary(snapshot), Is.EqualTo($"12 мс · {SizeFormatter.Format(1024)}"));
    }

    [Test]
    public void Сводка_с_операцией_дописывает_её_в_конец()
    {
        var operation = new PerformanceOperation("Сканирование", 1000, 0, TimeSpan.FromSeconds(2));
        var snapshot = PerformanceSnapshot.Empty with { UiDelayMs = 3, ManagedBytes = 2048, Operation = operation };

        Assert.That(PerformanceFormat.Summary(snapshot), Does.EndWith("Сканирование · 500 файлов/с"));
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
    public void Сводка_для_буфера_обмена_несёт_замеры_и_операцию()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            CapturedAtUtc = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc),
            UiDelayMs = 12.4,
            UiPeakMs = 640,
            SampleCount = 20,
            ObservedSpanSeconds = 10,
            ManagedBytes = 1024,
            StartupSeconds = 1.25,
            Operation = new("Сканирование", 1000, 0, TimeSpan.FromSeconds(2)),
        };

        var text = PerformanceReport.Build(snapshot, "2.8.42");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("2.8.42"));
            Assert.That(text, Does.Contain("пик 640 мс"));
            Assert.That(text, Does.Contain("20 замеров за 10,0 с"));
            Assert.That(text, Does.Contain("Сейчас идёт: Сканирование – 2,0 с"));
            Assert.That(text, Does.Not.Contain("Замеров ещё нет"));
        });
    }

    [Test]
    public void Сводка_разводит_неизмеренные_кадры_и_измеренные_без_просадок()
    {
        var noFrames = PerformanceSnapshot.Empty with { CapturedAtUtc = DateTime.UtcNow, SampleCount = 20 };

        var drawn = noFrames with
        {
            FrameLastMs = 16.7,
            FramePeakMs = 21.5,
            FrameAverageMs = 17,
            FrameCount = 31,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceReport.Build(noFrames, "2.8.42"), Does.Contain("Кадры окна: не измерялись"));
            Assert.That(PerformanceReport.Build(drawn, "2.8.42"), Does.Contain("Кадры окна: пик 21,5 мс, среднее 17,0 мс за 31 кадр, дольше 50 мс – 0"));
        });
    }

    [Test]
    public void Сводка_сразу_после_сброса_не_выдаёт_ноль_за_измеренный_отклик()
    {
        var justStarted = PerformanceSnapshot.Empty with
        {
            CapturedAtUtc = DateTime.UtcNow,
            ManagedBytes = 4096,
            StartupSeconds = 1.25,
        };

        var text = PerformanceReport.Build(justStarted, "2.8.42");

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("Замеров ещё нет"));
            Assert.That(text, Does.Not.Contain("Отклик UI"));
            Assert.That(text, Does.Contain(SizeFormatter.Format(4096)));
            Assert.That(text, Does.Contain("Последний прогон – нет прогонов"));
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
    public void Итог_считает_скорость_по_применённым_и_скопированным()
    {
        var report = new SyncReport { CopiedCount = 9 };
        report.Errors.Add(new("b.txt", SyncAction.CopyToRight, "нет доступа"));
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 2048);

        var text = SyncSessionViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromSeconds(2));

        Assert.That(text, Is.EqualTo($" Скорость: 5 файлов/с · {SizeFormatter.Format(1024)}/с."));
    }

    [Test]
    public void Итог_короткой_операции_обходится_без_скорости()
    {
        var report = new SyncReport { CopiedCount = 1 };
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 512);

        Assert.That(SyncSessionViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromMilliseconds(80)), Is.Empty);
    }

    [Test]
    public void Плитка_отклика_не_выдаёт_ноль_за_измеренный()
    {
        var justReset = PerformanceSnapshot.Empty with { CapturedAtUtc = DateTime.UtcNow };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileDelay(justReset), Is.EqualTo("нет замеров"));
            Assert.That(PerformanceFormat.TileDelayHint(justReset), Does.Not.Contain("пик"));
            Assert.That(PerformanceFormat.TileWindow(justReset), Is.EqualTo("пик и среднее – замеров ещё нет"));
        });
    }

    [Test]
    public void Признак_просадки_стоит_на_пике_а_не_на_мгновенной_задержке()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            CapturedAtUtc = DateTime.UtcNow,
            UiDelayMs = 2,
            UiPeakMs = 640,
            UiAverageMs = 35,
            SampleCount = 20,
            ObservedSpanSeconds = 10.4,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileDelay(snapshot), Is.EqualTo("640 мс"));
            Assert.That(PerformanceFormat.TileDelayHint(snapshot), Is.EqualTo("сейчас 2 мс · среднее 35 мс"));
            Assert.That(PerformanceFormat.TileWindow(snapshot), Is.EqualTo("пик и среднее – по 20 замерам за 10,4 с"));
        });
    }

    [Test]
    public void Плитка_памяти_ведёт_рабочим_набором_и_несёт_пик_за_сеанс()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            ManagedBytes = 40 * 1024 * 1024,
            WorkingSetBytes = 580 * 1024 * 1024,
            WorkingSetPeakBytes = 612 * 1024 * 1024,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileMemory(snapshot), Is.EqualTo(SizeFormatter.Format(580 * 1024 * 1024)));
            Assert.That(PerformanceFormat.TileMemoryHint(snapshot), Does.Contain(SizeFormatter.Format(40 * 1024 * 1024)));
            Assert.That(PerformanceFormat.TileMemoryPeak(snapshot), Is.EqualTo($"пик за сеанс {SizeFormatter.Format(612 * 1024 * 1024)}"));
            Assert.That(PerformanceFormat.TileMemoryPeak(PerformanceSnapshot.Empty), Does.Contain("замеров ещё нет"));
        });
    }

    [Test]
    public void Сборки_за_окно_не_путаются_с_суммой_от_старта()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            Gen0Collections = 22,
            Gen1Collections = 10,
            Gen2Collections = 5,
            History = new(75, 150, 3, 1, 0),
        };

        var single = snapshot with { History = new(0, 1, 0, 0, 0) };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileCollections(snapshot), Is.EqualTo("22 / 10 / 5"));
            Assert.That(PerformanceFormat.TileCollectionsWindow(snapshot), Is.EqualTo("за последние 1:15: 3 / 1 / 0"));
            Assert.That(PerformanceFormat.TileCollectionsWindow(single), Does.Contain("мерить не по чему"));
        });
    }

    [Test]
    public void Плитка_кадров_ведёт_пиком_и_отделяет_долгие_кадры_от_спокойных()
    {
        var quiet = PerformanceSnapshot.Empty with
        {
            FrameLastMs = 16.7,
            FramePeakMs = 18.2,
            FrameAverageMs = 16.7,
            FrameCount = 620,
        };

        var slow = quiet with
        {
            FramePeakMs = 214.8,
            FrameAverageMs = 17.2,
            FrameCount = 420,
            SlowFrameCount = 3,
        };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileFrame(slow), Is.EqualTo("214,8 мс"));
            Assert.That(PerformanceFormat.TileFrameHint(slow), Is.EqualTo("3 долгих кадра из 420 · среднее 17,2 мс"));
            Assert.That(PerformanceFormat.TileFrameHint(quiet), Is.EqualTo("дольше 50 мс не было · среднее 16,7 мс"));
            Assert.That(PerformanceFormat.TileFrame(PerformanceSnapshot.Empty), Is.EqualTo("кадров ещё нет"));
            Assert.That(PerformanceFormat.TileFrameHint(PerformanceSnapshot.Empty), Does.Contain("на этой странице"));
        });
    }

    [Test]
    public void Окно_равное_всему_сбору_не_повторяет_счётчики_второй_строкой()
    {
        var snapshot = PerformanceSnapshot.Empty with
        {
            Gen0Collections = 19,
            Gen1Collections = 18,
            Gen2Collections = 2,
            History = new(192, 358, 19, 18, 2),
        };

        Assert.That(PerformanceFormat.TileCollectionsWindow(snapshot), Is.EqualTo("сбор идёт 3:12, окно покрывает его целиком"));
    }

    [Test]
    public void Недоложенный_старт_отличается_от_мгновенного()
    {
        var missing = PerformanceSnapshot.Empty with { CapturedAtUtc = DateTime.UtcNow };
        var measured = missing with { StartupSeconds = 1.25 };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileStartup(missing), Is.EqualTo("не измерялся"));
            Assert.That(PerformanceFormat.TileStartupHint(missing), Does.Contain("не доложен"));
            Assert.That(PerformanceFormat.TileStartup(measured), Is.EqualTo("1,25 с"));
            Assert.That(PerformanceFormat.TileStartupHint(measured), Does.Contain("первого кадра"));
        });
    }

    [Test]
    public void Возраст_снимка_объявляется_только_после_двух_интервалов_съёма()
    {
        var now = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        var fresh = PerformanceSnapshot.Empty with { CapturedAtUtc = now.AddMilliseconds(-AppDefaults.PerformanceSampleIntervalMs) };
        var stale = PerformanceSnapshot.Empty with { CapturedAtUtc = now.AddSeconds(-30) };

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.StaleWarning(PerformanceSnapshot.Empty, now), Is.Null);
            Assert.That(PerformanceFormat.StaleWarning(fresh, now), Is.Null);
            Assert.That(PerformanceFormat.StaleWarning(stale, now), Does.Contain("30 с назад"));
        });
    }

    [TestCase(0, 0, "0:00")]
    [TestCase(0, 75, "1:15")]
    [TestCase(1, 5, "1:00:05")]
    public void Длительность_переходит_на_часы_только_после_часа(int hours, int seconds, string expected)
    {
        var value = TimeSpan.FromHours(hours) + TimeSpan.FromSeconds(seconds);

        Assert.That(PerformanceFormat.Duration(value), Is.EqualTo(expected));
    }

    [Test]
    public void Отрицательная_длительность_показывается_нулём()
    {
        Assert.That(PerformanceFormat.Duration(TimeSpan.FromSeconds(-5)), Is.EqualTo("0:00"));
    }

    [Test]
    public void Несбыточный_остаток_не_показывается_вовсе()
    {
        var operation = new PerformanceOperation("Сканирование", 1, 0, TimeSpan.FromSeconds(1), TotalItems: 1_000_000_000, Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.Null);
    }

    [Test]
    public void Остаток_в_пределах_суток_остаётся_виден()
    {
        var operation = new PerformanceOperation("Сканирование", 100, 0, TimeSpan.FromSeconds(1), TotalItems: 1000, Basis: EtaBasis.Items);

        Assert.That(operation.Remaining(), Is.EqualTo(TimeSpan.FromSeconds(9)));
    }

    [Test]
    public void Просадка_называет_пик_а_не_только_красит_строку()
    {
        var snapshot = PerformanceSnapshot.Empty with { UiDelayMs = 3, UiPeakMs = 800 };

        Assert.That(PerformanceFormat.Summary(snapshot), Does.Contain("пик 800 мс"));
    }

    [TestCase(0, "0,0 с")]
    [TestCase(0.04, "0,0 с")]
    [TestCase(12.34, "12,3 с")]
    [TestCase(59.94, "59,9 с")]
    [TestCase(60, "1:00")]
    [TestCase(61.5, "1:01")]
    [TestCase(3599, "59:59")]
    [TestCase(3661, "61:01")]
    public void Длительность_прогона_держит_секунды_до_минуты_и_минуты_дальше(double seconds, string expected)
    {
        Assert.That(PerformanceFormat.Elapsed(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public void Плитка_операции_в_простое_показывает_итог_последнего_прогона()
    {
        var last = new PerformanceOperation("Синхронизация", 4200, 4_500_000_000, TimeSpan.FromSeconds(12));

        var tile = PerformanceFormat.TileOperation(null, last);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Последний прогон: Синхронизация"));
            Assert.That(tile.Value, Is.EqualTo("12,0 с"));
            Assert.That(tile.Volume, Does.Contain("файлов").And.Contain(SizeFormatter.Format(4_500_000_000)));
            Assert.That(tile.Rate, Does.Contain("/с"));
        });
    }

    [Test]
    public void Идущая_операция_вытесняет_итог_и_несёт_остаток()
    {
        var current = new PerformanceOperation("Сканирование", 1000, 2000, TimeSpan.FromSeconds(2), TotalItems: 2000, Basis: EtaBasis.Items);
        var last = new PerformanceOperation("Синхронизация", 10, 20, TimeSpan.FromSeconds(30));

        var tile = PerformanceFormat.TileOperation(current, last);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Сейчас идёт: Сканирование"));
            Assert.That(tile.Value, Is.EqualTo("2,0 с"));
            Assert.That(tile.Rate, Does.Contain("осталось ≈ 0:02"));
        });
    }

    [Test]
    public void Плитка_операции_показывает_обход_только_у_сканирования()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromSeconds(4), Traversal: new(120, 3, 8));
        var sync = new PerformanceOperation("Синхронизация", 900, 1000, TimeSpan.FromSeconds(4));

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.TileOperation(null, scan).Traversal, Is.EqualTo("обход: 120 каталогов · 30 каталогов/с"));
            Assert.That(PerformanceFormat.TileOperation(null, scan).TraversalDetail, Is.EqualTo("8 потоков · 3 каталога без доступа"));
            Assert.That(PerformanceFormat.TileOperation(null, sync).Traversal, Is.Null);
            Assert.That(PerformanceFormat.TileOperation(null, sync).TraversalDetail, Is.Null);
        });
    }

    [Test]
    public void Обход_без_пропусков_говорит_об_этом_прямо()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromSeconds(4), Traversal: new(120, 0, 1));

        Assert.That(PerformanceFormat.TraversalDetail(scan), Is.EqualTo("1 поток · пропусков нет"));
    }

    [Test]
    public void Слишком_короткий_прогон_не_выдаёт_скорость_обхода_за_измеренную()
    {
        var scan = new PerformanceOperation("Сканирование", 900, 1000, TimeSpan.FromMilliseconds(50), Traversal: new(120, 0, 4));

        Assert.Multiple(() =>
        {
            Assert.That(scan.DirectoriesPerSecond, Is.Null);
            Assert.That(PerformanceFormat.Traversal(scan), Is.EqualTo("обход: 120 каталогов"));
        });
    }

    [Test]
    public void До_первого_прогона_плитка_операции_не_выдаёт_нули_за_замеры()
    {
        var tile = PerformanceFormat.TileOperation(null, null);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Caption, Is.EqualTo("Последний прогон"));
            Assert.That(tile.Value, Is.EqualTo("нет прогонов"));
            Assert.That(tile.Volume, Does.Contain("ещё не запускались"));
            Assert.That(tile.Rate, Does.Not.Contain("0"));
        });
    }

    [Test]
    public void В_плитке_стоит_последний_завершившийся_прогон_а_сброс_её_обнуляет()
    {
        var tracker = new PerformanceRunTracker();
        var changes = 0;

        tracker.Changed += (_, _) => changes++;

        tracker.Report(new("Сканирование", 10, 20, TimeSpan.FromSeconds(3)));
        tracker.Report(new("Синхронизация", 5, 6, TimeSpan.FromSeconds(1)));

        var afterRuns = tracker.Last;

        tracker.Clear();

        var afterClear = tracker.Last;

        tracker.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(afterRuns?.Name, Is.EqualTo("Синхронизация"));
            Assert.That(afterClear, Is.Null);
            Assert.That(changes, Is.EqualTo(3));
        });
    }

    [Test]
    public void Слот_операции_гасит_только_тот_кто_его_занял()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);

        var page = new PerformanceOperation("Сканирование", 10, 20, TimeSpan.FromSeconds(1));
        var agent = new PerformanceOperation(BackgroundScanProbe.OperationName, 3, 4, TimeSpan.FromSeconds(1));

        monitor.TryReportOperation(page, null);

        var agentBlocked = monitor.TryReportOperation(agent, null);

        monitor.ClearOperation(agent);

        var stillBusy = !monitor.TryReportOperation(agent, null);

        monitor.ClearOperation(page);

        var freed = monitor.TryReportOperation(agent, null);

        Assert.Multiple(() =>
        {
            Assert.That(agentBlocked, Is.False);
            Assert.That(stillBusy, Is.True);
            Assert.That(freed, Is.True);
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
            Assert.That(PerformanceFormat.TraversalDetail(live), Does.Contain("7 каталогов без доступа"));
        });
    }

    [Test]
    public void Итог_агентского_скана_доходит_до_плитки_и_освобождает_слот()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);
        var tracker = new PerformanceRunTracker();

        PerformanceOperation run;
        bool published, heldWhileWalking;

        using (var probe = new BackgroundScanProbe(monitor, tracker, null, 8))
        {
            probe.Progress.EnterDirectory(@"C:\Sources");
            probe.Progress.AddFiles(120, 4096);
            probe.Progress.FailDirectory();

            published = probe.Publish();
            heldWhileWalking = !monitor.TryReportOperation(Page(), null);

            run = probe.Finish();
        }

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.True);
            Assert.That(heldWhileWalking, Is.True);
            Assert.That(tracker.Last?.Name, Is.EqualTo("Сканирование (агент)"));
            Assert.That(tracker.Last?.Items, Is.EqualTo(120));
            Assert.That(run.Traversal, Is.EqualTo(new PerformanceTraversal(1, 1, 8)));
            Assert.That(monitor.TryReportOperation(Page(), null), Is.True);
        });
    }

    [Test]
    public void Зонд_агентского_скана_не_вытесняет_операцию_окна()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);
        var tracker = new PerformanceRunTracker();
        var page = Page();

        monitor.TryReportOperation(page, null);

        using var probe = new BackgroundScanProbe(monitor, tracker, null, 4);
        probe.Progress.AddFiles(5, 500);

        var published = probe.Publish();

        probe.Finish();

        Assert.Multiple(() =>
        {
            Assert.That(published, Is.False);
            Assert.That(tracker.Last?.Name, Is.EqualTo("Сканирование (агент)"));
            Assert.That(monitor.TryReportOperation(Page(), page), Is.True);
        });
    }

    [Test]
    public void Прерванный_обход_агента_не_попадает_в_плитку_и_освобождает_слот()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);
        var tracker = new PerformanceRunTracker();

        using (var probe = new BackgroundScanProbe(monitor, tracker, null, 2))
        {
            probe.Progress.AddFiles(9, 900);
            probe.Publish();
        }

        Assert.Multiple(() =>
        {
            Assert.That(tracker.Last, Is.Null);
            Assert.That(monitor.TryReportOperation(Page(), null), Is.True);
        });
    }

    [TestCase(1030d, 14d, 3)]
    [TestCase(1030d, 22.4d, 3)]
    [TestCase(590d, 14d, 2)]
    [TestCase(590d, 22.4d, 2)]
    [TestCase(430d, 22.4d, 1)]
    [TestCase(0d, 14d, 3)]
    public void Плитки_раскладываются_по_ширине_с_оглядкой_на_масштаб_шрифта(double width, double fontSize, int expected)
    {
        var columns = TileColumnsConverter.Columns(width, fontSize, AppDefaults.PerformanceTileMinWidth, AppDefaults.PerformanceTileColumnsMax);

        Assert.That(columns, Is.EqualTo(expected));
    }

    [Test]
    public void Подпись_просадок_называет_окно_и_обрезку_списка()
    {
        var moment = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var quiet = new PerformanceHitches([], 0, 75);
        var trimmed = new PerformanceHitches([new(moment, 900, "Сканирование"), new(moment, 700, null)], 37, 300);
        var whole = new PerformanceHitches([new(moment, 900, "Сканирование")], 1, 75);

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.HitchesCaption(PerformanceHitches.Empty), Does.Contain("сбор только запущен"));
            Assert.That(PerformanceFormat.HitchesCaption(quiet), Is.EqualTo($"Просадок от {AppDefaults.PerformanceHitchMs} мс за 1:15 наблюдения не было"));
            Assert.That(PerformanceFormat.HitchesCaption(trimmed), Is.EqualTo("37 просадок за 5:00 наблюдения, ниже последние 2"));
            Assert.That(PerformanceFormat.HitchesCaption(whole), Is.EqualTo("1 просадка за 1:15 наблюдения"));
        });
    }

    [Test]
    public void Подпись_просадок_не_умалчивает_о_кольце_и_сбросе()
    {
        Assert.That(PerformanceFormat.HitchesHint,
            Does.Contain($"{AppDefaults.PerformanceHistorySecondsMax / 60} мин").And.Contain("Сбросить"));
    }

    [Test]
    public void Строка_просадки_несёт_время_задержку_и_операцию()
    {
        var moment = new DateTime(2026, 8, 1, 10, 30, 5, DateTimeKind.Utc);

        var named = PerformanceFormat.HitchText(new(moment, 812.4, "Сравнение"));
        var idle = PerformanceFormat.HitchText(new(moment, 500, null));

        Assert.Multiple(() =>
        {
            Assert.That(named.Time, Is.EqualTo(moment.ToLocalTime().ToString("HH:mm:ss")));
            Assert.That(named.Delay, Is.EqualTo("812 мс"));
            Assert.That(named.Operation, Is.EqualTo("Сравнение"));
            Assert.That(idle.Operation, Is.EqualTo("вне операций"));
        });
    }

    [Test]
    public void Кадры_считаются_по_промежуткам_а_первая_отметка_только_заводит_отсчёт()
    {
        var frames = new PerformanceFrames(50, 2000);
        var start = Stopwatch.GetTimestamp();

        frames.Mark(start);
        frames.Mark(start + (Stopwatch.Frequency / 100));
        frames.Mark(start + (Stopwatch.Frequency / 5));

        Assert.Multiple(() =>
        {
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames.SlowCount, Is.EqualTo(1));
            Assert.That(frames.PeakMs, Is.EqualTo(190).Within(1));
            Assert.That(frames.LastMs, Is.EqualTo(190).Within(1));
            Assert.That(frames.AverageMs, Is.EqualTo(100).Within(1));
        });
    }

    [Test]
    public void Пауза_зонда_не_записывает_простой_как_один_гигантский_кадр()
    {
        var frames = new PerformanceFrames(50, 2000);
        var start = Stopwatch.GetTimestamp();

        frames.Mark(start);
        frames.Mark(start + (Stopwatch.Frequency / 100));
        frames.Pause();
        frames.Mark(start + (Stopwatch.Frequency * 60));
        frames.Mark(start + (Stopwatch.Frequency * 60) + (Stopwatch.Frequency / 100));

        Assert.Multiple(() =>
        {
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames.SlowCount, Is.Zero);
            Assert.That(frames.PeakMs, Is.EqualTo(10).Within(1));
        });
    }

    [Test]
    public void Свёрнутое_окно_не_превращается_в_кадр_длиной_в_свой_простой()
    {
        var frames = new PerformanceFrames(50, 2000);
        var start = Stopwatch.GetTimestamp();

        frames.Mark(start);
        frames.Mark(start + (Stopwatch.Frequency / 100));
        frames.Mark(start + (Stopwatch.Frequency * 90));
        frames.Mark(start + (Stopwatch.Frequency * 90) + (Stopwatch.Frequency / 100));

        Assert.Multiple(() =>
        {
            Assert.That(frames.Count, Is.EqualTo(2));
            Assert.That(frames.GapCount, Is.EqualTo(1));
            Assert.That(frames.SlowCount, Is.Zero);
            Assert.That(frames.PeakMs, Is.EqualTo(10).Within(1));
        });
    }

    private static PerformanceOperation Page()
    {
        return new("Сканирование", 1, 1, TimeSpan.FromSeconds(1));
    }
}
