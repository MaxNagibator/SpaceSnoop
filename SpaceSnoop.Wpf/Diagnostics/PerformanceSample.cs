namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformanceSample(
    long Timestamp,
    double UiDelayMs,
    long ManagedBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    string? Operation);
