using Microsoft.Extensions.Logging.Abstractions;
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
    public void Свёртка_в_бакеты_покрывает_всё_окно_а_не_его_хвост()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        for (var ago = 5; ago >= 1; ago--)
        {
            buffer.Add(Sample(now, ago, $"точка {ago}"));
        }

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, 2);

        Assert.Multiple(() =>
        {
            Assert.That(history.Points, Has.Length.EqualTo(2));
            Assert.That(history.Folded, Is.EqualTo(3));
            Assert.That(history.Points[0].AgeMs, Is.EqualTo(4000).Within(50));
            Assert.That(history.SpanSeconds, Is.EqualTo(4).Within(0.05));
        });
    }

    [Test]
    public void Свёртка_оставляет_в_бакете_худшую_задержку()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 6, "тихо", delayMs: 1));
        buffer.Add(Sample(now, 5, "просадка", delayMs: 900));
        buffer.Add(Sample(now, 4, "тихо", delayMs: 2));
        buffer.Add(Sample(now, 3, "тихо", delayMs: 3));
        buffer.Add(Sample(now, 2, "тихо", delayMs: 4));
        buffer.Add(Sample(now, 1, "тихо", delayMs: 5));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, 2);

        Assert.That(history.Points.Select(static point => point.UiDelayMs), Does.Contain(900));
    }

    [Test]
    public void Свёрнутая_точка_встаёт_по_границе_бакета_а_не_по_худшему_замеру()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 4, "просадка", delayMs: 900));
        buffer.Add(Sample(now, 3, "тихо", delayMs: 1));
        buffer.Add(Sample(now, 2, "тихо", delayMs: 2));
        buffer.Add(Sample(now, 1, "тихо", delayMs: 3));

        var points = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, 2).Points;

        Assert.Multiple(() =>
        {
            Assert.That(points[0].UiDelayMs, Is.EqualTo(900));
            Assert.That(points[0].AgeMs, Is.EqualTo(3000).Within(50));
            Assert.That(points[0].AgeMs - points[1].AgeMs, Is.EqualTo(2000).Within(50));
        });
    }

    [Test]
    public void Свёрнутыми_считаются_только_замеры_внутри_окна()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 30, "давняя"));
        buffer.Add(Sample(now, 4, "недавняя"));
        buffer.Add(Sample(now, 1, "свежая"));

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.FromSeconds(10), 1);

        Assert.Multiple(() =>
        {
            Assert.That(history.Points.Select(static point => point.Operation), Is.EqualTo(new[] { "свежая" }));
            Assert.That(history.Folded, Is.EqualTo(1));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(3)]
    public void Кольцо_любой_ёмкости_отдаёт_точки_от_старой_к_новой(int capacity)
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(capacity);

        for (var ago = 3; ago >= 1; ago--)
        {
            buffer.Add(Sample(now, ago, $"точка {ago}"));
        }

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.Multiple(() =>
        {
            Assert.That(history.Points, Has.Length.EqualTo(Math.Max(1, Math.Min(capacity, 3))));
            Assert.That(history.Points[^1].Operation, Is.EqualTo("точка 1"));
            Assert.That(history.Points.Select(static point => point.AgeMs), Is.Ordered.Descending);
        });
    }

    [Test]
    public void Заполненное_под_завязку_кольцо_не_путает_порядок()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(3);

        for (var ago = 3; ago >= 1; ago--)
        {
            buffer.Add(Sample(now, ago, $"точка {ago}"));
        }

        var history = buffer.Capture(now, DateTime.UnixEpoch, TimeSpan.Zero, NoLimit);

        Assert.That(history.Points.Select(static point => point.Operation),
            Is.EqualTo(new[] { "точка 3", "точка 2", "точка 1" }));
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
    public void Скаляры_кольца_снимаются_без_материализации_точек()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 30, "давняя", 5));
        buffer.Add(Sample(now, 4, "недавняя", 7));
        buffer.Add(Sample(now, 1, "свежая", 9));

        var stats = buffer.Stats();

        Assert.Multiple(() =>
        {
            Assert.That(stats.SampleCount, Is.EqualTo(3));
            Assert.That(stats.SpanSeconds, Is.EqualTo(29).Within(0.5));
            Assert.That(stats.Gen0Collections, Is.EqualTo(4));
            Assert.That(new PerformanceHistoryBuffer(4).Stats().SampleCount, Is.Zero);
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

    [Test]
    public void Время_старта_переживает_первую_публикацию_снимка()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);

        monitor.ReportStartup(TimeSpan.FromSeconds(1.25));
        monitor.Start();
        monitor.Stop();

        Assert.That(monitor.Snapshot.StartupSeconds, Is.EqualTo(1.25));
    }

    [Test]
    public void Повторный_старт_не_сбрасывает_наблюдение()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);

        monitor.Start();
        var first = monitor.Snapshot.CapturedAtUtc;
        monitor.Start();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Snapshot.CapturedAtUtc, Is.EqualTo(first));
            Assert.That(monitor.IsRunning, Is.True);
        });
    }

    [Test]
    public void Остановка_не_оставляет_наблюдение_прежним()
    {
        using var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);

        monitor.TryReportOperation(new("Сканирование", 10, 20, TimeSpan.FromSeconds(1)), null);
        monitor.Start();
        monitor.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.IsRunning, Is.False);
            Assert.That(monitor.Snapshot.Operation, Is.Null);
        });
    }

    [Test]
    public void После_освобождения_монитор_не_запускается_заново()
    {
        var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);

        monitor.Start();
        monitor.Dispose();
        monitor.Start();

        Assert.That(monitor.IsRunning, Is.False);
    }

    [Test]
    public void Просадки_читаются_из_кольца_без_свёртки()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        buffer.Add(Sample(now, 6, "Сканирование", delayMs: 900));
        buffer.Add(Sample(now, 5, "Сканирование", delayMs: 700));
        buffer.Add(Sample(now, 4, "Сканирование", delayMs: 4));
        buffer.Add(Sample(now, 1, "Сравнение", delayMs: 600));

        var hitches = buffer.Hitches(now, DateTime.UnixEpoch, 500, 10);

        Assert.Multiple(() =>
        {
            Assert.That(hitches.Total, Is.EqualTo(3));
            Assert.That(hitches.Rows.Select(static row => row.DelayMs), Is.EqualTo(new[] { 600d, 700d, 900d }));
            Assert.That(hitches.Rows[0].Operation, Is.EqualTo("Сравнение"));
            Assert.That(hitches.SpanSeconds, Is.EqualTo(5).Within(0.05));
        });
    }

    [Test]
    public void Предел_строк_режет_список_просадок_но_не_счётчик()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(8);

        for (var ago = 5; ago >= 1; ago--)
        {
            buffer.Add(Sample(now, ago, $"точка {ago}", delayMs: 600));
        }

        var hitches = buffer.Hitches(now, DateTime.UnixEpoch, 500, 2);

        Assert.Multiple(() =>
        {
            Assert.That(hitches.Total, Is.EqualTo(5));
            Assert.That(hitches.Rows.Select(static row => row.Operation), Is.EqualTo(new[] { "точка 1", "точка 2" }));
        });
    }

    [Test]
    public void Время_просадки_отсчитывается_от_момента_съёма()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);
        var capturedAt = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);

        buffer.Add(Sample(now, 3, "Сканирование", delayMs: 900));

        var row = buffer.Hitches(now, capturedAt, 500, 10).Rows[0];

        Assert.That(row.TimeUtc, Is.EqualTo(capturedAt.AddSeconds(-3)).Within(TimeSpan.FromMilliseconds(50)));
    }

    [Test]
    public void Тихое_кольцо_не_придумывает_просадок()
    {
        var now = Stopwatch.GetTimestamp();
        var buffer = new PerformanceHistoryBuffer(4);

        buffer.Add(Sample(now, 2, "Сканирование", delayMs: 4));
        buffer.Add(Sample(now, 1, "Сканирование", delayMs: 6));

        var hitches = buffer.Hitches(now, DateTime.UnixEpoch, 500, 10);

        Assert.Multiple(() =>
        {
            Assert.That(hitches.Total, Is.Zero);
            Assert.That(hitches.Rows, Is.Empty);
            Assert.That(hitches.SpanSeconds, Is.EqualTo(1).Within(0.05));
            Assert.That(new PerformanceHistoryBuffer(4).Hitches(now, DateTime.UnixEpoch, 500, 10), Is.SameAs(PerformanceHitches.Empty));
        });
    }

    private static PerformanceSample Sample(long now, double agoSeconds, string operation, int gen0 = 0, double delayMs = 0)
    {
        return new(now - (long)(agoSeconds * Stopwatch.Frequency), delayMs, 0, 0, gen0, 0, 0, operation);
    }
}
