using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class CleanupProgressDialogFactory(
    CleanupService service,
    ILogger<CleanupProgressDialogViewModel> logger)
{
    public CleanupProgressDialogViewModel Create(CleanupRequest request)
    {
        return new(request, service, logger);
    }
}
