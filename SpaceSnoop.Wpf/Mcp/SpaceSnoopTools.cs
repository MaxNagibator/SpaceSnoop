using ModelContextProtocol.Server;
using SpaceSnoop.Core.Export;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.Mcp;

[McpServerToolType]
public sealed class SpaceSnoopTools
{
    private SpaceSnoopTools()
    {
    }

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

    [McpServerTool(Name = "get_performance")]
    [Description("Как приложению живётся прямо сейчас: отклик интерфейса, занятая память, число сборок мусора, стоимость отрисовки карты диска, скорость текущей операции и время последнего запуска приложения. Скаляры описывают короткое окно последних 20 замеров (около 10 секунд), история – отдельный ряд за последние минуты, по которому видно, когда именно была просадка. Поля render* стоят особняком: их окно – последние 60 кадров карты диска, а не время, и меряют они построение кадра на UI-потоке, без растеризации на видеокарте; renderCount = 0 значит, что карту с начала сбора ни разу не рисовали. Сверяйте snapshotAgeMs: если оно велико, интерфейс заморожен и замеры не обновлялись, каким бы спокойным ни выглядел отклик. Отвечает мгновенно, ничего не считает и не меняет – с этого стоит начинать разговор «почему тормозит».")]
    public static string GetPerformance(
        McpBridge bridge,
        [Description("За сколько секунд вернуть историю замеров: 0 – без истории, потолок 300 секунд (столько хранится)")] int historySeconds = 0,
        [Description("Сколько точек истории вернуть максимум, потолок 240. Если замеров за окно больше, они сворачиваются в бакеты, и в каждом остаётся самый худший по задержке – окно возвращается целиком, просадка не теряется. Свёрнутые считаются в folded")] int historyPoints = AppDefaults.PerformanceHistoryPointsDefault)
    {
        return bridge.GetPerformance(historySeconds, historyPoints);
    }

    [McpServerTool(Name = "list_drives")]
    [Description("Диски машины: буква, метка, файловая система, сколько всего, занято и свободно. Отвечает мгновенно – с этого дешевле начинать разговор о нехватке места, чем со сканирования.")]
    public static string ListDrives(McpBridge bridge)
    {
        return bridge.ListDrives();
    }

