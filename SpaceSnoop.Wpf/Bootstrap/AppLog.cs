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

    [LoggerMessage(EventId = 1007, Level = LogLevel.Warning,
        Message = "Не удалось подгрузить заполненность дисков")]
    public static partial void DriveSizesFailed(this ILogger logger, Exception exception);

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

    [LoggerMessage(EventId = 1300, Level = LogLevel.Warning, Message = "Не удалось положить лог-секцию в буфер обмена")]
    public static partial void ClipboardLogSectionFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1301, Level = LogLevel.Error, Message = "Не удалось открыть форму репорта")]
    public static partial void ReportFormFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1302, Level = LogLevel.Warning, Message = "Не удалось скопировать сведения об окружении в буфер обмена")]
    public static partial void DiagnosticsCopyFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1303, Level = LogLevel.Warning, Message = "Не удалось открыть ссылку {Url}")]
    public static partial void OpenUrlFailed(this ILogger logger, Exception exception, string url);

    [LoggerMessage(EventId = 1400, Level = LogLevel.Warning, Message = "Не удалось показать файл настроек {Path}")]
    public static partial void ShowSettingsFileFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Warning, Message = "Не удалось скопировать путь к файлу настроек")]
    public static partial void CopySettingsPathFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Information, Message = "Расположение данных изменено: {Path}")]
    public static partial void StorageLocationChanged(this ILogger logger, string path);

    [LoggerMessage(EventId = 1403, Level = LogLevel.Error, Message = "Не удалось изменить расположение данных: {Path}")]
    public static partial void StorageLocationChangeFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1500, Level = LogLevel.Warning, Message = "Не удалось открыть проводник для {Path}")]
    public static partial void OpenExplorerFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1501, Level = LogLevel.Warning, Message = "Не удалось скопировать текст в буфер обмена")]
    public static partial void ClipboardTextCopyFailed(this ILogger logger, Exception exception);

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

    [LoggerMessage(EventId = 1606, Level = LogLevel.Information, Message = "Docker: сжатие диска завершено")]
    public static partial void DockerCompactFinished(this ILogger logger);

    [LoggerMessage(EventId = 1607, Level = LogLevel.Error, Message = "Docker: ошибка сжатия диска")]
    public static partial void DockerCompactFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1608, Level = LogLevel.Information, Message = "Docker: инвентарь получен, объектов – {Count}, групп – {Groups}")]
    public static partial void DockerInventoryLoaded(this ILogger logger, int count, int groups);

    [LoggerMessage(EventId = 1609, Level = LogLevel.Information, Message = "Docker: объект удалён ({Kind} {Name})")]
    public static partial void DockerObjectRemoved(this ILogger logger, string kind, string name);

    [LoggerMessage(EventId = 1610, Level = LogLevel.Error, Message = "Docker: ошибка удаления объекта ({Kind} {Name})")]
    public static partial void DockerObjectRemoveFailed(this ILogger logger, Exception exception, string kind, string name);
}
