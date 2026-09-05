namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class DeleteProgressDialogFactory(
    PerformanceMonitor performance,
    PerformanceRunTracker runs,
    ShellPreferences preferences,
    IUiDispatcher uiDispatcher,
    ILogger<DeleteProgressDialogViewModel> logger)
{
    public DeleteProgressDialogViewModel Create(IReadOnlyList<SpaceBase> items, bool permanent)
    {
        return new(items, permanent, performance, runs, preferences, uiDispatcher, logger);
    }
}
