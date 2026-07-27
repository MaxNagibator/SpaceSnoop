namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class AgentPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private bool _suppressPersist;

    [ObservableProperty]
    private bool _enabled = AppDefaults.AgentEnabledDefault;

    [ObservableProperty]
    private bool _consent = AppDefaults.AgentConsentDefault;

    [ObservableProperty]
    private AgentBackendKind _backend = AppDefaults.AgentBackendDefault;

    [ObservableProperty]
    private string _model = AppDefaults.AgentModelDefault;

    [ObservableProperty]
    private string _effort = AppDefaults.AgentEffortDefault;

    [ObservableProperty]
    private string _cliPath = string.Empty;

    public AgentPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        MigrateSharedKeys();
        Enabled = _settings.GetBool(SettingsKeys.AgentEnabled, AppDefaults.AgentEnabledDefault);
        Consent = _settings.GetBool(SettingsKeys.AgentConsent, AppDefaults.AgentConsentDefault);
        Backend = _settings.GetEnum(SettingsKeys.AgentBackend, AppDefaults.AgentBackendDefault);
        Model = ModelFor(Backend);
        Effort = EffortFor(Backend);
        CliPath = CliPathFor(Backend);
        _suppressPersist = false;
    }

    public string ModelFor(AgentBackendKind kind)
    {
        return _settings.GetStringValue(SettingsKeys.AgentModel(kind))?.Trim() ?? AppDefaults.AgentModelDefault;
    }

    public string EffortFor(AgentBackendKind kind)
    {
        return _settings.GetStringValue(SettingsKeys.AgentEffort(kind))?.Trim() ?? AppDefaults.AgentEffortDefault;
    }

    public string CliPathFor(AgentBackendKind kind)
    {
        return _settings.GetStringValue(SettingsKeys.AgentCliPath(kind))?.Trim() ?? string.Empty;
    }

    private void MigrateSharedKeys()
    {
        Migrate(SettingsKeys.AgentModelShared, SettingsKeys.AgentModel(AgentBackendKind.Claude));
        Migrate(SettingsKeys.AgentCliPathShared, SettingsKeys.AgentCliPath(AgentBackendKind.Claude));
    }

    private void Migrate(string sharedKey, string backendKey)
    {
        var shared = _settings.GetStringValue(sharedKey);

        if (string.IsNullOrWhiteSpace(shared))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.GetStringValue(backendKey)))
        {
            _settings.SetValue(backendKey, shared);
        }

        _settings.SetValue(sharedKey, string.Empty);
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

    partial void OnBackendChanged(AgentBackendKind value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetEnum(SettingsKeys.AgentBackend, value);

        _suppressPersist = true;
        Model = ModelFor(value);
        Effort = EffortFor(value);
        CliPath = CliPathFor(value);
        _suppressPersist = false;
    }

    partial void OnModelChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.AgentModel(Backend), value);
        }
    }

    partial void OnEffortChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.AgentEffort(Backend), value);
        }
    }

    partial void OnCliPathChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.AgentCliPath(Backend), value);
        }
    }
}
