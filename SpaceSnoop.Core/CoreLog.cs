using Microsoft.Extensions.Logging;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Core;

internal static partial class CoreLog
{
    [LoggerMessage(EventId = 1510, Level = LogLevel.Debug, Message = "Синхронизация: {Action} «{RelativePath}»")]
    public static partial void SyncFileApplied(this ILogger logger, SyncAction action, string relativePath);

    [LoggerMessage(EventId = 1511, Level = LogLevel.Warning, Message = "Синхронизация: не удалось {Action} «{RelativePath}»")]
    public static partial void SyncFileFailed(this ILogger logger, Exception exception, SyncAction action, string relativePath);

    [LoggerMessage(EventId = 1512, Level = LogLevel.Warning, Message = "Сравнение: каталог пропущен (нет доступа) «{Path}»")]
    public static partial void CompareDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1524, Level = LogLevel.Warning, Message = "Сравнение: каталог пропущен «{Path}» ({ErrorType}: {Reason}), стек – у первого пропуска сравнения")]
    public static partial void CompareDirectorySkippedAgain(this ILogger logger, string path, string errorType, string reason);

    [LoggerMessage(EventId = 1525, Level = LogLevel.Warning, Message = "Сравнение: пропущено каталогов {Count}")]
    public static partial void CompareDirectoriesSkippedTotal(this ILogger logger, int count);

    [LoggerMessage(EventId = 1513, Level = LogLevel.Warning, Message = "Сканирование: каталог пропущен «{Path}»")]
    public static partial void ScanDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1518, Level = LogLevel.Warning, Message = "Сканирование: каталог пропущен «{Path}» ({ErrorType}: {Reason}), стек – у первого пропуска скана")]
    public static partial void ScanDirectorySkippedAgain(this ILogger logger, string path, string errorType, string reason);

    [LoggerMessage(EventId = 1514, Level = LogLevel.Debug, Message = "Сканирование: пропущена ссылка (reparse point) «{Path}»")]
    public static partial void ScanReparsePointSkipped(this ILogger logger, string path);

    [LoggerMessage(EventId = 1515, Level = LogLevel.Debug, Message = "Сравнение: пропущена ссылка (reparse point) «{Path}»")]
    public static partial void CompareReparsePointSkipped(this ILogger logger, string path);

    [LoggerMessage(EventId = 1516, Level = LogLevel.Warning, Message = "Синхронизация: {Action} «{RelativePath}» отклонено, обход стороны неполон")]
    public static partial void SyncDeleteBlocked(this ILogger logger, SyncAction action, string relativePath);

    [LoggerMessage(
        EventId = 1517,
        Level = LogLevel.Warning,
        Message = "Синхронизация: {Action} «{RelativePath}» отклонено, конфликт вида объектов ({Conflict})")]
    public static partial void SyncTypeConflictBlocked(this ILogger logger, SyncAction action, string relativePath, FileTypeConflict conflict);

    [LoggerMessage(EventId = 1530, Level = LogLevel.Information, Message = "Очистка проектов: начат каталог «{Path}»")]
    public static partial void ProjectCleanupTargetStarted(this ILogger logger, string path);

    [LoggerMessage(EventId = 1531, Level = LogLevel.Information, Message = "Очистка проектов: каталог «{Path}» – в корзину файлов {Deleted}, {Bytes} Б")]
    public static partial void ProjectCleanupTargetFinished(this ILogger logger, string path, int deleted, long bytes);

    [LoggerMessage(EventId = 1532, Level = LogLevel.Warning, Message = "Очистка проектов: корни не прошли проверку «{Roots}»")]
    public static partial void ProjectCleanupRootsRejected(this ILogger logger, Exception exception, string roots);

    [LoggerMessage(EventId = 1533, Level = LogLevel.Warning, Message = "Очистка проектов: каталог не очищен «{Path}»")]
    public static partial void ProjectCleanupTargetFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1534, Level = LogLevel.Warning, Message = "Очистка проектов: файл оставлен на месте «{Path}»")]
    public static partial void ProjectCleanupFileSkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1535, Level = LogLevel.Warning, Message = "Очистка проектов: файл не возвращён «{Path}» из «{Staged}»")]
    public static partial void ProjectCleanupRestoreFailed(this ILogger logger, Exception exception, string path, string staged);

    [LoggerMessage(EventId = 1536, Level = LogLevel.Warning, Message = "Очистка проектов: каталог восстановления оставлен «{Path}»")]
    public static partial void ProjectCleanupStagingKept(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1550, Level = LogLevel.Information, Message = "Дубликаты: групп {Groups}, вернёт {ReclaimableBytes} Б, проверено файлов {Examined}")]
    public static partial void DuplicatesFinished(this ILogger logger, int groups, long reclaimableBytes, int examined);

    [LoggerMessage(EventId = 1551, Level = LogLevel.Warning, Message = "Дубликаты: файл пропущен «{Path}»")]
    public static partial void DuplicateFileSkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1540, Level = LogLevel.Information, Message = "Очистка: начата цель «{TargetId}»")]
    public static partial void CleanupStarted(this ILogger logger, string targetId);

    [LoggerMessage(EventId = 1541, Level = LogLevel.Information, Message = "Очистка: цель «{TargetId}» – удалено {Deleted}, освобождено {FreedBytes} Б")]
    public static partial void CleanupFinished(this ILogger logger, string targetId, int deleted, long freedBytes);

    [LoggerMessage(EventId = 1542, Level = LogLevel.Warning, Message = "Очистка: не удалось удалить «{Path}»")]
    public static partial void CleanupFileFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1543, Level = LogLevel.Warning, Message = "Очистка: каталог пропущен «{Path}»")]
    public static partial void CleanupDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1544, Level = LogLevel.Warning, Message = "Очистка: цель «{TargetId}» недоступна ({Availability})")]
    public static partial void CleanupTargetUnavailable(this ILogger logger, string targetId, CleanupAvailability availability);

    [LoggerMessage(EventId = 1545, Level = LogLevel.Information, Message = "Очистка NuGet: начато категорий {Caches}")]
    public static partial void NuGetCleanupStarted(this ILogger logger, int caches);

    [LoggerMessage(EventId = 1546, Level = LogLevel.Information, Message = "Очистка NuGet: удалено {DeletedFiles}, {FreedBytes} Б, пропущено {Skipped}, ошибок {Errors}, отменено {Cancelled}")]
    public static partial void NuGetCleanupCompleted(this ILogger logger, int deletedFiles, long freedBytes, int skipped, int errors, bool cancelled);

    [LoggerMessage(EventId = 1547, Level = LogLevel.Warning, Message = "Очистка NuGet завершилась с ошибкой")]
    public static partial void NuGetCleanupFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1548, Level = LogLevel.Warning, Message = "Замер NuGet {Kind}: «{Path}», пакет исключён: {PackageExcluded}")]
    public static partial void NuGetCacheScanFailed(this ILogger logger, Exception exception, string kind, string path, bool packageExcluded);

    [LoggerMessage(EventId = 1549, Level = LogLevel.Information, Message = "Замер NuGet {Kind}: «{Path}», файлов {Files}, байт {Bytes}, ошибок {Errors}, исключено пакетов {ExcludedPackages}")]
    public static partial void NuGetCacheScanCompleted(this ILogger logger, string kind, string path, int files, long bytes, int errors, int excludedPackages);

    [LoggerMessage(
        EventId = 1560,
        Level = LogLevel.Information,
        Message = "Обход по $MFT: «{Path}», записей {Records}, повторных имён {ExtraNames} на {ExtraNameBytes} Б ({Report})")]
    public static partial void MftScanCompleted(
        this ILogger logger,
        string path,
        int records,
        long extraNames,
        long extraNameBytes,
        string report);

    [LoggerMessage(EventId = 1570, Level = LogLevel.Information, Message = "Сетевой диск {LocalName} переподключён в сеансе администратора: «{RemotePath}»")]
    public static partial void NetworkDriveRestored(this ILogger logger, string localName, string remotePath);

    [LoggerMessage(EventId = 1571, Level = LogLevel.Warning, Message = "Сетевой диск {LocalName} «{RemotePath}» не переподключён: код Win32 {ErrorCode} ({Reason})")]
    public static partial void NetworkDriveRestoreFailed(this ILogger logger, string localName, string remotePath, int errorCode, string reason);
}
