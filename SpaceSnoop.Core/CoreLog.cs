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
}
