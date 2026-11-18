using System.Reflection;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string SystemKey = "system";
    public const string SystemDisplayName = "Как в системе";

    public static void Register(Assembly? themeModules = null)
    {
        RegisterSystem(SystemTheme.AppsUseLightTheme());
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        if (themeModules is not null)
        {
            ThemeManager.RegisterModules(themeModules);
        }
    }

    public static void RegisterSystem(bool appsUseLightTheme)
    {
        var source = appsUseLightTheme ? ThemeManager.DefaultLight : ThemeManager.DefaultDark;
        ThemeManager.Register(
            source with
            {
                Key = SystemKey,
                DisplayName = SystemDisplayName,
                PreviewPalette = ThemeManager.DefaultLight.Palette,
                AlternatePreviewPalette = ThemeManager.DefaultDark.Palette,
            });
    }

    public static string StartupKey(ISettingsStore settings, bool freshProfile)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (freshProfile)
        {
            settings.SetValue(SettingsKeys.Theme, AppDefaults.ThemeDefault);
        }

        var stored = settings.GetStringValue(SettingsKeys.Theme);
        return string.IsNullOrWhiteSpace(stored)
            ? LightKey
            : ThemeManager.Find(stored.Trim())?.Key ?? AppDefaults.ThemeDefault;
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
