using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ScanPreferences _scan;
    private readonly UpdatePreferences _updates;
    private readonly Func<Task<bool>> _restart;

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

    public ThemeSelectionViewModel Theme { get; }

    public bool CheckUpdates
    {
        get => _updates.CheckOnStartup;
        set => _updates.CheckOnStartup = value;
    }

    public FirstRunViewModel(
        ISettingsStore settings,
        ScanPreferences scan,
        ThemeSelectionViewModel theme,
        UpdatePreferences updates,
        bool isElevated,
        Func<Task<bool>> restart)
    {
        _settings = settings;
        _scan = scan;
        Theme = theme;
        _updates = updates;
        _restart = restart;

        IsElevated = isElevated;
        IsVisible = settings.GetBool(SettingsKeys.WelcomePending);

        scan.PropertyChanged += OnScanChanged;
        updates.PropertyChanged += OnUpdatesChanged;
    }

    internal void ShowForAutomation(bool isElevated)
    {
        IsElevated = isElevated;
        IsVisible = true;
        OnPropertyChanged(nameof(IsElevated));
        OnPropertyChanged(nameof(NeedsElevation));
        OnPropertyChanged(nameof(FastScanHint));
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

    private void OnUpdatesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.CheckOnStartup))
        {
            OnPropertyChanged(nameof(CheckUpdates));
        }
    }
}
