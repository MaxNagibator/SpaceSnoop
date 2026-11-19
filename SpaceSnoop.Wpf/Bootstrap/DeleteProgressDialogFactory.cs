using KeepShell.Diagnostics;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class DeleteProgressDialogFactory(
    PerformanceMonitor monitor,
    PerformanceOperations operations,
    ShellPreferences preferences,
    IUiDispatcher uiDispatcher,
    ILogger<DeleteProgressDialogViewModel> logger)
{
    public DeleteProgressDialogViewModel Create(
        IReadOnlyList<SpaceBase> items,
        bool permanent,
        Action<SpaceBase>? unmark = null)
    {
        return new(items, permanent, monitor, operations, preferences, uiDispatcher, logger, unmark);
    }
}
