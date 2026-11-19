namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ShellPreferences : ShellPreferencesBase
{
    [ObservableProperty]
    private StartupPage _startupPage = AppDefaults.StartupPageDefault;

    [ObservableProperty]
    private bool _navCollapsed;

    [ObservableProperty]
    private StartupAdminMode _startupAdmin = AppDefaults.StartupAdminDefault;

    [ObservableProperty]
    private bool _showPerformanceHud = AppDefaults.PerformanceHudDefault;

    public string? LastPage
    {
        get => Settings.GetStringValue(SettingsKeys.LastPage);
        set => Settings.SetValue(SettingsKeys.LastPage, value ?? string.Empty);
    }

    public ShellPreferences(ISettingsStore settings)
        : base(settings, new(SettingsKeys.ShowPageHeader, SettingsKeys.EnableToastNotifications, SettingsKeys.FontScale))
    {
        SuppressPersist = true;
        StartupPage = Settings.GetEnum(SettingsKeys.StartupPage, AppDefaults.StartupPageDefault);
        NavCollapsed = Settings.GetBool(SettingsKeys.NavCollapsed);
        StartupAdmin = MigrateStartupAdmin(Settings);
        ShowPerformanceHud = Settings.GetBool(SettingsKeys.PerformanceHud);
        SuppressPersist = false;
    }

    public static StartupAdminMode MigrateStartupAdmin(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var legacy = settings.GetStringValue(SettingsKeys.WarnIfNotAdmin);

        if (!string.IsNullOrWhiteSpace(legacy))
        {
            var migrated = settings.GetBool(SettingsKeys.WarnIfNotAdmin, true)
                ? StartupAdminMode.Ask
                : StartupAdminMode.WithoutAdmin;

            if (string.IsNullOrWhiteSpace(settings.GetStringValue(SettingsKeys.StartupAdmin)))
            {
                settings.SetEnum(SettingsKeys.StartupAdmin, migrated);
            }

            settings.SetValue(SettingsKeys.WarnIfNotAdmin, string.Empty);
        }

        var mode = settings.GetEnum(SettingsKeys.StartupAdmin, AppDefaults.StartupAdminDefault);
        return mode == StartupAdminMode.None ? AppDefaults.StartupAdminDefault : mode;
    }

    internal void Flush()
    {
        Settings.Flush();
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

    partial void OnStartupAdminChanged(StartupAdminMode value)
    {
        if (!SuppressPersist)
        {
            Settings.SetEnum(SettingsKeys.StartupAdmin, value);
        }
    }

    partial void OnShowPerformanceHudChanged(bool value)
    {
        PersistBool(SettingsKeys.PerformanceHud, value);
    }
}
