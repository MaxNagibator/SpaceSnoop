namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class UpdatePreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private string _repository = AppDefaults.UpdateRepositoryDefault;

    [ObservableProperty]
    private bool _checkOnStartup = AppDefaults.UpdateCheckOnStartupDefault;

    [ObservableProperty]
    private bool _autoDownload = AppDefaults.UpdateAutoDownloadDefault;

    public UpdatePreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        var stored = _settings.GetStringValue(SettingsKeys.UpdateRepository);
        Repository = string.IsNullOrWhiteSpace(stored) ? AppDefaults.UpdateRepositoryDefault : stored.Trim();
        CheckOnStartup = _settings.GetBool(SettingsKeys.UpdateCheckOnStartup, AppDefaults.UpdateCheckOnStartupDefault);
        AutoDownload = _settings.GetBool(SettingsKeys.UpdateAutoDownload);
        _suppressPersist = false;
    }

    partial void OnRepositoryChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.UpdateRepository, value?.Trim() ?? string.Empty);
        }
    }

    partial void OnCheckOnStartupChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.UpdateCheckOnStartup, value);
        }
    }

    partial void OnAutoDownloadChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.UpdateAutoDownload, value);
        }
    }
}
