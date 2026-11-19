namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelledCaption))]
    private string _cancelledPath = string.Empty;

    [ObservableProperty]
    private bool _cancelledReleasedPrevious;

    public bool TreeVisible => HasResult && ViewMode == ScanViewMode.Tree;

    public bool TreemapVisible => HasResult && ViewMode == ScanViewMode.Treemap;

    public bool DuplicatesVisible => HasResult && ViewMode == ScanViewMode.Duplicates;

    public bool ShowTargetPicker => !IsScanning && (!HasResult || IsPickerOpen);

    public bool ShowScanResult => !ShowTargetPicker;

    public bool ShowScanningState => !HasResult && IsScanning;

    public bool ShowSummary => HasResult && !IsScanning && !IsPickerOpen;

    public bool ShowStaleResult => IsScanning
        && HasResult
        && CurrentRoot is { } root
        && !string.IsNullOrWhiteSpace(ScanTargetPath)
        && !ScanTreeEditor.SamePath(root.AbsolutePath, ScanTargetPath);

    public string StaleResultCaption => CurrentRoot is { } root
        ? $"Прежний результат: {root.AbsolutePath}{OtherRootsSuffix} – новый появится после обхода {ScanTargetPath}"
        : string.Empty;

    public string CancelledCaption => $"Сканирование {CancelledPath} остановлено";

    public bool HasTarget => !string.IsNullOrWhiteSpace(SelectedDrive);

    public string StartCaption => HasTarget
        ? $"Сканировать {DriveItem.ShortName(SelectedDrive)}"
        : "Выберите диск или каталог";

    private string OtherRootsSuffix => Roots.Count > 1 ? $" и ещё {Roots.Count - 1}" : string.Empty;

    private bool CanRescanCancelled()
    {
        return !IsBusy && !_isPreparing && ScanWasCancelled && !string.IsNullOrWhiteSpace(CancelledPath);
    }

    [RelayCommand(CanExecute = nameof(CanRescanCancelled))]
    private async Task RescanCancelledAsync()
    {
        var path = CancelledPath;

        if (string.IsNullOrWhiteSpace(path) || _isPreparing)
        {
            return;
        }

        SetPreparing(true);
        bool exists;

        try
        {
            exists = await Task.Run(() => ScanTargetProbe.DirectoryExists(path), CancellationToken.None);
        }
        finally
        {
            SetPreparing(false);
        }

        if (IsBusy || !ScanWasCancelled || !string.Equals(CancelledPath, path, StringComparison.Ordinal))
        {
            return;
        }

        if (!exists)
        {
            ScanWasCancelled = false;
            _dialogs.Warning("Сканирование", $"Папка {path} не найдена: её удалили, переименовали или диск отключён. Выберите для сканирования другую папку.");
            return;
        }

        SelectPathForAutomation(path);
        await ScanAfterRescanPromptAsync(path, true);
    }

    [RelayCommand]
    private void DismissCancelled()
    {
        ScanWasCancelled = false;
    }
}
