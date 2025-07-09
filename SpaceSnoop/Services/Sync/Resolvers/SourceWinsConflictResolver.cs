namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Разрешитель конфликтов по стратегии "источник побеждает".
/// </summary>
public class SourceWinsConflictResolver : SyncConflictResolverBase
{
    /// <inheritdoc />
    public override Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        var result = new ConflictResolutionResult(ConflictAction.UseSource, false, "Всегда использовать исходный файл");
        return Task.FromResult(result);
    }
}
