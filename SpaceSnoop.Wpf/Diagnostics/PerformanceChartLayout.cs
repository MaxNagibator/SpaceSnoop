using System.Collections.Immutable;
using System.Windows;

namespace SpaceSnoop.Wpf.Diagnostics;

public static class PerformanceChartLayout
{
    public const double MinDelayScaleMs = 50;
    public const double MemoryMinSpanFraction = 0.15;
    public const long MemoryMinSpanBytes = 4L * 1024 * 1024;

    private const double GapFactor = 1.75;
    private const int DelayTickCount = 2;
    private const int MemoryTickDivisions = 8;

    private static readonly long[] MemoryUnits = [1024L * 1024 * 1024, 1024L * 1024, 1024L, 1L];

    private static readonly string[] MemoryUnitNames = ["ГБ", "МБ", "КБ", "байт"];

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

        var delayScale = BuildDelayScale(peak);
        var memoryScale = BuildMemoryScale(memoryMin, memoryMax);
        var offsets = new double[points.Length];
        var cursor = new PerformanceCursorPoint[points.Length];
        var hitches = new List<PerformanceMarker>();

        for (var index = 0; index < points.Length; index++)
        {
            var point = points[index];
            offsets[index] = (oldest - point.AgeMs) / span;
            cursor[index] = new(offsets[index], point.AgeMs, point.UiDelayMs, point.ManagedBytes, point.Operation);

            if (point.UiDelayMs >= AppDefaults.PerformanceHitchMs)
            {
                hitches.Add(new(offsets[index], delayScale.Offset(point.UiDelayMs), point.UiDelayMs, point.AgeMs));
            }
        }

        var ranges = Split(points);

        return new(BuildSeries(ranges, offsets, index => delayScale.Offset(points[index].UiDelayMs)),
            BuildSeries(ranges, offsets, index => memoryScale.Offset(points[index].ManagedBytes)),
            BuildBands(points, offsets),
            hitches,
            BuildTimeTicks(span),
            cursor,
            delayScale,
            memoryScale,
            peak,
            memoryMin,
            memoryMax,
            history.SpanSeconds,
            history.Folded);
    }

    internal static double NiceCeil(double value)
    {
        if (value <= 0)
        {
            return 0;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
        var normalized = value / magnitude;
        var step = normalized switch
        {
            <= 1 => 1,
            <= 2 => 2,
            <= 5 => 5,
            _ => 10,
        };

        return step * magnitude;
    }

    private static PerformanceScale BuildDelayScale(double peakMs)
    {
        var max = NiceCeil(Math.Max(peakMs, MinDelayScaleMs));
        var step = max / DelayTickCount;
        var ticks = new PerformanceTick[DelayTickCount + 1];

        for (var index = 0; index <= DelayTickCount; index++)
        {
            var value = step * index;
            ticks[index] = new(1 - ((double)index / DelayTickCount), $"{Math.Round(value):N0}");
        }

        return new(0, max, ticks);
    }

    private static PerformanceScale BuildMemoryScale(long minBytes, long maxBytes)
    {
        var unit = MemoryUnit(maxBytes);
        var low = (double)minBytes / unit;
        var high = (double)maxBytes / unit;
        var minSpan = Math.Max(high * MemoryMinSpanFraction, (double)MemoryMinSpanBytes / unit);

        if (high - low < minSpan)
        {
            var center = (high + low) / 2;
            low = center - (minSpan / 2);
            high = center + (minSpan / 2);

            if (low < 0)
            {
                high -= low;
                low = 0;
            }
        }

        var step = NiceCeil((high - low) / MemoryTickDivisions);

        if (step <= 0)
        {
            return new(minBytes, maxBytes, []);
        }

        low = Math.Max(0, Math.Floor(low / step) * step);
        high = Math.Ceiling(high / step) * step;

        var ticks = new List<PerformanceTick>();
        var range = high - low;

        for (var value = low; value <= high + (step / 2); value += step)
        {
            ticks.Add(new(1 - ((value - low) / range), MemoryLabel(value, unit)));
        }

        return new(low * unit, high * unit, ticks);
    }

    private static string MemoryLabel(double value, long unit)
    {
        return value > 0
            ? SizeFormatter.Format((long)Math.Round(value * unit))
            : $"0 {MemoryUnitNames[Array.IndexOf(MemoryUnits, unit)]}";
    }

    private static long MemoryUnit(long maxBytes)
    {
        return MemoryUnits.FirstOrDefault(unit => maxBytes >= unit, 1);
    }

    private static IReadOnlyList<PerformanceTick> BuildTimeTicks(double spanMs)
    {
        return
        [
            new(0, PerformanceFormat.Age(spanMs / 1000)),
            new(0.5, PerformanceFormat.Age(spanMs / 2000)),
            new(1, "сейчас"),
        ];
    }

    private static IReadOnlyList<IReadOnlyList<Point>> BuildSeries(
        IReadOnlyList<(int Start, int End)> ranges,
        double[] offsets,
        Func<int, double> value)
    {
        var series = new List<IReadOnlyList<Point>>(ranges.Count);

        foreach (var (start, end) in ranges)
        {
            var segment = new Point[end - start + 1];

            for (var index = start; index <= end; index++)
            {
                segment[index - start] = new(offsets[index], value(index));
            }

            series.Add(segment);
        }

        return series;
    }

    private static IReadOnlyList<(int Start, int End)> Split(ImmutableArray<PerformancePoint> points)
    {
        var threshold = GapThreshold(points);
        var ranges = new List<(int Start, int End)>();
        var start = 0;

        for (var index = 1; index < points.Length; index++)
        {
            if (points[index - 1].AgeMs - points[index].AgeMs <= threshold)
            {
                continue;
            }

            ranges.Add((start, index - 1));
            start = index;
        }

        ranges.Add((start, points.Length - 1));

        return ranges;
    }

    private static double GapThreshold(ImmutableArray<PerformancePoint> points)
    {
        var gaps = new double[points.Length - 1];

        for (var index = 1; index < points.Length; index++)
        {
            gaps[index - 1] = points[index - 1].AgeMs - points[index].AgeMs;
        }

        Array.Sort(gaps);

        return Math.Max(gaps[gaps.Length / 2], AppDefaults.PerformanceSampleIntervalMs) * GapFactor;
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
