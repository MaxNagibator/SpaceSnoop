using MahApps.Metro.IconPacks;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public static class SettingsOptions
{
    public static IReadOnlyList<EnumOption<StartupPage>> StartupPages { get; } =
    [
        new(StartupPage.LastUsed, "Последняя активная", PackIconLucideKind.Clock, "Последняя", "Открывать последнюю активную страницу перед закрытием"),
        new(StartupPage.Scan, "Сканирование", PackIconLucideKind.HardDrive, Tooltip: "Открывать страницу сканирования дисков"),
        new(StartupPage.Sync, "Синхронизация", PackIconLucideKind.FolderSync, Tooltip: "Открывать страницу сравнения и синхронизации"),
        new(StartupPage.Logs, "Логи", PackIconLucideKind.FileText, Tooltip: "Открывать журнал событий приложения"),
    ];

    public static IReadOnlyList<EnumOption<StartupAdminMode>> StartupAdminModes { get; } =
    [
        new(StartupAdminMode.Ask, "Спрашивать", PackIconLucideKind.ShieldAlert, Tooltip: "Спрашивать при запуске без прав администратора"),
        new(StartupAdminMode.WithoutAdmin, "Продолжать без прав", PackIconLucideKind.Shield, "Без прав", "Всегда запускать без прав администратора и не спрашивать"),
        new(StartupAdminMode.AlwaysAdmin, "Всегда с правами", PackIconLucideKind.ShieldCheck, Tooltip: "Автоматически перезапускать от имени администратора"),
    ];

    public static IReadOnlyList<EnumOption<DeleteMode>> DeleteModes { get; } =
    [
        new(DeleteMode.RecycleBin, "В корзину", PackIconLucideKind.Trash2, Tooltip: "Перемещать в корзину Windows – файлы можно восстановить"),
        new(DeleteMode.Permanent, "Безвозвратно", PackIconLucideKind.TriangleAlert, Tooltip: "Удалять сразу мимо корзины – быстрее, но отменить нельзя"),
    ];

    public static IReadOnlyList<EnumOption<CompressionLevel>> CompressionLevels { get; } =
    [
        new(CompressionLevel.NoCompression, "Без сжатия (только упаковка)", PackIconLucideKind.Package, "Без сжатия", "Только упаковка в .zip без сжатия – максимально быстро"),
        new(CompressionLevel.Fastest, "Быстрое", PackIconLucideKind.Zap, Tooltip: "Быстрое сжатие с умеренным уменьшением размера"),
        new(CompressionLevel.Optimal, "Оптимальное", PackIconLucideKind.Sparkles, Tooltip: "Сбалансированное соотношение скорости и размера архива"),
        new(CompressionLevel.SmallestSize, "Максимальное (медленно)", PackIconLucideKind.Shrink, "Максимальное", "Минимальный размер архива – упаковка идёт медленнее"),
    ];

    public static IReadOnlyList<EnumOption<GitFolderPromptChoice>> GitFolders { get; } =
    [
        new(GitFolderPromptChoice.Ask, "Спрашивать", PackIconLucideKind.MessageCircle, Tooltip: "Спрашивать перед синхронизацией при обнаружении папок .git"),
        new(GitFolderPromptChoice.Skip, "Всегда пропускать", PackIconLucideKind.Ban, "Пропускать", "Всегда исключать папки .git из синхронизации"),
        new(GitFolderPromptChoice.Keep, "Синхронизировать", PackIconLucideKind.FolderGit2, Tooltip: "Синхронизировать папки .git без предупреждения"),
    ];

    public static IReadOnlyList<SegmentOption> AgentBackends { get; } =
    [
        new(PackIconLucideKind.Bot, "Claude Code", "CLI claude – встроенные инструменты отключаются целиком, у агента только инструменты приложения"),
        new(PackIconLucideKind.SquareTerminal, "Codex", "CLI codex – встроенная оболочка отключена, у агента только инструменты приложения"),
        new(PackIconLucideKind.SquareCode, "OpenCode", "CLI opencode – работает по локально настроенной авторизации, встроенные инструменты отключены, у агента только инструменты приложения"),
        new(PackIconLucideKind.Bot, "Antigravity", "CLI agy – доступ к инструментам приложения задаёт SpaceSnoop; встроенные инструменты и другие MCP-серверы зависят от настроек CLI"),
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
