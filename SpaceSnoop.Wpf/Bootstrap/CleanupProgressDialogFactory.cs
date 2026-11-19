namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class CleanupProgressDialogFactory(
    CleanupService service,
    IUiDispatcher uiDispatcher,
    PerformanceOperations operations,
    ILogger<CleanupProgressDialogViewModel> logger)
{
    public CleanupProgressDialogViewModel Create(CleanupRequest request)
    {
        return new(request, service, uiDispatcher, logger, operations);
    }
}
