namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceSnapshot(
    DateTime CapturedAtUtc,
    double UiDelayMs,
    double UiPeakMs,
    double UiAverageMs,
    int SampleCount,
    double ObservedSpanSeconds,
    long ManagedBytes,
    long WorkingSetBytes,
    long WorkingSetPeakBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    PerformanceHistoryStats History,
    double StartupSeconds,
    double FrameLastMs,
    double FramePeakMs,
    double FrameAverageMs,
    int FrameCount,
    int SlowFrameCount,
    PerformanceOperation? Operation)
{
    public static PerformanceSnapshot Empty { get; } = new(DateTime.MinValue, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, default, 0, 0, 0, 0, 0, 0, null);
}
