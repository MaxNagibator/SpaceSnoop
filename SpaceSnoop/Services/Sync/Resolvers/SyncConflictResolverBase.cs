namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Базовый класс для разрешителей конфликтов.
/// </summary>
public abstract class SyncConflictResolverBase : ISyncConflictResolver
{
    /// <inheritdoc />
    public abstract Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Проверяет, нужно ли разрешать конфликт для данной операции.
    /// </summary>
    /// <param name="operation">Операция синхронизации.</param>
    /// <param name="targetInfo">Информация о целевом файле.</param>
    /// <returns>True, если конфликт существует, иначе false.</returns>
    protected static bool HasConflict(SyncOperation operation, FileInfo targetInfo)
    {
        return operation.OperationType == SyncOperationType.Copy && targetInfo.Exists;
    }
}
