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
    private bool _historyVisible = AppDefaults.AgentHistoryVisibleDefault;

    [ObservableProperty]
    private bool _transcript = AppDefaults.AgentTranscriptDefault;

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

        try
        {
            MigrateSharedKeys();
            Enabled = _settings.GetBool(SettingsKeys.AgentEnabled, AppDefaults.AgentEnabledDefault);
            HistoryVisible = _settings.GetBool(SettingsKeys.AgentHistoryVisible, AppDefaults.AgentHistoryVisibleDefault);
            Transcript = _settings.GetBool(SettingsKeys.AgentTranscript, AppDefaults.AgentTranscriptDefault);
            Backend = _settings.GetEnum(SettingsKeys.AgentBackend, AppDefaults.AgentBackendDefault);
            Consent = ConsentFor(Backend);
            Model = ModelFor(Backend);
            Effort = EffortFor(Backend);
            CliPath = CliPathFor(Backend);
        }
        finally
        {
            _suppressPersist = false;
        }
    }

    public bool ConsentFor(AgentBackendKind kind)
    {
        return _settings.GetBool(SettingsKeys.AgentConsent(kind), AppDefaults.AgentConsentDefault);
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
        MigrateConsent();
    }

    private void MigrateConsent()
    {
        var sharedKey = SettingsKeys.AgentConsentShared;

        if (string.IsNullOrWhiteSpace(_settings.GetStringValue(sharedKey)))
        {
            return;
        }

        var backendKey = SettingsKeys.AgentConsent(AgentBackendKind.Claude);

        _settings.SetBool(backendKey, _settings.GetBool(sharedKey) && _settings.GetBool(backendKey, true));
        _settings.SetValue(sharedKey, string.Empty);
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
            _settings.SetBool(SettingsKeys.AgentConsent(Backend), value);
        }
    }

    partial void OnHistoryVisibleChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.AgentHistoryVisible, value);
        }
    }

    partial void OnTranscriptChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.AgentTranscript, value);
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

        try
        {
            Consent = ConsentFor(value);
            Model = ModelFor(value);
            Effort = EffortFor(value);
            CliPath = CliPathFor(value);
        }
        finally
        {
            _suppressPersist = false;
        }
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
