using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.Mcp;

public interface ISyncAutomation
{
    string LeftPath { get; set; }

    string RightPath { get; set; }

    SyncMode Mode { get; set; }

    SyncWinner Winner { get; set; }

    bool Mirror { get; set; }

    string Exclusions { get; set; }

    bool IsBusy { get; }

    bool HasComparison { get; }

    bool HasPendingConflicts { get; }

    int IdenticalCount { get; }

    int LeftOnlyCount { get; }

    int RightOnlyCount { get; }

    int ModifiedCount { get; }

    int ConflictCount { get; }

    string SummaryText { get; }

    Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit);

    Func<SyncPlanExportModel>? CapturePlanBuilder(int entryLimit);

    Task CompareFromAutomationAsync(CancellationToken cancellationToken);

    Task<SyncRunResult?> SyncFromAutomationAsync(CancellationToken cancellationToken);
}
