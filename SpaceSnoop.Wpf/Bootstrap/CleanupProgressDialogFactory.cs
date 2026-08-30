using KeepShell.Services.Platform;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class CleanupProgressDialogFactory(
    CleanupService service,
    IUiDispatcher uiDispatcher,
    ILogger<CleanupProgressDialogViewModel> logger)
{
    public CleanupProgressDialogViewModel Create(CleanupRequest request)
    {
        return new(request, service, uiDispatcher, logger);
    }
}
