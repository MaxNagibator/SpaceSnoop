using Microsoft.Extensions.Logging;

namespace SpaceSnoop.Core.UseCases;

public sealed record ExecuteSyncRequest(
    ComparisonResult Comparison,
    SyncConflictPolicy ConflictPolicy = SyncConflictPolicy.None,
    SyncDeleteUi DeleteUi = SyncDeleteUi.Interactive,
    bool VerifyAfterSync = false,
    bool RecycleOverwritten = false,
    bool AllowPermanentNetworkDelete = false);

public sealed class ExecuteSyncUseCase(ILogger<SyncEngine> logger)
{
    private readonly Func<string, bool> _supportsRecycleBin = RecycleBin.SupportsRecycleBin;

    internal ExecuteSyncUseCase(ILogger<SyncEngine> logger, Func<string, bool> supportsRecycleBin)
        : this(logger)
    {
        _supportsRecycleBin = supportsRecycleBin;
    }

    public SyncReport Execute(ExecuteSyncRequest request, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var comparison = request.Comparison;

        if (request.ConflictPolicy == SyncConflictPolicy.SkipUnresolved)
        {
            comparison.ResolveAllConflicts(SyncAction.Skip);
        }

        var engine = new SyncEngine(
            logger,
            request.DeleteUi != SyncDeleteUi.Silent,
            request.RecycleOverwritten,
            request.AllowPermanentNetworkDelete,
            _supportsRecycleBin);
        var report = engine.Execute(comparison, cancel, progress);

        if (request.VerifyAfterSync)
        {
            engine.Verify(report, comparison.LeftPath, comparison.RightPath, cancel);
        }

        return report;
    }
}
