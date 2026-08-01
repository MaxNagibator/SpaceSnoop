namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceSnapshot(
    double UiDelayMs,
    double UiPeakMs,
    double UiAverageMs,
    int SampleCount,
    double ObservedSpanSeconds,
    long ManagedBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    PerformanceOperation? Operation)
{
    public static PerformanceSnapshot Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null);
}
