namespace SpaceSnoop.Wpf.Mcp;

internal sealed record McpAppState(
    string App,
    string Version,
    bool Elevated,
    string? Page,
    bool MutationsAllowed,
    int ProfileCount,
    McpScanState Scan,
    McpSyncState Sync);

internal sealed record McpScanState(
    string SelectedPath,
    string ResultPath,
    bool Busy,
    bool HasResult,
    string Size,
    string Files,
    string Directories,
    int MarkedForDeletion,
    double ElapsedSeconds,
    string Rate);

internal sealed record McpScanNavigation(string Page, McpScanState Scan, string? Navigation);

internal sealed record McpPerformance(
    bool Collecting,
    DateTime CapturedAtUtc,
    double SnapshotAgeMs,
    string Window,
    int SampleCount,
    double ObservedSpanSeconds,
    double UiDelayMs,
    double UiPeakMs,
    double UiAverageMs,
    long ManagedBytes,
    string Managed,
    long WorkingSetBytes,
    string WorkingSet,
    long WorkingSetPeakBytes,
    string WorkingSetPeak,
    int Gen0CollectionsTotal,
    int Gen1CollectionsTotal,
    int Gen2CollectionsTotal,
    double StartupSeconds,
    double FrameLastMs,
    double FramePeakMs,
    double FrameAverageMs,
    int FrameCount,
    int SlowFrameCount,
    McpPerformanceOperation? Operation,
    McpPerformanceHistory? History);

internal sealed record McpPerformanceHistory(
    DateTime CapturedAtUtc,
    double SpanSeconds,
    int RequestedSeconds,
    int RequestedPoints,
    int Points,
    int Folded,
    int Gen0CollectionsInWindow,
    int Gen1CollectionsInWindow,
    int Gen2CollectionsInWindow,
    IReadOnlyList<McpPerformancePoint> Timeline);

internal sealed record McpPerformancePoint(
    double AgeMs,
    double UiDelayMs,
    long ManagedBytes,
    long WorkingSetBytes,
    int Gen0CollectionsTotal,
    int Gen1CollectionsTotal,
    int Gen2CollectionsTotal,
    string? Operation);

internal sealed record McpPerformanceOperation(
    string Name,
    long Items,
    long Bytes,
    string Size,
    double ElapsedSeconds,
    double? ItemsPerSecond,
    double? BytesPerSecond,
    double? RemainingSeconds,
    string Summary);

internal sealed record McpArchivePlan(
    string Path,
    string Target,
    int EstimatedFiles,
    long Bytes,
    string Size,
    bool DeleteOriginal);

internal sealed record McpArchiveResult(
    string Path,
    string Target,
    int EstimatedFiles,
    bool OriginalDeleted,
    string Status,
    McpScanState Scan);

internal sealed record McpMarkResult(
    int Changed,
    IReadOnlyList<string> NotFound,
    IReadOnlyList<string> Rejected,
    string? RejectedReason,
    int MarkedTotal,
    string MarkedSize,
    McpScanState Scan);

internal sealed record McpCleanupReport(
    int MinimumAgeHours,
    long TotalBytes,
    string TotalSize,
    int TotalFiles,
    long ReclaimableBytes,
    string ReclaimableSize,
    IReadOnlyList<McpCleanupTarget> Targets);

internal sealed record McpCleanupTarget(
    string Id,
    string Name,
    string Description,
    CleanupTargetKind Kind,
    string Path,
    CleanupAvailability Availability,
    string AvailabilityHint,
    bool Cleanable,
    long Bytes,
    string Size,
    int Files,
    IReadOnlyList<string> Unreadable,
    int OmittedUnreadable,
    string? Error);

internal sealed record McpDrive(
    string Path,
    string Label,
    string Type,
    string? FileSystem,
    bool Ready,
    long TotalBytes,
    long FreeBytes,
    long UsedBytes,
    string Total,
    string Free,
    string Used,
    string? Error);

internal sealed record McpDockerBucket(
    string Type,
    int TotalCount,
    int Active,
    string Size,
    long SizeBytes,
    string Reclaimable,
    long ReclaimableBytes);

internal sealed record McpDockerObject(
    DockerObjectKind Kind,
    string Id,
    string Name,
    string Size,
    long SizeBytes,
    bool InUse,
    string Detail);

internal sealed record McpDockerReport(
    bool Available,
    string? Error,
    IReadOnlyList<McpDockerBucket> Buckets,
    IReadOnlyList<McpDockerObject>? Objects,
    int OmittedObjects);

internal sealed record McpSyncState(
    string LeftPath,
    string RightPath,
    SyncMode Mode,
    SyncWinner Winner,
    bool Mirror,
    string Exclusions,
    bool Busy,
    bool HasComparison,
    IReadOnlyDictionary<string, int> Files,
    IReadOnlyList<string> IncompleteDirectories);

internal sealed record McpProfile(
    string Id,
    string Name,
    string Left,
    string Right,
    SyncMode Mode,
    SyncWinner Winner,
    bool Mirror,
    string Exclusions,
    bool Enabled,
    bool SkipInBatch,
    string Schedule,
    string? Unavailable);

internal sealed record McpNavigationResult(string Page, McpSyncState Sync, string? Navigation, string? IgnoredParameters);

internal sealed record McpCapture(
    string Path,
    string Page,
    string? Element,
    string Theme,
    int Width,
    int Height,
    string? Navigation);

internal sealed record McpSyncResult(
    int Copied,
    int Deleted,
    int Succeeded,
    double ElapsedSeconds,
    long CopiedBytes,
    string CopiedSize,
    SyncVerifyState Verify,
    string VerifyHint,
    int ErrorCount,
    int MismatchCount,
    int OmittedErrors,
    int OmittedMismatches,
    IReadOnlyList<SyncError> Errors,
    IReadOnlyList<SyncMismatch> Mismatches,
    string Summary,
    McpSyncState Sync);
