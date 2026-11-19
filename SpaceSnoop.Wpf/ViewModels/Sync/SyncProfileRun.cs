namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed record SyncProfileRun(string ProfileId, ComparisonResult? Comparison, SyncReport? Report, long ElapsedMs, SyncVerifyState Verify);
