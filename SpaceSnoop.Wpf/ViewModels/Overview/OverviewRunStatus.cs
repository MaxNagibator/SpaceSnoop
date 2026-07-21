namespace SpaceSnoop.Wpf.ViewModels.Overview;

public enum OverviewRunStatus
{
    None = 0,
    Comparing = 1,
    Compared = 2,
    Unavailable = 3,
    Overlap = 4,
    Error = 5,
    Syncing = 6,
    Synced = 7,
    Skipped = 8,
}
