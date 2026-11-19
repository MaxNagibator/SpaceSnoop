using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ScanPreferences _scan;
    private readonly OperationPreferences _operations;
    private readonly UpdatePreferences _updates;
    private readonly Func<Task<bool>> _restart;

    private bool _initialFastScan;
    private DeleteMode _initialDeleteMode;
    private string? _initialThemeKey;
    private bool _initialCheckUpdates;

    [ObservableProperty]
    private bool _isVisible;

    public bool IsElevated { get; private set; }

    public bool NeedsElevation => !IsElevated;

    public string FastScanHint => IsElevated
        ? "Целый диск читается по таблице файлов, а не папка за папкой: в несколько раз быстрее, и закрытые системные папки тоже попадают в итог. Отдельную папку программа обходит как обычно."
        : "Целый диск читается по таблице файлов, а не папка за папкой: в несколько раз быстрее, и закрытые системные папки тоже попадают в итог. Нужны права администратора – программа перезапустится, Windows спросит разрешение.";

    public bool FastScan
    {
        get => _scan.MftEnabled;
        set => _scan.MftEnabled = value;
    }

    public IReadOnlyList<EnumOption<DeleteMode>> DeleteModeOptions => SettingsOptions.DeleteModes;

    public EnumOption<DeleteMode> SelectedDeleteModeOption
    {
        get => DeleteModeOptions.FirstOrDefault(option => option.Value == _operations.DeleteMode) ?? DeleteModeOptions[0];
        set
        {
            if (value is null)
            {
                OnPropertyChanged();
                return;
            }

            if (value.Value != _operations.DeleteMode)
            {
                _operations.DeleteMode = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public ThemeSelectionViewModel Theme { get; }

    public bool CheckUpdates
    {
        get => _updates.CheckOnStartup;
        set => _updates.CheckOnStartup = value;
    }

    public FirstRunViewModel(
        ISettingsStore settings,
        ScanPreferences scan,
        OperationPreferences operations,
        ThemeSelectionViewModel theme,
        UpdatePreferences updates,
        bool isElevated,
        Func<Task<bool>> restart)
    {
        _settings = settings;
        _scan = scan;
        _operations = operations;
        Theme = theme;
        _updates = updates;
        _restart = restart;

        IsElevated = isElevated;
        IsVisible = settings.GetBool(SettingsKeys.WelcomePending);
        SnapshotInitialValues();

        scan.PropertyChanged += OnScanChanged;
        operations.PropertyChanged += OnOperationsChanged;
        updates.PropertyChanged += OnUpdatesChanged;
    }

    internal void ShowForAutomation(bool isElevated)
    {
        IsElevated = isElevated;
        SnapshotInitialValues();
        IsVisible = true;
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(NeedsElevation));
        OnPropertyChanged(nameof(FastScanHint));
    }

    [RelayCommand]
    private void Skip()
    {
        if (_scan.MftEnabled != _initialFastScan)
        {
            _scan.MftEnabled = _initialFastScan;
        }

        if (_operations.DeleteMode != _initialDeleteMode)
        {
            _operations.DeleteMode = _initialDeleteMode;
        }

        if (!string.Equals(Theme.CurrentKey, _initialThemeKey, StringComparison.Ordinal))
        {
            Theme.CurrentKey = _initialThemeKey;
        }

        if (_updates.CheckOnStartup != _initialCheckUpdates)
        {
            _updates.CheckOnStartup = _initialCheckUpdates;
        }

        Complete();
    }

    [RelayCommand]
    private void Dismiss()
    {
        Complete();
    }

    [RelayCommand]
    private async Task RestartAsAdminAsync()
    {
        var fastScan = _scan.MftEnabled;
        _scan.MftEnabled = true;
        _settings.Flush();

        if (await _restart())
        {
            return;
        }

        _scan.MftEnabled = fastScan;
    }

    private void SnapshotInitialValues()
    {
        _initialFastScan = _scan.MftEnabled;
        _initialDeleteMode = _operations.DeleteMode;
        _initialThemeKey = Theme.CurrentKey;
        _initialCheckUpdates = _updates.CheckOnStartup;
    }

    private void Complete()
    {
        IsVisible = false;
        _settings.SetBool(SettingsKeys.WelcomePending, false);
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanPreferences.MftEnabled))
        {
            OnPropertyChanged(nameof(FastScan));
        }
    }

    private void OnOperationsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OperationPreferences.DeleteMode))
        {
            OnPropertyChanged(nameof(SelectedDeleteModeOption));
        }
    }

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.CheckOnStartup))
        {
            OnPropertyChanged(nameof(CheckUpdates));
        }
    }
}
