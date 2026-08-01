namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    [ObservableProperty]
    private StartupPage _startupPage = AppDefaults.StartupPageDefault;

    [ObservableProperty]
    private bool _navCollapsed;

    [ObservableProperty]
    private bool _warnIfNotAdministrator = AppDefaults.WarnIfNotAdminDefault;

    [ObservableProperty]
    private bool _showPerformanceHud = AppDefaults.PerformanceHudDefault;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(SettingsKeys.ShowPageHeader, SettingsKeys.EnableToastNotifications, SettingsKeys.FontScale))
    {
        SuppressPersist = true;
        StartupPage = Settings.GetEnum(SettingsKeys.StartupPage, AppDefaults.StartupPageDefault);
        NavCollapsed = Settings.GetBool(SettingsKeys.NavCollapsed);
        WarnIfNotAdministrator = Settings.GetBool(SettingsKeys.WarnIfNotAdmin, AppDefaults.WarnIfNotAdminDefault);
        ShowPerformanceHud = Settings.GetBool(SettingsKeys.PerformanceHud, AppDefaults.PerformanceHudDefault);
        SuppressPersist = false;
    }

    public string? LastPage
    {
        get => Settings.GetStringValue(SettingsKeys.LastPage);
        set => Settings.SetValue(SettingsKeys.LastPage, value ?? string.Empty);
    }

    partial void OnStartupPageChanged(StartupPage value)
    {
        if (!SuppressPersist)
        {
            Settings.SetEnum(SettingsKeys.StartupPage, value);
        }
    }

    partial void OnNavCollapsedChanged(bool value)
    {
        PersistBool(SettingsKeys.NavCollapsed, value);
    }

    partial void OnWarnIfNotAdministratorChanged(bool value)
    {
        PersistBool(SettingsKeys.WarnIfNotAdmin, value);
    }

    partial void OnShowPerformanceHudChanged(bool value)
    {
        PersistBool(SettingsKeys.PerformanceHud, value);
    }
}
