namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Разрешитель конфликтов по стратегии "пропускать".
/// </summary>
public class SkipConflictResolver : SyncConflictResolverBase
{
    /// <inheritdoc />
    public override Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        ConflictResolutionResult result = HasConflict(operation, targetInfo)
            ? new(ConflictAction.Skip, false, "Пропуск конфликтующих файлов")
            : new(ConflictAction.UseSource, false, "Конфликт отсутствует");

        return Task.FromResult(result);
    }
}
