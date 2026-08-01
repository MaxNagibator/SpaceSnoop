using SpaceSnoop.Wpf.Diagnostics;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PerformanceChartTests
{
    [Test]
    public void Одной_точки_на_график_не_хватает()
    {
        Assert.That(PerformanceChartLayout.Build(History(Point(500))).HasData, Is.False);
    }

    [Test]
    public void Замеры_одного_возраста_графика_не_дают()
    {
        Assert.That(PerformanceChartLayout.Build(History(Point(500), Point(500))).HasData, Is.False);
    }

    [Test]
    public void Ряд_растягивается_от_левого_края_к_правому()
    {
        var data = PerformanceChartLayout.Build(History(Point(2000), Point(1500), Point(1000), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.Delay[0].X, Is.Zero);
            Assert.That(data.Delay[1].X, Is.EqualTo(1d / 3).Within(0.0001));
            Assert.That(data.Delay[^1].X, Is.EqualTo(1));
        });
    }

    [Test]
    public void Спокойный_отклик_не_растягивается_на_всю_высоту()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.PeakDelayMs, Is.Zero);
            Assert.That(data.DelayScaleMs, Is.EqualTo(PerformanceChartLayout.MinDelayScaleMs));
            Assert.That(data.Delay[0].Y, Is.EqualTo(1));
        });
    }

    [Test]
    public void Пик_отклика_задаёт_шкалу_и_упирается_в_верх()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, delayMs: 200), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.PeakDelayMs, Is.EqualTo(200));
            Assert.That(data.DelayScaleMs, Is.EqualTo(200));
            Assert.That(data.Delay[0].Y, Is.Zero);
            Assert.That(data.Delay[1].Y, Is.EqualTo(1));
        });
    }

    [Test]
    public void Память_растягивается_по_диапазону_окна_а_не_от_нуля()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(1500, managed: 400),
            Point(1000, managed: 500),
            Point(500, managed: 600)));

        Assert.Multiple(() =>
        {
            Assert.That(data.MemoryMinBytes, Is.EqualTo(400));
            Assert.That(data.MemoryMaxBytes, Is.EqualTo(600));
            Assert.That(data.Memory[0].Y, Is.EqualTo(1));
            Assert.That(data.Memory[1].Y, Is.EqualTo(0.5));
            Assert.That(data.Memory[2].Y, Is.Zero);
        });
    }

    [Test]
    public void Неподвижная_память_рисуется_посередине()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, managed: 500), Point(500, managed: 500)));

        Assert.That(data.Memory.Select(static point => point.Y), Is.All.EqualTo(0.5));
    }

    [Test]
    public void Полоса_операции_занимает_половины_соседних_промежутков()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(2000),
            Point(1500, operation: "Сканирование"),
            Point(1000),
            Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.Bands, Has.Count.EqualTo(1));
            Assert.That(data.Bands[0].Start, Is.EqualTo(1d / 6).Within(0.0001));
            Assert.That(data.Bands[0].End, Is.EqualTo(0.5).Within(0.0001));
        });
    }

    [Test]
    public void Полоса_у_края_окна_доходит_до_края()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(2000, operation: "Сканирование"),
            Point(1500),
            Point(1000),
            Point(500, operation: "Синхронизация")));

        Assert.Multiple(() =>
        {
            Assert.That(data.Bands[0].Start, Is.Zero);
            Assert.That(data.Bands[^1].End, Is.EqualTo(1));
        });
    }

    [Test]
    public void Соседние_замеры_одной_операции_сливаются_в_одну_полосу()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(2000, operation: "Сканирование"),
            Point(1500, operation: "Сканирование"),
            Point(1000, operation: "Сравнение"),
            Point(500, operation: "Сканирование")));

        Assert.That(data.Bands.Select(static band => band.Name),
            Is.EqualTo(new[] { "Сканирование", "Сравнение", "Сканирование" }));
    }

    [Test]
    public void Операция_после_паузы_даёт_две_полосы_а_не_одну()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(2000, operation: "Сканирование"),
            Point(1500),
            Point(1000),
            Point(500, operation: "Сканирование")));

        Assert.Multiple(() =>
        {
            Assert.That(data.Bands, Has.Count.EqualTo(2));
            Assert.That(data.Bands[0].End, Is.LessThan(data.Bands[1].Start));
        });
    }

    [Test]
    public void Окно_без_операций_полос_не_даёт()
    {
        Assert.That(PerformanceChartLayout.Build(History(Point(1000), Point(500))).Bands, Is.Empty);
    }

    [Test]
    public void Подпись_памяти_называет_диапазон_только_когда_он_есть()
    {
        var moving = PerformanceChartLayout.Build(History(Point(1000, managed: 1024), Point(500, managed: 2048)));
        var still = PerformanceChartLayout.Build(History(Point(1000, managed: 2048), Point(500, managed: 2048)));

        Assert.Multiple(() =>
        {
            Assert.That(PerformanceFormat.ChartMemory(moving), Does.Contain("–"));
            Assert.That(PerformanceFormat.ChartMemory(still), Does.Not.Contain("–"));
        });
    }

    [Test]
    public void Подпись_окна_называет_охват_и_число_замеров()
    {
        var history = new PerformanceHistory(DateTime.UnixEpoch, 95, 0, 0, 0, 0,
            [Point(1000), Point(500)]);

        Assert.That(PerformanceFormat.ChartWindow(PerformanceChartLayout.Build(history)), Is.EqualTo("за 1:35 · 2 замера"));
    }

    private static PerformanceHistory History(params PerformancePoint[] points)
    {
        return new(DateTime.UnixEpoch, 0, 0, 0, 0, 0, [.. points]);
    }

    private static PerformancePoint Point(double ageMs, double delayMs = 0, long managed = 0, string? operation = null)
    {
        return new(ageMs, delayMs, managed, 0, 0, 0, 0, operation);
    }
}
