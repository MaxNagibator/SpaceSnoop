namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Интерфейс для разрешения конфликтов синхронизации.
/// </summary>
public interface ISyncConflictResolver
{
    /// <summary>
    /// Разрешает конфликт между исходным и целевым файлами.
    /// </summary>
    /// <param name="operation">Операция, вызвавшая конфликт.</param>
    /// <param name="sourceInfo">Информация об исходном файле.</param>
    /// <param name="targetInfo">Информация о целевом файле.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>Результат разрешения конфликта.</returns>
    Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default);
}
