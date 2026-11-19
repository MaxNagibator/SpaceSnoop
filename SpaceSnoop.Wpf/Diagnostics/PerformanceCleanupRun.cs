namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceCleanupRun(
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    int Deleted,
    int Skipped,
    long LogicalDeletedBytes,
    bool Cancelled,
    string? Failure,
    IReadOnlyList<string> ReportedErrors);
