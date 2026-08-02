using Microsoft.Extensions.Logging;

namespace SpaceSnoop.Core.UseCases;

public sealed record ExecuteSyncRequest(
    ComparisonResult Comparison,
    SyncConflictPolicy ConflictPolicy = SyncConflictPolicy.None,
    SyncDeleteUi DeleteUi = SyncDeleteUi.Interactive,
    bool VerifyAfterSync = false);

public sealed class ExecuteSyncUseCase(ILogger<SyncEngine> logger)
{
    public SyncReport Execute(ExecuteSyncRequest request, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var comparison = request.Comparison;

        if (request.ConflictPolicy == SyncConflictPolicy.SkipUnresolved)
        {
            comparison.ResolveAllConflicts(SyncAction.Skip);
        }

        var engine = new SyncEngine(logger, request.DeleteUi != SyncDeleteUi.Silent);
        var report = engine.Execute(comparison, cancel, progress);

        if (request.VerifyAfterSync)
        {
            engine.Verify(report, comparison.LeftPath, comparison.RightPath, cancel);
        }

        return report;
    }
}
