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
    int MarkedForDeletion);

internal sealed record McpScanNavigation(string Page, McpScanState Scan);

internal sealed record McpArchivePlan(
    string Path,
    string Target,
    int Files,
    long Bytes,
    string Size,
    bool DeleteOriginal);

internal sealed record McpArchiveResult(
    string Path,
    string Target,
    int Files,
    bool OriginalDeleted,
    string Status,
    McpScanState Scan);

internal sealed record McpMarkResult(
    int Changed,
    IReadOnlyList<string> NotFound,
    int MarkedTotal,
    string MarkedSize,
    McpScanState Scan);

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
    IReadOnlyDictionary<string, int> Files);

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

internal sealed record McpNavigationResult(string Page, McpSyncState Sync);

internal sealed record McpSyncResult(
    int Copied,
    int Deleted,
    int Succeeded,
    IReadOnlyList<SyncError> Errors,
    IReadOnlyList<SyncMismatch> Mismatches,
    McpSyncState Sync);
