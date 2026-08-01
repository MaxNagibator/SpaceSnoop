using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PerformanceChartTests
{
    private const long Megabyte = 1024 * 1024;

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
            Assert.That(data.Points[0].Offset, Is.Zero);
            Assert.That(data.Points[1].Offset, Is.EqualTo(1d / 3).Within(0.0001));
            Assert.That(data.Points[^1].Offset, Is.EqualTo(1));
        });
    }

    [Test]
    public void Спокойный_отклик_не_растягивается_на_всю_высоту()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.PeakDelayMs, Is.Zero);
            Assert.That(data.DelayScale.Max, Is.EqualTo(PerformanceChartLayout.MinDelayScaleMs));
            Assert.That(data.Delay[0][0].Y, Is.EqualTo(1));
        });
    }

    [TestCase(145, 200)]
    [TestCase(45, 50)]
    [TestCase(210, 500)]
    [TestCase(700, 1000)]
    public void Верх_шкалы_отклика_круглое_число_над_пиком(double peakMs, double expected)
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, delayMs: peakMs), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.DelayScale.Max, Is.EqualTo(expected));
            Assert.That(data.Delay[0][0].Y, Is.EqualTo(1 - (peakMs / expected)).Within(0.0001));
        });
    }

    [Test]
    public void Пик_ниже_порога_не_упирается_в_верх_шкалы()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, delayMs: 145), Point(500)));

        Assert.That(data.Delay[0][0].Y, Is.GreaterThan(0.2));
    }

    [Test]
    public void Шкала_отклика_подписана_от_нуля_до_верха()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, delayMs: 145), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.DelayScale.Ticks[0], Is.EqualTo(new PerformanceTick(1, "0")));
            Assert.That(data.DelayScale.Ticks[^1], Is.EqualTo(new PerformanceTick(0, "200")));
        });
    }

    [Test]
    public void Мелкая_дрожь_памяти_не_занимает_всю_высоту()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(1500, managed: (long)(52.7 * Megabyte)),
            Point(1000, managed: (long)(55.0 * Megabyte)),
            Point(500, managed: (long)(59.5 * Megabyte))));

        var span = data.Memory[0].Max(static point => point.Y) - data.Memory[0].Min(static point => point.Y);

        Assert.Multiple(() =>
        {
            Assert.That(span, Is.LessThan(0.7));
            Assert.That(data.MemoryScale.Min, Is.LessThanOrEqualTo(data.MemoryMinBytes));
            Assert.That(data.MemoryScale.Max, Is.GreaterThanOrEqualTo(data.MemoryMaxBytes));
        });
    }

    [Test]
    public void Шкала_памяти_подписана_размерами_и_не_уходит_ниже_нуля()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(1000, managed: 40 * Megabyte),
            Point(500, managed: 44 * Megabyte)));

        Assert.Multiple(() =>
        {
            Assert.That(data.MemoryScale.Min, Is.GreaterThanOrEqualTo(0));
            Assert.That(data.MemoryScale.Ticks, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(data.MemoryScale.Ticks[0].Label, Does.Contain("МБ"));
        });
    }

    [Test]
    public void Неподвижная_память_рисуется_около_середины()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(1000, managed: 57 * Megabyte),
            Point(500, managed: 57 * Megabyte)));

        Assert.That(data.Memory[0].Select(static point => point.Y), Is.All.InRange(0.25, 0.75));
    }

    [Test]
    public void Замеры_выше_порога_помечаются_просадками()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(2000, delayMs: 40),
            Point(1500, delayMs: AppDefaults.PerformanceHitchMs + 120),
            Point(1000, delayMs: 30),
            Point(500, delayMs: AppDefaults.PerformanceHitchMs)));

        Assert.Multiple(() =>
        {
            Assert.That(data.HasHitches, Is.True);
            Assert.That(data.Hitches, Has.Count.EqualTo(2));
            Assert.That(data.Hitches[^1].AgeMs, Is.EqualTo(500));
        });
    }

    [Test]
    public void Спокойное_окно_просадок_не_даёт()
    {
        Assert.That(PerformanceChartLayout.Build(History(Point(1000, delayMs: 120), Point(500))).HasHitches, Is.False);
    }

    [Test]
    public void Пропуск_замеров_рвёт_линию_на_два_отрезка()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(9000),
            Point(8500),
            Point(8000),
            Point(1000),
            Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.Delay, Has.Count.EqualTo(2));
            Assert.That(data.Delay[0], Has.Count.EqualTo(3));
            Assert.That(data.Delay[1], Has.Count.EqualTo(2));
            Assert.That(data.Memory, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Ровные_замеры_идут_одним_отрезком()
    {
        var data = PerformanceChartLayout.Build(History(Point(1500), Point(1000), Point(500)));

        Assert.That(data.Delay, Has.Count.EqualTo(1));
    }

    [Test]
    public void Опоздавший_тик_линию_не_рвёт()
    {
        var data = PerformanceChartLayout.Build(History(Point(1700), Point(1000), Point(500)));

        Assert.That(data.Delay, Has.Count.EqualTo(1));
    }

    [Test]
    public void Ось_времени_отсчитывает_назад_от_сейчас()
    {
        var data = PerformanceChartLayout.Build(History(Point(90_500), Point(500)));

        Assert.Multiple(() =>
        {
            Assert.That(data.TimeTicks[0], Is.EqualTo(new PerformanceTick(0, "−1:30")));
            Assert.That(data.TimeTicks[1].Label, Is.EqualTo("−45 с"));
            Assert.That(data.TimeTicks[^1], Is.EqualTo(new PerformanceTick(1, "сейчас")));
        });
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

    [Test]
    public void Вердикт_спокойного_окна_называет_пик_и_порог()
    {
        var data = PerformanceChartLayout.Build(History(Point(1000, delayMs: 145), Point(500)));

        Assert.That(PerformanceFormat.ChartVerdict(data),
            Is.EqualTo($"Просадок нет · пик 145 мс при пороге {AppDefaults.PerformanceHitchMs} мс"));
    }

    [Test]
    public void Вердикт_с_просадками_называет_их_число_и_свежесть()
    {
        var data = PerformanceChartLayout.Build(History(
            Point(40_000, delayMs: 800),
            Point(20_000, delayMs: 10),
            Point(10_000, delayMs: 640)));

        Assert.That(PerformanceFormat.ChartVerdict(data),
            Is.EqualTo($"2 просадки · последняя 10 с назад · пик 800 мс при пороге {AppDefaults.PerformanceHitchMs} мс"));
    }

    [Test]
    public void Вердикта_без_замеров_нет()
    {
        Assert.That(PerformanceFormat.ChartVerdict(PerformanceChartData.Empty), Is.Empty);
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
