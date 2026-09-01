using MahApps.Metro.IconPacks;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public static class SettingsOptions
{
    public static IReadOnlyList<EnumOption<AppTheme>> Themes { get; } =
    [
        new(AppTheme.Light, "Светлая"),
        new(AppTheme.Dark, "Тёмная"),
        new(AppTheme.Tarkov, "Tarkov"),
    ];

    public static IReadOnlyList<EnumOption<StartupPage>> StartupPages { get; } =
    [
        new(StartupPage.LastUsed, "Последняя активная"),
        new(StartupPage.Scan, "Сканирование"),
        new(StartupPage.Sync, "Синхронизация"),
        new(StartupPage.Logs, "Логи"),
    ];

    public static IReadOnlyList<EnumOption<DeleteMode>> DeleteModes { get; } =
    [
        new(DeleteMode.RecycleBin, "В корзину"),
        new(DeleteMode.Permanent, "Безвозвратно"),
    ];

    public static IReadOnlyList<EnumOption<CompressionLevel>> CompressionLevels { get; } =
    [
        new(CompressionLevel.Optimal, "Оптимальное"),
        new(CompressionLevel.SmallestSize, "Максимальное (медленно)"),
        new(CompressionLevel.Fastest, "Быстрое"),
        new(CompressionLevel.NoCompression, "Без сжатия (только упаковка)"),
    ];

    public static IReadOnlyList<EnumOption<GitFolderPromptChoice>> GitFolders { get; } =
    [
        new(GitFolderPromptChoice.Ask, "Спрашивать"),
        new(GitFolderPromptChoice.Skip, "Всегда пропускать"),
        new(GitFolderPromptChoice.Keep, "Синхронизировать"),
    ];

    public static IReadOnlyList<SegmentOption> AgentBackends { get; } =
    [
        new(PackIconLucideKind.Bot, "Claude Code", "CLI claude – встроенные инструменты отключаются целиком, у агента только инструменты приложения"),
        new(PackIconLucideKind.SquareTerminal, "Codex", "CLI codex – помимо инструментов приложения агент получает оболочку системы, отключить её нечем"),
        new(PackIconLucideKind.SquareCode, "OpenCode", "CLI opencode – работает по локально настроенной авторизации, встроенные инструменты отключены, у агента только инструменты приложения"),
    ];

    public static string LabelOf<T>(IReadOnlyList<EnumOption<T>> options, T value)
        where T : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.First(option => option.Value.Equals(value)).Label;
    }

    public static string LabelOf(AgentBackendKind backend)
    {
        return AgentBackends[AgentBackendChoice.Order.IndexOf(backend)].Text;
    }
}
