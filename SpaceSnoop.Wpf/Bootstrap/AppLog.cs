namespace SpaceSnoop.Wpf.Bootstrap;

internal static partial class AppLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information,
        Message = "Сканирование начато: {Path} (многопоточно: {Multithreaded}, потоков: {Parallelism})")]
    public static partial void ScanStarted(this ILogger logger, string path, bool multithreaded, int parallelism);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information,
        Message = "Сканирование завершено: {Path} – {SizeText}, файлов: {Files}, каталогов: {Directories}, за {ElapsedMs} мс")]
    public static partial void ScanCompleted(this ILogger logger, string path, string sizeText, int files, int directories, long elapsedMs);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Сканирование отменено: {Path}")]
    public static partial void ScanCancelled(this ILogger logger, string path);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Error, Message = "Ошибка сканирования: {Path}")]
    public static partial void ScanFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Удаление запрошено, но нет помеченных элементов")]
    public static partial void NothingMarkedForDeletion(this ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information,
        Message = "Запрошено удаление {Count} помеченных элемент(ов) (безвозвратно: {Permanent})")]
    public static partial void DeletionRequested(this ILogger logger, int count, bool permanent);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug,
        Message = "Результат удаления применён к дереву: убрано узлов – {Count}")]
    public static partial void DeletionResultApplied(this ILogger logger, int count);

    [LoggerMessage(EventId = 1050, Level = LogLevel.Information, Message = "Поиск дубликатов начат: {Path}, порог {MinSize} Б")]
    public static partial void DuplicateSearchStarted(this ILogger logger, string path, long minSize);

    [LoggerMessage(EventId = 1051, Level = LogLevel.Information, Message = "Поиск дубликатов завершён: групп {Groups}, вернёт {ReclaimableBytes} Б, проверено {Examined}")]
    public static partial void DuplicateSearchFinished(this ILogger logger, int groups, long reclaimableBytes, int examined);

    [LoggerMessage(EventId = 1052, Level = LogLevel.Information, Message = "Поиск дубликатов отменён")]
    public static partial void DuplicateSearchCancelled(this ILogger logger);

    [LoggerMessage(EventId = 1053, Level = LogLevel.Error, Message = "Ошибка поиска дубликатов")]
    public static partial void DuplicateSearchFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Не удалось подгрузить заполненность дисков")]
    public static partial void DriveSizesFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information,
        Message = "Обход в один поток: {Path} лежит на диске со штрафом за позиционирование (запрошено потоков: {Requested})")]
    public static partial void ScanMediaLimited(this ILogger logger, string path, int requested);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug,
        Message = "Скан по фазам: обход {WalkMs} мс, показ {ApplyMs} мс")]
    public static partial void ScanPhases(this ILogger logger, long walkMs, long applyMs);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information,
        Message = "Обход по $MFT недоступен для «{Path}» ({Availability}), идём каталогами")]
    public static partial void MftUnavailable(this ILogger logger, string path, MftAvailability availability);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Обход по $MFT для «{Path}» не удался, идём каталогами")]
    public static partial void MftFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Warning,
        Message = "Обход по $MFT для «{Path}» неполон: в дерево не попало {Dropped} объект(ов) на {DroppedBytes} Б, "
                  + "прочитано не полностью {Partial} запись(ей), без известного размера {UnknownSize}; {Report}")]
    public static partial void MftScanIncomplete(
        this ILogger logger,
        string path,
        long dropped,
        long droppedBytes,
        long partial,
        long unknownSize,
        string report);

    [LoggerMessage(EventId = 1100, Level = LogLevel.Information,
        Message = "Старт удаления: {Count} элемент(ов), {BytesText} (безвозвратно: {Permanent})")]
    public static partial void DeletionStarted(this ILogger logger, int count, string bytesText, bool permanent);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Warning, Message = "Не удалось удалить: {Path}")]
    public static partial void DeleteItemFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Warning, Message = "Пропущено (путь не найден): {Path}")]
    public static partial void DeleteItemMissing(this ILogger logger, string path);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information,
        Message = "Удаление завершено (отменено: {Cancelled}): успешно {Deleted}, ошибок {Failed}, освобождено {FreedText}")]
    public static partial void DeletionFinished(this ILogger logger, bool cancelled, int deleted, int failed, string freedText);

    [LoggerMessage(EventId = 1200, Level = LogLevel.Information, Message = "Сравнение начато: «{Left}» ↔ «{Right}»")]
    public static partial void CompareStarted(this ILogger logger, string left, string right);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information,
        Message = "Сравнение завершено: записей {Total}, за {ElapsedMs} мс")]
    public static partial void CompareFinished(this ILogger logger, int total, long elapsedMs);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "Хеширование изменённых файлов начато")]
    public static partial void HashStarted(this ILogger logger);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "Хеширование завершено за {ElapsedMs} мс")]
    public static partial void HashFinished(this ILogger logger, long elapsedMs);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Information, Message = "Синхронизация начата (режим: {Mode})")]
    public static partial void SyncStarted(this ILogger logger, SyncMode mode);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Information,
        Message = "Синхронизация завершена: успешно {Success}, ошибок {Errors}, за {ElapsedMs} мс")]
    public static partial void SyncFinished(this ILogger logger, int success, int errors, long elapsedMs);

    [LoggerMessage(EventId = 1206, Level = LogLevel.Warning, Message = "Ошибка при хешировании файла {Path}")]
    public static partial void HashFileFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1207, Level = LogLevel.Error, Message = "Не удалось записать лог синхронизации")]
    public static partial void SyncLogWriteFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1208, Level = LogLevel.Information, Message = "Сравнение содержимого: «{Path}» (+{Added} −{Removed})")]
    public static partial void ContentCompareOpened(this ILogger logger, string path, int added, int removed);

    [LoggerMessage(EventId = 1209, Level = LogLevel.Warning, Message = "Не удалось сравнить содержимое файла {Path}")]
    public static partial void ContentCompareFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1210, Level = LogLevel.Information, Message = "Git-папки исключены из синхронизации: {Count}")]
    public static partial void SyncGitFoldersSkipped(this ILogger logger, int count);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Information,
        Message = "Автосинхронизация начата: «{Left}» → «{Right}» (режим: {Mode}, зеркало: {Mirror})")]
    public static partial void HeadlessSyncStarted(this ILogger logger, string left, string right, SyncMode mode, bool mirror);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Information,
        Message = "Автосинхронизация завершена: успешно {Success}, ошибок {Errors}, расхождений {Mismatches}, за {ElapsedMs} мс")]
    public static partial void HeadlessSyncFinished(this ILogger logger, int success, int errors, int mismatches, long elapsedMs);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Warning, Message = "Автосинхронизация отменена: {Reason}")]
    public static partial void HeadlessSyncAborted(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Error, Message = "Автосинхронизация прервана ошибкой")]
    public static partial void HeadlessSyncFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Error, Message = "Операция «{Operation}» завершилась ошибкой")]
    public static partial void SyncOperationFailed(this ILogger logger, Exception exception, string operation);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Information, Message = "Операция «{Operation}» отменена")]
    public static partial void SyncOperationCancelled(this ILogger logger, string operation);

    [LoggerMessage(EventId = 1217, Level = LogLevel.Information, Message = "Проверка синхронизации: проверено {Checked}, расхождений {Mismatches}")]
    public static partial void SyncVerified(this ILogger logger, int @checked, int mismatches);

    [LoggerMessage(EventId = 1218, Level = LogLevel.Information, Message = "Состояние Git прочитано: слева «{Left}», справа «{Right}»")]
    public static partial void GitStateRead(this ILogger logger, string left, string right);

    [LoggerMessage(EventId = 1219, Level = LogLevel.Warning, Message = "Не удалось прочитать состояние Git")]
    public static partial void GitStateFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1220, Level = LogLevel.Information,
        Message = "Сравнение выгружено в «{Path}»: записей {Entries}, пропущено {Omitted}")]
    public static partial void ComparisonExported(this ILogger logger, string path, int entries, int omitted);

    [LoggerMessage(EventId = 1221, Level = LogLevel.Error, Message = "Не удалось выгрузить сравнение в «{Path}»")]
    public static partial void ComparisonExportFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1222, Level = LogLevel.Warning,
        Message = "Сравнение неполное: каталогов не прочитано {Count}, первый – «{First}»; удаления в этих ветках отключены")]
    public static partial void CompareIncomplete(this ILogger logger, int count, string first);

    [LoggerMessage(EventId = 1223, Level = LogLevel.Information,
        Message = "Ссылок пропущено при сравнении: {Count}, первая – «{First}»; junction, symlink и облачные заглушки не синхронизируются")]
    public static partial void CompareLinksSkipped(this ILogger logger, int count, string first);

    [LoggerMessage(EventId = 1250, Level = LogLevel.Information, Message = "Профиль расписания сохранён: «{Name}» (активно: {Enabled})")]
    public static partial void ScheduleProfileSaved(this ILogger logger, string name, bool enabled);

    [LoggerMessage(EventId = 1251, Level = LogLevel.Information, Message = "Профиль расписания удалён: «{Name}»")]
    public static partial void ScheduleProfileRemoved(this ILogger logger, string name);

    [LoggerMessage(EventId = 1252, Level = LogLevel.Information, Message = "Профиль расписания запущен вручную: «{Name}»")]
    public static partial void ScheduleProfileRunNow(this ILogger logger, string name);

    [LoggerMessage(EventId = 1253, Level = LogLevel.Warning, Message = "Не удалось применить задачу Планировщика для «{Name}»: {Error}")]
    public static partial void ScheduleTaskFailed(this ILogger logger, string name, string error);

    [LoggerMessage(EventId = 1254, Level = LogLevel.Information, Message = "Расписание перенесено в профиль: «{Name}»")]
    public static partial void ScheduleProfileMigrated(this ILogger logger, string name);

    [LoggerMessage(EventId = 1255, Level = LogLevel.Information, Message = "Создана пачка профилей: {Count}")]
    public static partial void ScheduleBatchCreated(this ILogger logger, int count);

    [LoggerMessage(EventId = 1256, Level = LogLevel.Information, Message = "Задача расписания привязана к текущему пути: «{Name}» → {Exe}")]
    public static partial void ScheduleTaskReconciled(this ILogger logger, string name, string exe);

    [LoggerMessage(EventId = 1257, Level = LogLevel.Warning, Message = "Не удалось обновить путь задачи «{Name}»: {Error}")]
    public static partial void ScheduleReconcileFailed(this ILogger logger, string name, string error);

    [LoggerMessage(EventId = 1258, Level = LogLevel.Information, Message = "Пакетная правка профилей ({Action}): затронуто {Count}")]
    public static partial void ScheduleBulkApplied(this ILogger logger, string action, int count);

    [LoggerMessage(EventId = 1259, Level = LogLevel.Information, Message = "Пакетно удалено профилей: {Count}")]
    public static partial void ScheduleBulkRemoved(this ILogger logger, int count);

    [LoggerMessage(EventId = 1260, Level = LogLevel.Warning, Message = "Журнал синхронизации «{Path}» не прочитан – история показана без него")]
    public static partial void SyncLogFileUnreadable(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Error, Message = "Не удалось открыть форму репорта")]
    public static partial void ReportFormFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Не удалось сохранить файл настроек {Path} перед показом в проводнике")]
    public static partial void SettingsFlushFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "Расположение данных изменено: {Path}")]
    public static partial void StorageLocationChanged(this ILogger logger, string path);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Error, Message = "Не удалось изменить расположение данных: {Path}")]
    public static partial void StorageLocationChangeFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1404, Level = LogLevel.Warning, Message = "После переноса данных в прежнем расположении осталось файлов: {Count} (заняты другим процессом)")]
    public static partial void StorageSourceFilesLeft(this ILogger logger, int count);

    [LoggerMessage(EventId = 1600, Level = LogLevel.Information, Message = "Docker: снимок получен, категорий – {Count}")]
    public static partial void DockerSnapshotLoaded(this ILogger logger, int count);

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "Docker недоступен: {Reason}")]
    public static partial void DockerUnavailable(this ILogger logger, string reason);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Information, Message = "Docker: очистка начата ({Target})")]
    public static partial void DockerCleanupStarted(this ILogger logger, string target);

    [LoggerMessage(EventId = 1603, Level = LogLevel.Information, Message = "Docker: очистка завершена ({Target}): {Summary}")]
    public static partial void DockerCleanupFinished(this ILogger logger, string target, string summary);

    [LoggerMessage(EventId = 1604, Level = LogLevel.Error, Message = "Docker: ошибка очистки ({Target})")]
    public static partial void DockerCleanupFailed(this ILogger logger, Exception exception, string target);

    [LoggerMessage(EventId = 1605, Level = LogLevel.Information, Message = "Docker: сжатие диска начато")]
    public static partial void DockerCompactStarted(this ILogger logger);

    [LoggerMessage(EventId = 1606, Level = LogLevel.Information, Message = "Docker: сжатие диска завершено: {Summary}")]
    public static partial void DockerCompactFinished(this ILogger logger, string summary);

    [LoggerMessage(EventId = 1607, Level = LogLevel.Error, Message = "Docker: ошибка сжатия диска")]
    public static partial void DockerCompactFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1611, Level = LogLevel.Information, Message = "Docker: сжатие диска отменено")]
    public static partial void DockerCompactCancelled(this ILogger logger);

    [LoggerMessage(EventId = 1608, Level = LogLevel.Information, Message = "Docker: инвентарь получен, объектов – {Count}, групп – {Groups}")]
    public static partial void DockerInventoryLoaded(this ILogger logger, int count, int groups);

    [LoggerMessage(EventId = 1609, Level = LogLevel.Information, Message = "Docker: объект удалён ({Kind} {Name})")]
    public static partial void DockerObjectRemoved(this ILogger logger, string kind, string name);

    [LoggerMessage(EventId = 1610, Level = LogLevel.Error, Message = "Docker: ошибка удаления объекта ({Kind} {Name})")]
    public static partial void DockerObjectRemoveFailed(this ILogger logger, Exception exception, string kind, string name);

    [LoggerMessage(EventId = 1700, Level = LogLevel.Information, Message = "Упаковка в архив начата: {Path}")]
    public static partial void ArchiveStarted(this ILogger logger, string path);

    [LoggerMessage(EventId = 1701, Level = LogLevel.Information, Message = "Упаковка в архив завершена: {Path} – {Summary}")]
    public static partial void ArchiveFinished(this ILogger logger, string path, string summary);

    [LoggerMessage(EventId = 1702, Level = LogLevel.Error, Message = "Ошибка архивации: {Path}")]
    public static partial void ArchiveFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1703, Level = LogLevel.Information, Message = "Архивация отменена: {Path}")]
    public static partial void ArchiveCancelled(this ILogger logger, string path);

    [LoggerMessage(EventId = 1704, Level = LogLevel.Warning, Message = "Архив не прошёл проверку, оригинал не тронут: {Path} ({Detail})")]
    public static partial void ArchiveVerifyFailed(this ILogger logger, string path, string detail);

    [LoggerMessage(EventId = 1800, Level = LogLevel.Information, Message = "Доступно обновление: {Latest} (текущая {Current})")]
    public static partial void UpdateAvailable(this ILogger logger, string latest, string current);

    [LoggerMessage(EventId = 1801, Level = LogLevel.Debug, Message = "Обновлений нет: текущая {Current}, последняя {Latest}")]
    public static partial void UpdateUpToDate(this ILogger logger, string current, string latest);

    [LoggerMessage(EventId = 1802, Level = LogLevel.Debug, Message = "Не удалось проверить обновления")]
    public static partial void UpdateCheckFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1803, Level = LogLevel.Information, Message = "Обновление скачано: {Path}")]
    public static partial void UpdateDownloaded(this ILogger logger, string path);

    [LoggerMessage(EventId = 1804, Level = LogLevel.Warning, Message = "Не удалось скачать обновление: {Url}")]
    public static partial void UpdateDownloadFailed(this ILogger logger, Exception exception, string url);

    [LoggerMessage(EventId = 1805, Level = LogLevel.Debug, Message = "Незавершённая загрузка не удалена: {Path}")]
    public static partial void UpdateTempFileLeft(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1900, Level = LogLevel.Information, Message = "Пакетное сравнение начато: пар {Total}")]
    public static partial void OverviewCompareStarted(this ILogger logger, int total);

    [LoggerMessage(EventId = 1901, Level = LogLevel.Information, Message = "Пакетное сравнение завершено: сравнено {Compared}, ошибок {Failed}, пропущено {Skipped}, за {ElapsedMs} мс")]
    public static partial void OverviewCompareFinished(this ILogger logger, int compared, int failed, int skipped, long elapsedMs);

    [LoggerMessage(EventId = 1902, Level = LogLevel.Information, Message = "Пакетное сравнение отменено")]
    public static partial void OverviewCompareCancelled(this ILogger logger);

    [LoggerMessage(EventId = 1903, Level = LogLevel.Information, Message = "Пакетная синхронизация начата: профилей {Total}")]
    public static partial void OverviewSyncStarted(this ILogger logger, int total);

    [LoggerMessage(EventId = 1904, Level = LogLevel.Information, Message = "Пакетная синхронизация завершена: успешно {Synced}, с ошибками {Failed}, пропущено {Skipped}, за {ElapsedMs} мс")]
    public static partial void OverviewSyncFinished(this ILogger logger, int synced, int failed, int skipped, long elapsedMs);

    [LoggerMessage(EventId = 1905, Level = LogLevel.Information, Message = "Пакетная синхронизация отменена")]
    public static partial void OverviewSyncCancelled(this ILogger logger);

    [LoggerMessage(EventId = 1906, Level = LogLevel.Information, Message = "Профиль пропущен пользователем: {Name}")]
    public static partial void OverviewRowSkipped(this ILogger logger, string name);

    [LoggerMessage(EventId = 2000, Level = LogLevel.Information, Message = "MCP-сервер запущен: {Endpoint}")]
    public static partial void McpServerStarted(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "MCP-сервер остановлен")]
    public static partial void McpServerStopped(this ILogger logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "MCP-сервер не смог запуститься на порту {Port}")]
    public static partial void McpServerFailed(this ILogger logger, Exception exception, int port);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "MCP-инструмент вызван: {Tool} ({Details})")]
    public static partial void McpToolInvoked(this ILogger logger, string tool, string details);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "MCP-инструмент отклонён: {Tool} ({Reason})")]
    public static partial void McpToolRejected(this ILogger logger, string tool, string reason);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Information, Message = "MCP-инструмент изменяет данные: {Tool} ({Details})")]
    public static partial void McpMutationRequested(this ILogger logger, string tool, string details);

    [LoggerMessage(EventId = 2006, Level = LogLevel.Warning, Message = "MCP-сервер поднят в процессе с правами администратора: {Endpoint}")]
    public static partial void McpServerElevated(this ILogger logger, string endpoint);

    [LoggerMessage(EventId = 2007, Level = LogLevel.Warning, Message = "MCP-сервер не запущен: токен доступа не задан")]
    public static partial void McpServerTokenMissing(this ILogger logger);

    [LoggerMessage(EventId = 2008, Level = LogLevel.Warning, Message = "MCP-сервер не остановился за {Seconds} с, выход продолжен без него")]
    public static partial void McpServerStopTimedOut(this ILogger logger, int seconds);

    [LoggerMessage(EventId = 2009, Level = LogLevel.Error, Message = "MCP-сервер не остановился штатно")]
    public static partial void McpServerStopFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2010, Level = LogLevel.Debug, Message = "scan_directory по фазам: обход {WalkMs} мс, выгрузка {ExportMs} мс, сериализация {JsonMs} мс, показ {ApplyMs} мс, ответ {JsonLength} знаков")]
    public static partial void McpScanPhases(this ILogger logger, long walkMs, long exportMs, long jsonMs, long applyMs, int jsonLength);

    [LoggerMessage(EventId = 2100, Level = LogLevel.Information, Message = "CLI агента найден: {Path} ({Version})")]
    public static partial void AgentCliDetected(this ILogger logger, string path, string version);

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "CLI агента «{Backend}» не найден на машине")]
    public static partial void AgentCliMissing(this ILogger logger, string backend);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Debug, Message = "Ход агента «{Backend}» начат: инструментов {Tools}, продолжение сессии {Resume}")]
    public static partial void AgentTurnStarted(this ILogger logger, string backend, int tools, bool resume);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Ход агента завершён за {ElapsedMs} мс, стоимость {CostUsd} $, токенов {Tokens}")]
    public static partial void AgentTurnCompleted(this ILogger logger, long elapsedMs, double costUsd, long tokens);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Error, Message = "Ход агента оборвался: {Reason}")]
    public static partial void AgentTurnFailed(this ILogger logger, Exception? exception, string reason);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Information, Message = "Ход агента отменён пользователем")]
    public static partial void AgentTurnCancelled(this ILogger logger);

    [LoggerMessage(EventId = 2106, Level = LogLevel.Debug, Message = "Агент вызвал инструмент: {Tool}")]
    public static partial void AgentToolInvoked(this ILogger logger, string tool);

    [LoggerMessage(EventId = 2107, Level = LogLevel.Information, Message = "Отправка данных агенту разрешена пользователем")]
    public static partial void AgentConsentGranted(this ILogger logger);

    [LoggerMessage(EventId = 2108, Level = LogLevel.Information, Message = "Ход агента получил изменяющие инструменты: {Tools}")]
    public static partial void AgentMutationsGranted(this ILogger logger, string tools);

    [LoggerMessage(EventId = 2109, Level = LogLevel.Warning, Message = "MCP-сервер «{Name}» агенту не подключился (статус «{Status}») – его инструменты будут недоступны")]
    public static partial void AgentMcpServerUnavailable(this ILogger logger, string name, string status);

    [LoggerMessage(EventId = 2110, Level = LogLevel.Warning, Message = "Агент сообщил об ошибке в потоке: {Message}")]
    public static partial void AgentStreamError(this ILogger logger, string message);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Information, Message = "Бэкенд агента переключён на «{Backend}»")]
    public static partial void AgentBackendChanged(this ILogger logger, string backend);

    [LoggerMessage(EventId = 2112, Level = LogLevel.Warning, Message = "Поиск CLI агента «{Backend}» сорвался")]
    public static partial void AgentCliDetectionFailed(this ILogger logger, Exception exception, string backend);

    [LoggerMessage(EventId = 2114, Level = LogLevel.Information, Message = "Каталог моделей «{Backend}» обновлён: {Count}")]
    public static partial void AgentModelsLoaded(this ILogger logger, string backend, int count);

    [LoggerMessage(EventId = 2115, Level = LogLevel.Warning, Message = "Не удалось получить список моделей «{Backend}»")]
    public static partial void AgentModelsFailed(this ILogger logger, Exception exception, string backend);

    [LoggerMessage(EventId = 2116, Level = LogLevel.Debug, Message = "Вопрос агенту подставлен со страницы: {Question}")]
    public static partial void AgentQuestionPrefilled(this ILogger logger, string question);

    [LoggerMessage(EventId = 2117, Level = LogLevel.Debug, Message = "История чата прочитана: разговоров {Count}")]
    public static partial void ChatHistoryLoaded(this ILogger logger, int count);

    [LoggerMessage(EventId = 2118, Level = LogLevel.Warning, Message = "Не удалось прочитать историю чата «{Path}» – разговоры начнутся с чистого листа")]
    public static partial void ChatHistoryReadFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2119, Level = LogLevel.Warning, Message = "Не удалось сохранить историю чата «{Path}»")]
    public static partial void ChatHistoryWriteFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2120, Level = LogLevel.Information, Message = "Сессия CLI из сохранённого разговора не подхватилась – следующий ход начнётся заново")]
    public static partial void ChatRestoredSessionDropped(this ILogger logger);

    [LoggerMessage(EventId = 2121, Level = LogLevel.Information, Message = "История чата очищена: удалено разговоров {Count}")]
    public static partial void ChatHistoryCleared(this ILogger logger, int count);

    [LoggerMessage(EventId = 2122, Level = LogLevel.Debug, Message = "Транскрипт хода агента пишется в «{Path}»")]
    public static partial void AgentTranscriptStarted(this ILogger logger, string path);

    [LoggerMessage(EventId = 2123, Level = LogLevel.Warning, Message = "Транскрипт хода агента «{Path}» не пишется")]
    public static partial void AgentTranscriptFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2125, Level = LogLevel.Information, Message = "Разговор откачен к вопросу: убрано сообщений {Count}, сессия CLI сброшена")]
    public static partial void ChatRewound(this ILogger logger, int count);

    [LoggerMessage(EventId = 2126, Level = LogLevel.Debug, Message = "Переход на страницу «{Page}» отложен до конца хода агента")]
    public static partial void AgentNavigationDeferred(this ILogger logger, string page);

    [LoggerMessage(EventId = 2127, Level = LogLevel.Debug, Message = "Процесс CLI агента не остановлен – возможно, он уже завершился")]
    public static partial void AgentProcessKillFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2128, Level = LogLevel.Debug, Message = "Временный файл хода агента не удалён: {Path}")]
    public static partial void AgentTempFileLeft(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2129, Level = LogLevel.Debug, Message = "Чтение stderr CLI агента оборвано – хвост вывода неполон")]
    public static partial void AgentStderrReadFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2200, Level = LogLevel.Information, Message = "Снимок сохранён: {Path} ({Width}×{Height})")]
    public static partial void ViewCaptured(this ILogger logger, string path, int width, int height);

    [LoggerMessage(EventId = 2201, Level = LogLevel.Warning, Message = "Снимок «{Path}» не сохранён")]
    public static partial void ViewCaptureFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 2202, Level = LogLevel.Information, Message = "Галерея: {Cases} страниц × {Themes} тем в «{Directory}»")]
    public static partial void GalleryStarted(this ILogger logger, int cases, int themes, string directory);

    [LoggerMessage(EventId = 2203, Level = LogLevel.Information, Message = "Галерея готова: кадров {Frames}, пропущено {Skipped}, за {ElapsedMs} мс")]
    public static partial void GalleryFinished(this ILogger logger, int frames, int skipped, long elapsedMs);

    [LoggerMessage(EventId = 2204, Level = LogLevel.Warning, Message = "Галерея: кадр «{Case}» не снят")]
    public static partial void GalleryCaseFailed(this ILogger logger, Exception exception, string @case);

    [LoggerMessage(EventId = 2205, Level = LogLevel.Information, Message = "Галерея: состояние «{State}» не снимается для {Cases} кейсов")]
    public static partial void GalleryStateSkipped(this ILogger logger, string state, int cases);

    [LoggerMessage(EventId = 2300, Level = LogLevel.Warning, Message = "Интерфейс не отвечал {DelayMs} мс ({Operation})")]
    public static partial void PerformanceHitch(this ILogger logger, long delayMs, string operation);

    [LoggerMessage(EventId = 2301, Level = LogLevel.Warning, Message = "Подписчик на замер производительности бросил исключение")]
    public static partial void PerformanceListenerFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2400, Level = LogLevel.Information, Message = "Очистка: запуск, целей {Targets}, ожидается {PlannedBytes} Б")]
    public static partial void CleanupRunStarted(this ILogger logger, int targets, long plannedBytes);

    [LoggerMessage(EventId = 2401, Level = LogLevel.Information, Message = "Очистка: удалено {Deleted}, освобождено {FreedBytes} Б, пропущено {Skipped}")]
    public static partial void CleanupRunFinished(this ILogger logger, int deleted, long freedBytes, int skipped);

    [LoggerMessage(EventId = 2402, Level = LogLevel.Error, Message = "Очистка: прервана ошибкой")]
    public static partial void CleanupRunFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2403, Level = LogLevel.Information, Message = "Очистка: отменена, удалено {Deleted}, освобождено {FreedBytes} Б")]
    public static partial void CleanupRunCancelled(this ILogger logger, int deleted, long freedBytes);

    [LoggerMessage(EventId = 2404, Level = LogLevel.Warning, Message = "Очистка: не удалось замерить цель «{TargetId}»")]
    public static partial void CleanupMeasureFailed(this ILogger logger, Exception exception, string targetId);

    [LoggerMessage(EventId = 2405, Level = LogLevel.Information, Message = "Очистка: агент запросил подтверждение, целей {Targets}, ожидается {PlannedBytes} Б")]
    public static partial void CleanupRunRequested(this ILogger logger, int targets, long plannedBytes);

    [LoggerMessage(EventId = 2406, Level = LogLevel.Warning, Message = "Очистка: запрос агента не подтверждён ({Consent})")]
    public static partial void CleanupRunDeclined(this ILogger logger, string consent);
}
