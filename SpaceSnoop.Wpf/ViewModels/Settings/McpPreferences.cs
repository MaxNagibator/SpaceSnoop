using System.Security.Cryptography;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class McpPreferences : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    private bool _enabled = AppDefaults.McpEnabledDefault;

    [ObservableProperty]
    private int _port = AppDefaults.McpPortDefault;

    [ObservableProperty]
    private bool _allowMutations = AppDefaults.McpAllowMutationsDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TokenMissing))]
    private string _token = string.Empty;

    public McpPreferences(ISettingsStore settings)
    {
        _settings = settings;

        _suppressPersist = true;
        Enabled = _settings.GetBool(SettingsKeys.McpEnabled, AppDefaults.McpEnabledDefault);
        Port = Math.Clamp(_settings.GetInt(SettingsKeys.McpPort, AppDefaults.McpPortDefault), AppDefaults.McpPortMin, AppDefaults.McpPortMax);
        AllowMutations = _settings.GetBool(SettingsKeys.McpAllowMutations, AppDefaults.McpAllowMutationsDefault);
        Token = _settings.GetStringValue(SettingsKeys.McpToken)?.Trim() ?? string.Empty;
        _suppressPersist = false;

        if (Token.Length == 0)
        {
            RegenerateToken();
        }
    }

    public bool TokenMissing => Token.Length == 0;

    [RelayCommand]
    public void RegenerateToken()
    {
        Token = RandomNumberGenerator.GetHexString(32, true);
    }

    partial void OnEnabledChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.McpEnabled, value);
        }
    }

    partial void OnPortChanged(int value)
    {
        var clamped = Math.Clamp(value, AppDefaults.McpPortMin, AppDefaults.McpPortMax);

        if (clamped != value)
        {
            Port = clamped;
            return;
        }

        if (!_suppressPersist)
        {
            _settings.SetInt(SettingsKeys.McpPort, value);
        }
    }

    partial void OnAllowMutationsChanged(bool value)
    {
        if (!_suppressPersist)
        {
            _settings.SetBool(SettingsKeys.McpAllowMutations, value);
        }
    }

    partial void OnTokenChanged(string value)
    {
        if (!_suppressPersist)
        {
            _settings.SetValue(SettingsKeys.McpToken, value);
        }
    }
}