    [McpServerTool(Name = "get_current_scan")]
    [Description("Выгружает результат сканирования, который сейчас открыт на странице «Сканирование»: крупнейшие подкаталоги и файлы. Ничего не пересчитывает – это снимок того, что видит человек в окне.")]
    public static Task<string> GetCurrentScanAsync(
        McpBridge bridge,
        [Description("До какой глубины вложенности перечислять подкаталоги")] int depth = ScanExport.DefaultDepth,
        [Description("Сколько записей выгружать в каждом списке (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.GetCurrentScanAsync(depth, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "open_scan")]
    [Description("Управляет окном приложения: открывает страницу «Сканирование», подставляет путь и по запросу запускает сканирование. Ничего не удаляет.")]
    public static Task<string> OpenScanAsync(
        McpBridge bridge,
        [Description("Путь к каталогу или диску; не задан – остаётся текущий")] string? path = null,
        [Description("Запустить сканирование сразу после открытия страницы")] bool scan = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.OpenScanAsync(path, scan, cancellationToken);
    }

    [McpServerTool(Name = "capture_view")]
    [Description("Снимает окно приложения в PNG и возвращает путь к файлу – чтобы увидеть страницу глазами человека, а не по данным. Ничего не меняет, кроме открытой страницы, если её попросили. Выпадающие списки, контекстные меню и подсказки живут в отдельных окнах и в кадр не попадают.")]
    public static string CaptureView(
        McpBridge bridge,
        [Description("Ключ страницы: scan, sync, overview, schedule, docker, chat, logs, about; не задан – снимается открытая")] string? section = null,
        [Description("Имя элемента (x:Name) внутри окна; не задано – снимается всё окно")] string? element = null,
        [Description("Масштаб кадра: 1 – логический размер окна, 2 – вдвое подробнее и вчетверо тяжелее")] double scale = AppDefaults.ViewCaptureScaleDefault)
    {
        return bridge.CaptureView(section, element, scale);
    }

    [McpServerTool(Name = "docker_usage")]
    [Description("Сколько места занял Docker: образы, контейнеры, тома и кэш сборки, с долей, которую можно вернуть. Только чтение – очистка Docker агенту недоступна, она удаляет мимо корзины.")]
    public static Task<string> GetDockerUsageAsync(
        McpBridge bridge,
        [Description("Перечислить сами объекты (образы, контейнеры, тома), а не только итоги по типам")] bool includeObjects = false,
        [Description("Сколько объектов выгружать (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.GetDockerUsageAsync(includeObjects, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "scan_directory")]
    [Description("Сканирует каталог или диск и возвращает распределение занятого места: крупнейшие подкаталоги до заданной глубины и крупнейшие файлы всего дерева. Только чтение – ничего не удаляет. При show=true результат попадает в дерево страницы «Сканирование», и после этого по нему работают mark_for_deletion и archive_directory; без show это отдельный расчёт, окно о нём не знает.")]
    public static Task<string> ScanDirectoryAsync(
        McpBridge bridge,
        [Description("Путь к каталогу или диску, например «C:\\» или «C:\\Users\\Иван»")] string path,
        [Description("До какой глубины вложенности перечислять подкаталоги")] int depth = ScanExport.DefaultDepth,
        [Description("Сколько записей выгружать в каждом списке (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        [Description("Показать результат в окне: дерево заменит открытое на странице «Сканирование»")] bool show = false,
        CancellationToken cancellationToken = default)
    {
        return bridge.ScanAsync(path, depth, entryLimit, show, cancellationToken);
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
        [Description("Сколько различий выгружать (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.CompareAsync(left, right, mode, winner, mirror, exclusions, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "get_current_comparison")]
    [Description("Выгружает сравнение, которое сейчас открыто на странице «Синхронизация», включая состояние Git обеих сторон и итог последней синхронизации.")]
    public static Task<string> GetCurrentComparisonAsync(
        McpBridge bridge,
        [Description("Сколько различий выгружать (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.GetCurrentComparisonAsync(entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "sync_current")]
    [Description("Применяет сравнение, открытое на странице «Синхронизация»: копирует файлы и удаляет лишние в корзину. При dryRun=true ничего не выполняется – возвращается компактный план: счётчики действий, объём копируемого и удаляемого, крупнейшие пути. Полное сравнение отдаёт get_current_comparison. Реальное выполнение требует включённой настройки «Разрешить изменяющие операции».")]
    public static Task<string> SyncCurrentAsync(
        McpBridge bridge,
        [Description("true – только показать план, ничего не менять")] bool dryRun = true,
        [Description("Сколько крупнейших путей включать в план")] int entryLimit = SyncPlanExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.SyncCurrentAsync(dryRun, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "archive_directory")]
    [Description("Упаковывает каталог из открытого сканирования в архив .zip рядом с ним и по запросу отправляет оригинал в корзину (только после проверки архива). При dryRun=true ничего не делается – возвращается план: сколько файлов и какого объёма попадёт в архив. Реальное выполнение требует включённой настройки «Разрешить изменяющие операции».")]
    public static Task<string> ArchiveDirectoryAsync(
        McpBridge bridge,
        [Description("Путь к каталогу; он должен быть внутри дерева, открытого на странице «Сканирование»")] string path,
        [Description("Отправить оригинал в корзину после успешной проверки архива")] bool deleteOriginal = false,
        [Description("true – только показать план, ничего не упаковывать")] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.ArchiveDirectoryAsync(path, deleteOriginal, dryRun, cancellationToken);
    }

    [McpServerTool(Name = "mark_for_deletion")]
    [Description("Помечает каталоги и файлы открытого сканирования на удаление – ровно как «Ctrl + правый клик» в окне. Ничего не удаляет: пометки видны человеку в дереве, а удаление в корзину запускает он сам кнопкой «Удалить помеченное». Требует включённой настройки «Разрешить изменяющие операции».")]
    public static string MarkForDeletion(
        McpBridge bridge,
        [Description("Пути к каталогам и файлам внутри дерева, открытого на странице «Сканирование»")] string[] paths,
        [Description("true – пометить, false – снять пометку вместе со вложенными")] bool mark = true)
    {
        return bridge.MarkForDeletion(paths, mark);
    }

    [McpServerTool(Name = "open_sync")]
    [Description("Управляет окном приложения: открывает страницу «Синхронизация», подставляет каталоги и параметры и по запросу запускает сравнение. Ничего не копирует и не удаляет. Победитель и зеркало заряжают удаления на приёмнике, поэтому применяются только при включённой настройке «Разрешить изменяющие операции» – без неё они игнорируются, и ответ это сообщает.")]
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
