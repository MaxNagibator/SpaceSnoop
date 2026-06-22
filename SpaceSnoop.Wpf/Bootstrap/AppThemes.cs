using KeepShell.Themes.Tarkov;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string TarkovKey = TarkovTheme.Key;

    public static string ToKey(AppTheme theme)
    {
        return theme switch
        {
            AppTheme.Dark => DarkKey,
            AppTheme.Tarkov => TarkovKey,
            _ => LightKey,
        };
    }

    public static AppTheme FromKey(string? key)
    {
        return key?.Trim().ToLowerInvariant() switch
        {
            DarkKey => AppTheme.Dark,
            TarkovKey => AppTheme.Tarkov,
            _ => AppTheme.Light,
        };
    }

    public static void Register()
    {
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        TarkovTheme.Register();
    }
}
