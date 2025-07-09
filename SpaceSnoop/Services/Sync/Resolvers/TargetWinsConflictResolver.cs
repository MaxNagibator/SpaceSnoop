namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Разрешитель конфликтов по стратегии "цель побеждает".
/// </summary>
public class TargetWinsConflictResolver : SyncConflictResolverBase
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
            ? new(ConflictAction.UseTarget, false, "Всегда использовать целевой файл")
            : new(ConflictAction.UseSource, false, "Конфликт отсутствует");

        return Task.FromResult(result);
    }
}
