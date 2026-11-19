namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncProfileViewModel
{
    private string? _savedPermanentNetworkDeleteTarget;
    private string? _draftPermanentNetworkDeleteTarget;

    private string? MirrorTarget => MirrorWarning
        ? SyncProfile.MirrorTarget(HeadlessSync.MapMode(SelectedModeIndex), SyncProfile.WinnerFromIndex(SelectedWinnerIndex), LeftPath, RightPath)
        : null;

    public bool MirrorTargetWithoutRecycleBin => MirrorTarget is { } target && UncPath.IsUnc(target);

    public string MirrorWarningText => SyncProfileValidation.FormatMirrorWarning(MirrorTargetWithoutRecycleBin, AllowPermanentNetworkDelete);

    public bool CanAllowPermanentNetworkDelete => MirrorTarget is not null;

    public bool AllowPermanentNetworkDelete
    {
        get => _draftPermanentNetworkDeleteTarget is { } target && string.Equals(target, MirrorTarget, StringComparison.Ordinal);
        set
        {
            if (SetProperty(ref _draftPermanentNetworkDeleteTarget, value ? MirrorTarget : null))
            {
                OnPropertyChanged(nameof(MirrorWarningText));
            }
        }
    }
}
