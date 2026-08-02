namespace SpaceSnoop.Wpf.ViewModels.Overview;

internal static class OverviewNarrative
{
    internal static (int Synced, int Failed, int Skipped) Tally(OverviewRowViewModel row)
    {
        return row.Status switch
        {
            OverviewRunStatus.Synced when row.SyncErrors == 0 => (1, 0, 0),
            OverviewRunStatus.Synced or OverviewRunStatus.Error => (0, 1, 0),
            _ => (0, 0, 1),
        };
    }

    internal static PlannedActions? SumPlans(IEnumerable<OverviewRowViewModel> rows)
    {
        var plans = rows.Select(static row => row.Comparison?.CountPlannedActions()).OfType<PlannedActions>().ToList();

        if (plans.Count == 0)
        {
            return null;
        }

        return plans.Aggregate(PlannedActions.Empty, static (total, plan) => new PlannedActions(
            total.NewCopies + plan.NewCopies,
            total.ModifiedCopies + plan.ModifiedCopies,
            total.Deletes + plan.Deletes,
            total.DirCopies + plan.DirCopies,
            total.DirDeletes + plan.DirDeletes)
        {
            NewCopyBytes = total.NewCopyBytes + plan.NewCopyBytes,
            ModifiedCopyBytes = total.ModifiedCopyBytes + plan.ModifiedCopyBytes,
            CopyToLeftBytes = total.CopyToLeftBytes + plan.CopyToLeftBytes,
            CopyToRightBytes = total.CopyToRightBytes + plan.CopyToRightBytes,
            OverwriteLeftBytes = total.OverwriteLeftBytes + plan.OverwriteLeftBytes,
            OverwriteRightBytes = total.OverwriteRightBytes + plan.OverwriteRightBytes,
            DeleteFileBytes = total.DeleteFileBytes + plan.DeleteFileBytes,
            DeleteDirBytes = total.DeleteDirBytes + plan.DeleteDirBytes,
        });
    }

    internal static (string Message, StatusSeverity Severity) DescribeOutcome(string caption, int ok, int failed)
    {
        var severity = (failed, ok) switch
        {
            ( > 0, _) => StatusSeverity.Error,
            (_, 0) => StatusSeverity.Warning,
            _ => StatusSeverity.Success,
        };

        return (caption, severity);
    }
}
