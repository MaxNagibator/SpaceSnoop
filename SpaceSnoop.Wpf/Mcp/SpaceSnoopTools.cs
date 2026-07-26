using ModelContextProtocol.Server;
using SpaceSnoop.Core.Export;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.Mcp;

[McpServerToolType]
public sealed class SpaceSnoopTools
{
    [McpServerTool(Name = "get_app_state")]
    [Description("Текущее состояние запущенного SpaceSnoop: версия, права, открытая страница, параметры страницы «Синхронизация» и счётчики последнего сравнения.")]
    public static string GetAppState(McpBridge bridge)
    {
        return bridge.GetState();
    }

    [McpServerTool(Name = "list_profiles")]
    [Description("Профили синхронизации из расписания: пути, направление, победитель, зеркало, исключения, периодичность и признак недоступности.")]
    public static string ListProfiles(McpBridge bridge)
    {
        return bridge.ListProfiles();
    }

    [McpServerTool(Name = "scan_directory")]
    [Description("Сканирует каталог или диск и возвращает распределение занятого места: крупнейшие подкаталоги до заданной глубины и крупнейшие файлы всего дерева. Только чтение – приложение не трогает, ничего не удаляет.")]
    public static Task<string> ScanDirectoryAsync(
        McpBridge bridge,
        [Description("Путь к каталогу или диску, например «C:\\» или «C:\\Users\\Иван»")] string path,
        [Description("До какой глубины вложенности перечислять подкаталоги")] int depth = ScanExport.DefaultDepth,
        [Description("Сколько записей выгружать в каждом списке (крупнейшие по размеру)")] int entryLimit = ScanExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.ScanAsync(path, depth, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "compare_directories")]
    [Description("Сравнивает два каталога и возвращает машиночитаемый разбор различий (сводка, распределение по каталогам, список файлов с причиной различия). Приложение не трогает, ничего не копирует и не удаляет.")]
    public static Task<string> CompareDirectoriesAsync(
        McpBridge bridge,
        [Description("Путь к левому каталогу")] string left,
        [Description("Путь к правому каталогу")] string right,
        [Description("Направление: LeftToRight, RightToLeft или Bidirectional")] SyncMode mode = SyncMode.LeftToRight,
        [Description("Победитель в двустороннем режиме: Newest, Left или Right")] SyncWinner winner = SyncWinner.Newest,
        [Description("Зеркало: удалять на приёмнике то, чего нет у источника")] bool mirror = false,
        [Description("Исключения через запятую, например «bin,obj,*.tmp»")] string? exclusions = null,
        [Description("Сколько различий выгружать (крупнейшие по размеру)")] int entryLimit = ComparisonExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.CompareAsync(left, right, mode, winner, mirror, exclusions, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "get_current_comparison")]
    [Description("Выгружает сравнение, которое сейчас открыто на странице «Синхронизация», включая состояние Git обеих сторон и итог последней синхронизации.")]
    public static Task<string> GetCurrentComparisonAsync(
        McpBridge bridge,
        [Description("Сколько различий выгружать (крупнейшие по размеру)")] int entryLimit = ComparisonExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.GetCurrentComparisonAsync(entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "sync_current")]
    [Description("Применяет сравнение, открытое на странице «Синхронизация»: копирует файлы и удаляет лишние в корзину. При dryRun=true ничего не выполняется – возвращается только план. Реальное выполнение требует включённой настройки «Разрешить изменяющие операции».")]
    public static Task<string> SyncCurrentAsync(
        McpBridge bridge,
        [Description("true – только показать план, ничего не менять")] bool dryRun = true,
        [Description("Сколько записей включать в план")] int entryLimit = ComparisonExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.SyncCurrentAsync(dryRun, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "open_sync")]
    [Description("Управляет окном приложения: открывает страницу «Синхронизация», подставляет каталоги и параметры и по запросу запускает сравнение. Ничего не копирует и не удаляет.")]
    public static Task<string> OpenSyncAsync(
        McpBridge bridge,
        [Description("Путь к левому каталогу; не задан – остаётся текущий")] string? left = null,
        [Description("Путь к правому каталогу; не задан – остаётся текущий")] string? right = null,
        [Description("Направление: LeftToRight, RightToLeft или Bidirectional")] SyncMode? mode = null,
        [Description("Победитель в двустороннем режиме: Newest, Left или Right")] SyncWinner? winner = null,
        [Description("Зеркало: удалять на приёмнике то, чего нет у источника")] bool? mirror = null,
        [Description("Исключения через запятую")] string? exclusions = null,
        [Description("Запустить сравнение сразу после открытия страницы")] bool compare = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.OpenSyncAsync(left, right, mode, winner, mirror, exclusions, compare, cancellationToken);
    }
}
