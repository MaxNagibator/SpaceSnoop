using System.Collections.Immutable;
using System.Windows;

namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformanceBand(double Start, double End, string Name);

public sealed record PerformanceChartData(
    IReadOnlyList<Point> Delay,
    IReadOnlyList<Point> Memory,
    IReadOnlyList<PerformanceBand> Bands,
    double PeakDelayMs,
    double DelayScaleMs,
    long MemoryMinBytes,
    long MemoryMaxBytes,
    double SpanSeconds)
{
    public static PerformanceChartData Empty { get; } = new([], [], [], 0, 0, 0, 0, 0);

    public bool HasData => Delay.Count > 1;
}

public static class PerformanceChartLayout
{
    public const double MinDelayScaleMs = 50;

    public static PerformanceChartData Build(PerformanceHistory history)
    {
        var points = history.Points;

        if (points.Length < 2)
        {
            return PerformanceChartData.Empty;
        }

        var oldest = points[0].AgeMs;
        var span = oldest - points[^1].AgeMs;

        if (span <= 0)
        {
            return PerformanceChartData.Empty;
        }

        var peak = 0d;
        var memoryMin = long.MaxValue;
        var memoryMax = long.MinValue;

        foreach (var point in points)
        {
            peak = Math.Max(peak, point.UiDelayMs);
            memoryMin = Math.Min(memoryMin, point.ManagedBytes);
            memoryMax = Math.Max(memoryMax, point.ManagedBytes);
        }

        var delayScale = Math.Max(MinDelayScaleMs, peak);
        var memorySpan = (double)(memoryMax - memoryMin);
        var offsets = new double[points.Length];
        var delay = new Point[points.Length];
        var memory = new Point[points.Length];

        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            offsets[index] = (oldest - point.AgeMs) / span;

            delay[index] = new(offsets[index], 1 - Math.Clamp(point.UiDelayMs / delayScale, 0, 1));
            memory[index] = new(offsets[index], memorySpan > 0 ? 1 - ((point.ManagedBytes - memoryMin) / memorySpan) : 0.5);
        }

        return new(delay, memory, BuildBands(points, offsets), peak, delayScale, memoryMin, memoryMax, history.SpanSeconds);
    }

    private static IReadOnlyList<PerformanceBand> BuildBands(ImmutableArray<PerformancePoint> points, double[] offsets)
    {
        var bands = new List<PerformanceBand>();

        for (var index = 0; index < points.Length; index++)
        {
            var name = points[index].Operation;

            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            var start = index == 0 ? 0 : (offsets[index - 1] + offsets[index]) / 2;
            var end = index == points.Length - 1 ? 1 : (offsets[index] + offsets[index + 1]) / 2;

            if (bands.Count > 0 && bands[^1].Name == name && bands[^1].End >= start)
            {
                bands[^1] = bands[^1] with { End = end };
                continue;
            }

            bands.Add(new(start, end, name));
        }

        return bands;
    }
}
