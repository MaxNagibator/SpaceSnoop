namespace SpaceSnoop.Wpf.Mcp;

internal sealed record McpAppState(
    string App,
    string Version,
    bool Elevated,
    string? Page,
    bool MutationsAllowed,
    int ProfileCount,
    McpSyncState Sync);

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
