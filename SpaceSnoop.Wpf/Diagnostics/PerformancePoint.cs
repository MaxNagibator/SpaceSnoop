namespace SpaceSnoop.Wpf.Diagnostics;

public readonly record struct PerformancePoint(
    double AgeMs,
    double UiDelayMs,
    long ManagedBytes,
    long WorkingSetBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    string? Operation);
