using Microsoft.Extensions.Logging;

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
}
