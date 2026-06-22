namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class DeleteProgressDialogFactory(ILogger<DeleteProgressDialogViewModel> logger)
{
    public DeleteProgressDialogViewModel Create(IReadOnlyList<SpaceBase> items, bool permanent)
    {
        return new(items, permanent, logger);
    }
}
