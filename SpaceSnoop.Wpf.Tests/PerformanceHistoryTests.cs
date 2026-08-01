using SpaceSnoop.Wpf.Diagnostics;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PerformanceHistoryTests
{
    private const int NoLimit = 1000;

    [Test]
    public void Пустое_кольцо_не_даёт_точек()
    {
        var history = new PerformanceHistoryBuffer(4).Capture(Stopwatch.GetTimestamp(), DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.Multiple(() =>
        {
            Assert.That(history.Points, Is.Empty);
            Assert.That(history.SpanSeconds, Is.Zero);
            Assert.That(history.CapturedAtUtc, Is.EqualTo(DateTime.UnixEpoch));
        });
    }

    [Test]
    public void Нулевой_предел_точек_даёт_пустую_историю()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);
        buffer.Add(Sample(now, 1, "Сканирование"));

        Assert.That(buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, 0).Points, Is.Empty);
    }

    [Test]
    public void Кольцо_вытесняет_старые_точки()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(3);

        buffer.Add(Sample(now, 4, "первая"));
        buffer.Add(Sample(now, 3, "вторая"));
        buffer.Add(Sample(now, 2, "третья"));
        buffer.Add(Sample(now, 1, "четвёртая"));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Count, Is.EqualTo(3));
            Assert.That(history.Points.Select(static point => point.Operation), Is.EqualTo(new[] { "вторая", "третья", "четвёртая" }));
        });
    }

    [Test]
    public void Точки_идут_от_старой_к_новой()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);

        buffer.Add(Sample(now, 3, "старая"));
        buffer.Add(Sample(now, 1, "свежая"));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.Multiple(() =>
        {
            Assert.That(history.Points[0].AgeMs, Is.EqualTo(3000).Within(50));
            Assert.That(history.Points[1].AgeMs, Is.EqualTo(1000).Within(50));
            Assert.That(history.SpanSeconds, Is.EqualTo(2).Within(0.05));
        });
    }

    [Test]
    public void Ограничение_по_числу_точек_оставляет_свежие()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        for (var ago = 5; ago >= 1; ago--)
        {
            buffer.Add(Sample(now, ago, $"точка {ago}"));
        }

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, 2);

        Assert.That(history.Points.Select(static point => point.Operation), Is.EqualTo(new[] { "точка 2", "точка 1" }));
    }

    [Test]
    public void Ограничение_по_времени_отбрасывает_старые_точки()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 30, "давняя"));
        buffer.Add(Sample(now, 4, "недавняя"));
        buffer.Add(Sample(now, 1, "свежая"));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.FromSeconds(10), NoLimit);

        Assert.That(history.Points.Select(static point => point.Operation), Is.EqualTo(new[] { "недавняя", "свежая" }));
    }

    [Test]
    public void Снятая_история_не_меняется_от_новых_точек()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);

        buffer.Add(Sample(now, 3, "первая"));
        buffer.Add(Sample(now, 2, "вторая"));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        buffer.Add(Sample(now, 1, "третья"));

        Assert.Multiple(() =>
        {
            Assert.That(history.Points, Has.Length.EqualTo(2));
            Assert.That(history.Points[^1].Operation, Is.EqualTo("вторая"));
        });
    }

    [Test]
    public void Сборки_мусора_считаются_за_окно_а_не_от_старта()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 30, "давняя", 5));
        buffer.Add(Sample(now, 4, "недавняя", 7));
        buffer.Add(Sample(now, 1, "свежая", 9));

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit).Gen0Collections, Is.EqualTo(4));
            Assert.That(buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.FromSeconds(10), NoLimit).Gen0Collections, Is.EqualTo(2));
        });
    }

    [Test]
    public void Одна_точка_не_даёт_ни_окна_ни_сборок()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);
        buffer.Add(Sample(now, 1, "единственная", 9));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.Multiple(() =>
        {
            Assert.That(history.SpanSeconds, Is.Zero);
            Assert.That(history.Gen0Collections, Is.Zero);
        });
    }

    [Test]
    public void Пустое_окно_наблюдения_не_покрывает_времени()
    {
        Assert.That(new PerformanceSamples(20).SpanSeconds(500), Is.Zero);
    }

    [Test]
    public void Окно_наблюдения_растёт_на_пропущенных_тиках()
    {
        var samples = new PerformanceSamples(20);

        samples.Add(0);
        samples.Add(0);
        samples.Add(1000);

        Assert.That(samples.SpanSeconds(500), Is.EqualTo(2.5).Within(0.001));
    }

    private static PerformanceSample Sample(long now, double agoSeconds, string operation, int gen0 = 0)
    {
        return new(now - (long)(agoSeconds * Stopwatch.Frequency), 0, 0, 0, gen0, 0, 0, operation);
    }
}
