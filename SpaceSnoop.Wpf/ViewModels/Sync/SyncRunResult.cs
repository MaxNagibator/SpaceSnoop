using SpaceSnoop.Core;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal sealed record SyncRunResult(SyncReport Report, TimeSpan Elapsed, SyncVerifyState Verify);
