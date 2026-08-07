using Microsoft.Extensions.Logging;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Core;

internal static partial class CoreLog
{
    [LoggerMessage(EventId = 1210, Level = LogLevel.Debug, Message = "Синхронизация: {Action} «{RelativePath}»")]
    public static partial void SyncFileApplied(this ILogger logger, SyncAction action, string relativePath);

    [LoggerMessage(EventId = 1211, Level = LogLevel.Warning, Message = "Синхронизация: не удалось {Action} «{RelativePath}»")]
    public static partial void SyncFileFailed(this ILogger logger, Exception exception, SyncAction action, string relativePath);

    [LoggerMessage(EventId = 1212, Level = LogLevel.Warning, Message = "Сравнение: каталог пропущен (нет доступа) «{Path}»")]
    public static partial void CompareDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1213, Level = LogLevel.Warning, Message = "Сканирование: каталог пропущен «{Path}»")]
    public static partial void ScanDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1214, Level = LogLevel.Debug, Message = "Сканирование: пропущена ссылка (reparse point) «{Path}»")]
    public static partial void ScanReparsePointSkipped(this ILogger logger, string path);

    [LoggerMessage(EventId = 1215, Level = LogLevel.Debug, Message = "Сравнение: пропущена ссылка (reparse point) «{Path}»")]
    public static partial void CompareReparsePointSkipped(this ILogger logger, string path);

    [LoggerMessage(EventId = 1216, Level = LogLevel.Warning, Message = "Синхронизация: {Action} «{RelativePath}» отклонено, обход стороны неполон")]
    public static partial void SyncDeleteBlocked(this ILogger logger, SyncAction action, string relativePath);

    [LoggerMessage(EventId = 1217, Level = LogLevel.Warning,
        Message = "Синхронизация: {Action} «{RelativePath}» отклонено, конфликт вида объектов ({Conflict})")]
    public static partial void SyncTypeConflictBlocked(this ILogger logger, SyncAction action, string relativePath, FileTypeConflict conflict);

    [LoggerMessage(EventId = 1250, Level = LogLevel.Information, Message = "Дубликаты: групп {Groups}, вернёт {ReclaimableBytes} Б, проверено файлов {Examined}")]
    public static partial void DuplicatesFinished(this ILogger logger, int groups, long reclaimableBytes, int examined);

    [LoggerMessage(EventId = 1251, Level = LogLevel.Warning, Message = "Дубликаты: файл пропущен «{Path}»")]
    public static partial void DuplicateFileSkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1240, Level = LogLevel.Information, Message = "Очистка: начата цель «{TargetId}»")]
    public static partial void CleanupStarted(this ILogger logger, string targetId);

    [LoggerMessage(EventId = 1241, Level = LogLevel.Information, Message = "Очистка: цель «{TargetId}» – удалено {Deleted}, освобождено {FreedBytes} Б")]
    public static partial void CleanupFinished(this ILogger logger, string targetId, int deleted, long freedBytes);

    [LoggerMessage(EventId = 1242, Level = LogLevel.Warning, Message = "Очистка: не удалось удалить «{Path}»")]
    public static partial void CleanupFileFailed(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1243, Level = LogLevel.Warning, Message = "Очистка: каталог пропущен «{Path}»")]
    public static partial void CleanupDirectorySkipped(this ILogger logger, Exception exception, string path);

    [LoggerMessage(EventId = 1244, Level = LogLevel.Warning, Message = "Очистка: цель «{TargetId}» недоступна ({Availability})")]
    public static partial void CleanupTargetUnavailable(this ILogger logger, string targetId, CleanupAvailability availability);
}
