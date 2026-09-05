using SpaceSnoop.Core;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed record SyncRunResult(SyncReport Report, TimeSpan Elapsed, SyncVerifyState Verify);
