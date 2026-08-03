namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformanceHistoryStats(
    double SpanSeconds,
    int SampleCount,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);
