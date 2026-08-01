using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Diagnostics;

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
        Assert.That(PerformanceFormat.Operation(null), Is.Null);
    }

    [Test]
    public void Строка_операции_собирает_имя_скорость_и_остаток()
    {
        var operation = new PerformanceOperation("Синхронизация", 100, 1024, TimeSpan.FromSeconds(10), TotalItems: 300);

        var text = PerformanceFormat.Operation(operation);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.StartWith("Синхронизация · "));
            Assert.That(text, Does.Contain("шт/с"));
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

        Assert.That(PerformanceFormat.Summary(snapshot), Does.EndWith("Сканирование · 500 шт/с"));
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
}
