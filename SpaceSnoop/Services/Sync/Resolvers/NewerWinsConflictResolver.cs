namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Разрешитель конфликтов по стратегии "новее побеждает".
/// </summary>
public class NewerWinsConflictResolver : SyncConflictResolverBase
{
    /// <inheritdoc />
    public override Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        ConflictResolutionResult result;

        if (HasConflict(operation, targetInfo))
        {
            var action = sourceInfo.LastWriteTime > targetInfo.LastWriteTime
                ? ConflictAction.UseSource
                : ConflictAction.UseTarget;

            var reason = action == ConflictAction.UseSource
                ? "Исходный файл новее"
                : "Целевой файл новее";

            result = new(action, false, reason);
        }
        else
        {
            result = new(ConflictAction.UseSource, false, "Конфликт отсутствует");
        }

        return Task.FromResult(result);
    }
}
