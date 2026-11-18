using KeepShell.Themes.Tarkov;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string TarkovKey = TarkovTheme.Key;
    public const string SystemKey = "system";
    public const string SublimeKey = ThemeManager.SublimeKey;

    public static string ToKey(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => DarkKey,
            AppTheme.Tarkov => TarkovKey,
            AppTheme.System => SystemKey,
            AppTheme.Sublime => SublimeKey,
            _ => LightKey,
        };
    }

    public static AppTheme FromKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            DarkKey => AppTheme.Dark,
            TarkovKey => AppTheme.Tarkov,
            SystemKey => AppTheme.System,
            SublimeKey => AppTheme.Sublime,
            _ => AppTheme.Light,
        };
    }

    public static void Register()
    {
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        TarkovTheme.Register();
        ThemeManager.Register(ThemeManager.Sublime);

        RegisterSystem(SystemTheme.AppsUseLightTheme());
    }

    public static void RegisterSystem(bool appsUseLightTheme)
    {
        var source = appsUseLightTheme ? ThemeManager.DefaultLight : ThemeManager.DefaultDark;
        ThemeManager.Register(
            source with
            {
                Key = SystemKey,
            });
    }

    public static string StartupKey(ISettingsStore settings, bool freshProfile)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (freshProfile)
        {
            settings.SetValue(SettingsKeys.Theme, ToKey(AppDefaults.ThemeDefault));
        }

        var stored = settings.GetStringValue(SettingsKeys.Theme);
        return string.IsNullOrWhiteSpace(stored) ? LightKey : stored;
    }

    public static void FollowSystem(bool appsUseLightTheme)
    {
        if (ThemeManager.Find(SystemKey)?.IsDark == !appsUseLightTheme)
        {
            return;
        }

        RegisterSystem(appsUseLightTheme);

        if (!string.Equals(ThemeManager.Current?.Key, SystemKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ThemeManager.Apply(SystemKey);
    }
}
