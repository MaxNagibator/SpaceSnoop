namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    private bool CanOpenPicker()
    {
        return HasResult && !IsBusy && !IsPickerOpen;
    }

    [RelayCommand(CanExecute = nameof(CanOpenPicker))]
    private void OpenPicker()
    {
        IsPickerOpen = true;
    }

    [RelayCommand(CanExecute = nameof(CanClosePicker))]
    private void ClosePicker()
    {
        IsPickerOpen = false;
    }

    private bool CanClosePicker()
    {
        return HasResult && IsPickerOpen;
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        Drives.ReloadLabels(SelectedDrive);
    }

    [RelayCommand]
    private void SelectTarget(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            SelectedDrive = path;
        }
    }

    [RelayCommand]
    private void ForgetTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Drives.RemoveDrive(path))
        {
            return;
        }

        if (SelectedDrive is not null && ScanTreeEditor.SamePath(SelectedDrive, path))
        {
            SelectedDrive = Drives.FallbackPath;
        }
    }
}
