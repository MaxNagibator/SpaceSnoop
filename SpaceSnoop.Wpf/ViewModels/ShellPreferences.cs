namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    [ObservableProperty]
    private StartupPage _startupPage = AppDefaults.StartupPageDefault;

    [ObservableProperty]
    private bool _navCollapsed;

    [ObservableProperty]
    private BackdropKind _backdrop = AppDefaults.BackdropDefault;

    [ObservableProperty]
    private bool _warnIfNotAdministrator = AppDefaults.WarnIfNotAdminDefault;

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(SettingsKeys.ShowPageHeader, SettingsKeys.EnableToastNotifications, SettingsKeys.FontScale))
    {
        SuppressPersist = true;
        StartupPage = Settings.GetEnum(SettingsKeys.StartupPage, AppDefaults.StartupPageDefault);
        NavCollapsed = Settings.GetBool(SettingsKeys.NavCollapsed);
        Backdrop = Settings.GetEnum(SettingsKeys.Backdrop, AppDefaults.BackdropDefault);
        WarnIfNotAdministrator = Settings.GetBool(SettingsKeys.WarnIfNotAdmin, AppDefaults.WarnIfNotAdminDefault);
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

    partial void OnBackdropChanged(BackdropKind value)
    {
        if (!SuppressPersist)
        {
            Settings.SetEnum(SettingsKeys.Backdrop, value);
        }
    }

    partial void OnWarnIfNotAdministratorChanged(bool value)
    {
        PersistBool(SettingsKeys.WarnIfNotAdmin, value);
    }
}
