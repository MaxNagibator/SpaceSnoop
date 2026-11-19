namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class DuplicateProgressDialogFactory(
    DuplicateFinder finder,
    IUiDispatcher uiDispatcher,
    ILogger<DuplicateProgressDialogViewModel> logger)
{
    public DuplicateProgressDialogViewModel Create(DuplicateRequest request)
    {
        return new(request, finder, uiDispatcher, logger);
    }
}
