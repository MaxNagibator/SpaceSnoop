using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ISyncAutomation
{
    string ISyncAutomation.LeftPath
    {
        get => Setup.LeftPath;
        set => Setup.LeftPath = value;
    }

    string ISyncAutomation.RightPath
    {
        get => Setup.RightPath;
        set => Setup.RightPath = value;
    }

    SyncMode ISyncAutomation.Mode
    {
        get => Setup.CurrentMode;
        set => Setup.SelectedModeIndex = SyncProfile.IndexOfMode(value);
    }

    SyncWinner ISyncAutomation.Winner
    {
        get => Setup.CurrentWinner;
        set => Setup.SelectedWinnerIndex = SyncProfile.IndexOfWinner(value);
    }

    bool ISyncAutomation.Mirror
    {
        get => Setup.Mirror;
        set => Setup.Mirror = value;
    }

    string ISyncAutomation.Exclusions
    {
        get => Setup.Exclusions;
        set => Setup.Exclusions = value;
    }

    bool ISyncAutomation.HasComparison => Ledger.HasResult;

    IReadOnlyList<string> ISyncAutomation.IncompleteDirectories => Operations.Result?.IncompleteDirectories() ?? [];

    bool ISyncAutomation.HasPendingConflicts => Operations.HasPending;

    int ISyncAutomation.IdenticalCount => Ledger.IdenticalCount;

    int ISyncAutomation.LeftOnlyCount => Ledger.LeftOnlyCount;

    int ISyncAutomation.RightOnlyCount => Ledger.RightOnlyCount;

    int ISyncAutomation.ModifiedCount => Ledger.ModifiedCount;

    int ISyncAutomation.ConflictCount => Ledger.ConflictCount;

    Func<ComparisonExportModel>? ISyncAutomation.CaptureExportBuilder(int entryLimit)
    {
        return Export.CaptureExportBuilder(entryLimit);
    }

    Func<SyncPlanExportModel>? ISyncAutomation.CapturePlanBuilder(int entryLimit)
    {
        return Export.CapturePlanBuilder(entryLimit);
    }

    Task ISyncAutomation.CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        return Operations.CompareFromAutomationAsync(cancellationToken);
    }

    Task<SyncRunResult?> ISyncAutomation.SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        return Operations.SyncFromAutomationAsync(cancellationToken);
    }
}
