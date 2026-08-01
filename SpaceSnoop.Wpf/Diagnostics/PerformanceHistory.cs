using System.Collections.Immutable;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceHistory(
    DateTime CapturedAtUtc,
    double SpanSeconds,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    ImmutableArray<PerformancePoint> Points)
{
    public static PerformanceHistory Empty { get; } = new(DateTime.MinValue, 0, 0, 0, 0, []);
}
