using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class PerformanceHistoryBenchmarks
{
    private const int MaxDelayMs = 700;
    private const long ManagedBytes = 40L * 1024 * 1024;
    private const long WorkingSetBytes = 580L * 1024 * 1024;
    private const string OperationName = "Сканирование";

    private readonly PerformanceHistoryBuffer _buffer = new(AppDefaults.PerformanceHistorySamples);

    private PerformanceSample _next;
    private DateTime _capturedAtUtc;
    private long _now;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(SyntheticTree.Seed);
        var step = Stopwatch.Frequency * AppDefaults.PerformanceSampleIntervalMs / 1000;

        _now = Stopwatch.GetTimestamp();
        _capturedAtUtc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = AppDefaults.PerformanceHistorySamples - 1; i >= 0; i--)
        {
            _buffer.Add(Sample(_now - (i * step), random, AppDefaults.PerformanceHistorySamples - i));
        }

        _next = Sample(_now, random, AppDefaults.PerformanceHistorySamples);
    }

    [Benchmark]
    public PerformanceHistoryStats Tick()
    {
        _buffer.Add(_next);

        return _buffer.Stats();
    }

    [Benchmark]
    [Arguments(60)]
    [Arguments(AppDefaults.PerformanceHistoryPointsMax)]
    public PerformanceHistory Capture(int points)
    {
        return _buffer.Capture(_now, _capturedAtUtc, TimeSpan.FromSeconds(AppDefaults.PerformanceHistorySecondsMax), points);
    }

    private static PerformanceSample Sample(long timestamp, Random random, int index)
    {
        return new(
            timestamp,
            random.Next(MaxDelayMs),
            ManagedBytes + random.Next(1 << 20),
            WorkingSetBytes + random.Next(1 << 20),
            index / 3,
            index / 20,
            index / 200,
            OperationName);
    }
}
