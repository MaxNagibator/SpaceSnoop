using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PerformanceTests
{
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

        Assert.That(PerformanceFormat.Rate(operation), Is.EqualTo($"50 файл/с · {SizeFormatter.Format(1024)}/с"));
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
            Assert.That(text, Does.Contain("файл/с"));
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

        Assert.That(PerformanceFormat.Summary(snapshot), Does.EndWith("Сканирование · 500 файл/с"));
    }

    [Test]
    public void Итог_различает_проверено_и_проверка_прервана()
    {
        var report = new SyncReport();
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 1024);

        var interrupted = SyncViewModel.DescribeVerify(true, report);
        report.MarkVerified();

        Assert.Multiple(() =>
        {
            Assert.That(interrupted, Does.Contain("проверка прервана"));
            Assert.That(SyncViewModel.DescribeVerify(true, report), Is.EqualTo(", расхождений: 0"));
            Assert.That(SyncViewModel.DescribeVerify(false, report), Is.Empty);
        });
    }

    [Test]
    public void Итог_считает_скорость_по_применённым_и_скопированным()
    {
        var report = new SyncReport { CopiedCount = 9 };
        report.Errors.Add(new("b.txt", SyncAction.CopyToRight, "нет доступа"));
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 2048);

        var text = SyncViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromSeconds(2));

        Assert.That(text, Is.EqualTo($" Скорость: 5 файл/с · {SizeFormatter.Format(1024)}/с."));
    }

    [Test]
    public void Итог_короткой_операции_обходится_без_скорости()
    {
        var report = new SyncReport { CopiedCount = 1 };
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 512);

        Assert.That(SyncViewModel.DescribeRate("Синхронизация", report, TimeSpan.FromMilliseconds(80)), Is.Empty);
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
}
