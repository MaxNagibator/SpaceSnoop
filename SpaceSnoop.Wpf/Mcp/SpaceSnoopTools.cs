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
        return bridge.Insight.GetState();
    }

    [McpServerTool(Name = "list_profiles")]
    [Description("Профили синхронизации из расписания: пути, направление, победитель, зеркало, исключения, периодичность и признак недоступности.")]
    public static string ListProfiles(McpBridge bridge)
    {
        return bridge.Insight.ListProfiles();
    }

    [McpServerTool(Name = "get_performance")]
    [Description("Как приложению живётся прямо сейчас: отклик интерфейса, занятая память, число сборок мусора, плавность кадров окна, скорость текущей операции и время последнего запуска приложения. Скаляры описывают короткое окно последних 20 замеров (около 10 секунд), история – отдельный ряд за последние минуты, по которому видно, когда именно была просадка. Поля frame* стоят особняком: это промежутки между кадрами всего окна, пик и среднее по ним – за весь сбор, а не за окно замеров; при vsync 60 Гц спокойное среднее около 16,7 мс, а slowFrameCount считает кадры дольше 50 мс. Кадры меряются не всегда: зонд подписан, пока открыта страница «Производительность» или пока идёт операция, поэтому frameCount = 0 значит «не меряли», а не «всё гладко». workingSetPeakBytes – пик рабочего набора за сеанс сбора, а не за окно: он один отвечает на «сколько оно жрёт на пике» и обнуляется вместе с окном. Сверяйте snapshotAgeMs: если оно велико, интерфейс заморожен и замеры не обновлялись, каким бы спокойным ни выглядел отклик. Отвечает мгновенно, ничего не считает и не меняет – с этого стоит начинать разговор «почему тормозит».")]
    public static string GetPerformance(
        McpBridge bridge,
        [Description("За сколько секунд вернуть историю замеров: 0 – без истории, потолок 300 секунд (столько хранится)")] int historySeconds = 0,
        [Description("Сколько точек истории вернуть максимум, потолок 240. Если замеров за окно больше, они сворачиваются в бакеты, и в каждом остаётся самый худший по задержке – окно возвращается целиком, просадка не теряется. Свёрнутые считаются в folded")] int historyPoints = AppDefaults.PerformanceHistoryPointsDefault)
    {
        return bridge.Insight.GetPerformance(historySeconds, historyPoints);
    }

    [McpServerTool(Name = "list_drives")]
    [Description("Диски машины: буква, метка, файловая система, сколько всего, занято и свободно. Отвечает мгновенно – с этого дешевле начинать разговор о нехватке места, чем со сканирования.")]
    public static string ListDrives(McpBridge bridge)
    {
        return bridge.Insight.ListDrives();
    }

    [McpServerTool(Name = "get_current_scan")]
    [Description("Выгружает результат сканирования, который сейчас открыт на странице «Сканирование»: крупнейшие подкаталоги и файлы. Ничего не пересчитывает – это снимок того, что видит человек в окне.")]
    public static Task<string> GetCurrentScanAsync(
        McpBridge bridge,
        [Description("До какой глубины вложенности перечислять подкаталоги")] int depth = ScanExport.DefaultDepth,
        [Description("Сколько записей выгружать в каждом списке (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.Scan.GetCurrentScanAsync(depth, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "open_scan")]
    [Description("Управляет окном приложения: открывает страницу «Сканирование», подставляет путь и по запросу запускает сканирование. Ничего не удаляет.")]
    public static Task<string> OpenScanAsync(
        McpBridge bridge,
        [Description("Путь к каталогу или диску; не задан – остаётся текущий")] string? path = null,
        [Description("Запустить сканирование сразу после открытия страницы")] bool scan = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.Scan.OpenScanAsync(path, scan, cancellationToken);
    }

    [McpServerTool(Name = "capture_view")]
    [Description("Снимает окно приложения в PNG и возвращает путь к файлу – чтобы увидеть страницу глазами человека, а не по данным. Ничего не меняет, кроме открытой страницы, если её попросили. Выпадающие списки, контекстные меню и подсказки живут в отдельных окнах и в кадр не попадают.")]
    public static string CaptureView(
        McpBridge bridge,
        [Description("Ключ страницы: scan, sync, overview, schedule, docker, chat, logs, about; не задан – снимается открытая")] string? section = null,
        [Description("Имя элемента (x:Name) внутри окна; не задано – снимается всё окно")] string? element = null,
        [Description("Масштаб кадра: 1 – логический размер окна, 2 – вдвое подробнее и вчетверо тяжелее")] double scale = AppDefaults.ViewCaptureScaleDefault)
    {
        return bridge.Capture.CaptureView(section, element, scale);
    }

    [McpServerTool(Name = "docker_usage")]
    [Description("Сколько места занял Docker: образы, контейнеры, тома и кэш сборки, с долей, которую можно вернуть. Только чтение – очистка Docker агенту недоступна, она удаляет мимо корзины.")]
    public static Task<string> GetDockerUsageAsync(
        McpBridge bridge,
        [Description("Перечислить сами объекты (образы, контейнеры, тома), а не только итоги по типам")] bool includeObjects = false,
        [Description("Сколько объектов выгружать (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.Insight.GetDockerUsageAsync(includeObjects, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "cleanup_scan")]
    [Description("Замеряет системные корзины Windows со страницы «Очистка»: временные файлы, кэш обновлений и эскизов, Prefetch, дампы, корзина. Только чтение – ничего не удаляет; запускает очистку человек кнопкой на странице. Два итога отвечают на разные вопросы и складывать их не надо: total* – сколько всего лежит в замеренных целях, reclaimable* – сколько из этого вернёт очистка. Расходятся они на целях, которые очистить нельзя (нужны права администратора, каталога нет, Windows.old удаляется штатным средством Windows); причину называет availabilityHint каждой цели, а cleanable отвечает, попала ли она в reclaimable*. К каталогам применяется порог возраста файла из настроек – файлы моложе него не считаются и не удаляются, потому что их может держать живой процесс; к корзине порог не применяется, она считается целиком. Замер без targets обходит все цели и на машине с Windows.old идёт долго. Очистка удаляет безвозвратно, мимо корзины, – в отличие от сканирования и синхронизации.")]
    public static Task<string> CleanupScanAsync(
        McpBridge bridge,
        [Description("Идентификаторы целей (TempFiles, SystemTemp, WindowsUpdate, Prefetch, Thumbnails, RecycleBin, ErrorReports, OldWindowsInstallation); не заданы – замеряются все")] string[]? targets = null,
        CancellationToken cancellationToken = default)
    {
        return bridge.Cleanup.ScanAsync(targets, cancellationToken);
    }

    [McpServerTool(Name = "cleanup_run")]
    [Description("Очищает названные системные корзины Windows. Удаление безвозвратное, мимо корзины, поэтому запуск проходит через живое подтверждение: приложение показывает человеку модальное окно со списком целей и объёмом и ждёт ответа не дольше двух минут. Отказ, молчание и занятое окно возвращают ошибку, а не пустой отчёт. dryRun=true (по умолчанию) ничего не удаляет – замеряет цели и отдаёт план. Цели, которые очистить нельзя, из плана и из запуска выпадают; причину называет availabilityHint в cleanup_scan. К каталогам применяется порог возраста файла из настроек, к корзине он не применяется – она чистится целиком.")]
    public static Task<string> CleanupRunAsync(
        McpBridge bridge,
        [Description("Идентификаторы целей (TempFiles, SystemTemp, WindowsUpdate, Prefetch, Thumbnails, RecycleBin, ErrorReports); хотя бы одна обязательна")] string[] targets,
        [Description("Только план: замерить и показать, что удалилось бы, ничего не удаляя")] bool dryRun = true,
        CancellationToken cancellationToken = default)
    {
        return bridge.Cleanup.RunAsync(targets, dryRun, cancellationToken);
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
        return bridge.Scan.ScanAsync(path, depth, entryLimit, show, cancellationToken);
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
        return bridge.Sync.CompareAsync(left, right, mode, winner, mirror, exclusions, entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "get_current_comparison")]
    [Description("Выгружает сравнение, которое сейчас открыто на странице «Синхронизация», включая состояние Git обеих сторон и итог последней синхронизации.")]
    public static Task<string> GetCurrentComparisonAsync(
        McpBridge bridge,
        [Description("Сколько различий выгружать (крупнейшие по размеру)")] int entryLimit = AppDefaults.McpEntryLimitDefault,
        CancellationToken cancellationToken = default)
    {
        return bridge.Sync.GetCurrentComparisonAsync(entryLimit, cancellationToken);
    }

    [McpServerTool(Name = "sync_current")]
    [Description("Применяет сравнение, открытое на странице «Синхронизация»: копирует файлы и удаляет лишние в корзину. При dryRun=true ничего не выполняется – возвращается компактный план: счётчики действий, объём копируемого и удаляемого, крупнейшие пути. Полное сравнение отдаёт get_current_comparison. Реальное выполнение требует включённой настройки «Разрешить изменяющие операции».")]
    public static Task<string> SyncCurrentAsync(
        McpBridge bridge,
        [Description("true – только показать план, ничего не менять")] bool dryRun = true,
        [Description("Сколько крупнейших путей включать в план")] int entryLimit = SyncPlanExport.DefaultEntryLimit,
        CancellationToken cancellationToken = default)
    {
        return bridge.Sync.SyncCurrentAsync(dryRun, entryLimit, cancellationToken);
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
        return bridge.Scan.ArchiveDirectoryAsync(path, deleteOriginal, dryRun, cancellationToken);
    }

    [McpServerTool(Name = "mark_for_deletion")]
    [Description("Помечает каталоги и файлы открытого сканирования на удаление – ровно как «Ctrl + правый клик» в окне. Ничего не удаляет: пометки видны человеку в дереве, а удаление в корзину запускает он сам кнопкой «Удалить помеченное». Требует включённой настройки «Разрешить изменяющие операции».")]
    public static string MarkForDeletion(
        McpBridge bridge,
        [Description("Пути к каталогам и файлам внутри дерева, открытого на странице «Сканирование»")] string[] paths,
        [Description("true – пометить, false – снять пометку вместе со вложенными")] bool mark = true)
    {
        return bridge.Scan.MarkForDeletion(paths, mark);
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
        return bridge.Sync.OpenSyncAsync(left, right, mode, winner, mirror, exclusions, compare, cancellationToken);
    }
}
