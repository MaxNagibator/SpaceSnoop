namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class AgentPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _enabled = AppDefaults.AgentEnabledDefault;

    [ObservableProperty]
    private bool _consent = AppDefaults.AgentConsentDefault;

    [ObservableProperty]
    private string _model = AppDefaults.AgentModelDefault;

    [ObservableProperty]
    private string _cliPath = string.Empty;

    public AgentPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        Enabled = _settings.GetBool(SettingsKeys.AgentEnabled, AppDefaults.AgentEnabledDefault);
        Consent = _settings.GetBool(SettingsKeys.AgentConsent, AppDefaults.AgentConsentDefault);
        Model = _settings.GetStringValue(SettingsKeys.AgentModel)?.Trim() ?? AppDefaults.AgentModelDefault;
        CliPath = _settings.GetStringValue(SettingsKeys.AgentCliPath)?.Trim() ?? string.Empty;
        _suppressPersist = false;
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.AgentEnabled, value);
        }
    }

    partial void OnConsentChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.AgentConsent, value);
        }
    }

    partial void OnModelChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.AgentModel, value);
        }
    }

    partial void OnCliPathChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.AgentCliPath, value);
        }
    }
}
