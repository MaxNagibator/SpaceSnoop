using MahApps.Metro.IconPacks;
using Serilog;
using SpaceSnoop.Wpf.Converters;
using System.IO;
using System.Reflection;

namespace SpaceSnoop.Wpf.Bootstrap;

public static class AppThemes
{
    public const string LightKey = "light";
    public const string DarkKey = "dark";
    public const string SystemKey = "system";
    public const string SystemDisplayName = "Как в системе";

    private static readonly Lazy<ThemePackLoader> PackLoader = new(() =>
        new ThemePackLoader(Path.Combine(AppStorage.DataDirectory, AppStorage.ThemesFolderName), LogThemeLoadFailure));

    internal static ThemePackLoader Packages => PackLoader.Value;

    internal static ThemePackCatalog Catalog { get; } = new(
        new Uri("https://github.com/TheVSAKeeper/KeepShell.Themes/releases/latest/download/themes.json"));

    public static void Register(Assembly? themeModules = null)
    {
        HeatColor.AttachThemeSync();
        RegisterSystem(SystemTheme.AppsUseLightTheme());
        ThemeManager.Register(ThemeManager.DefaultLight);
        ThemeManager.Register(ThemeManager.DefaultDark);

        if (themeModules is not null)
        {
            ThemeManager.RegisterModules(themeModules);
        }

        HeatColor.SyncFromActiveTheme();
    }

    public static void RegisterSystem(bool appsUseLightTheme)
    {
        var source = appsUseLightTheme ? ThemeManager.DefaultLight : ThemeManager.DefaultDark;
        ThemeManager.Register(
            source with
            {
                Key = SystemKey,
                DisplayName = SystemDisplayName,
                Icon = PackIconLucideKind.Monitor,
                PreviewPalette = ThemeManager.DefaultLight.Palette,
                AlternatePreviewPalette = ThemeManager.DefaultDark.Palette,
            });
    }

    private static void LogThemeLoadFailure(string path, Exception error) =>
        Log.Warning(error, "Пакет темы пропущен: {ThemePackPath}", path);

    public static string StartupKey(ISettingsStore settings, bool freshProfile) =>
        StartupKey(settings, freshProfile, Packages);

    internal static string StartupKey(ISettingsStore settings, bool freshProfile, ThemePackLoader loader)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(loader);

        if (freshProfile)
        {
            settings.SetValue(SettingsKeys.Theme, AppDefaults.ThemeDefault);
        }

        var stored = settings.GetStringValue(SettingsKeys.Theme)?.Trim();
        if (string.IsNullOrEmpty(stored))
        {
            return LightKey;
        }

        return loader.Restore(settings) ?? AppDefaults.ThemeDefault;
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
