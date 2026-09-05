using System.Windows;

namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformanceBand(double Start, double End, string Name);

public readonly record struct PerformanceTick(double Offset, string Label);

public readonly record struct PerformanceMarker(double X, double Y, double DelayMs, double AgeMs);

public readonly record struct PerformanceCursorPoint(
    double Offset,
    double AgeMs,
    double UiDelayMs,
    long ManagedBytes,
    string? Operation);

public sealed record PerformanceScale(double Min, double Max, IReadOnlyList<PerformanceTick> Ticks)
{
    public static PerformanceScale Empty { get; } = new(0, 0, []);

    public double Offset(double value)
    {
        return Max > Min ? 1 - Math.Clamp((value - Min) / (Max - Min), 0, 1) : 0.5;
    }
}

public sealed record PerformanceChartData(
    IReadOnlyList<IReadOnlyList<Point>> Delay,
    IReadOnlyList<IReadOnlyList<Point>> Memory,
    IReadOnlyList<PerformanceBand> Bands,
    IReadOnlyList<PerformanceMarker> Hitches,
    IReadOnlyList<PerformanceTick> TimeTicks,
    IReadOnlyList<PerformanceCursorPoint> Points,
    PerformanceScale DelayScale,
    PerformanceScale MemoryScale,
    double PeakDelayMs,
    long MemoryMinBytes,
    long MemoryMaxBytes,
    double SpanSeconds,
    int Folded = 0)
{
    public static PerformanceChartData Empty { get; } =
        new([], [], [], [], [], [], PerformanceScale.Empty, PerformanceScale.Empty, 0, 0, 0, 0);

    public bool HasData => Points.Count > 1;

    public bool HasHitches => Hitches.Count > 0;

    public bool ShowDots => Points.Count is > 0 and <= AppDefaults.PerformanceChartDotLimit;
}
