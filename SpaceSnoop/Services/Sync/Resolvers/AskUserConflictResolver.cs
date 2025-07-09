namespace SpaceSnoop.Services.Sync.Resolvers;

//TODO: Сомнительно. Подумать.
/// <summary>
/// Разрешитель конфликтов по стратегии "спросить пользователя".
/// </summary>
public class AskUserConflictResolver : SyncConflictResolverBase
{
    private readonly Func<SyncConflictEventArgs, Task<ConflictResolutionStrategy?>> _userPrompt;

    /// <summary>
    /// Создает новый экземпляр разрешителя с пользовательским запросом.
    /// </summary>
    /// <param name="userPrompt">Функция для запроса решения у пользователя.</param>
    public AskUserConflictResolver(Func<SyncConflictEventArgs, Task<ConflictResolutionStrategy?>> userPrompt)
    {
        _userPrompt = userPrompt;
    }

    /// <inheritdoc />
    public override async Task<ConflictResolutionResult> ResolveConflictAsync(
        SyncOperation operation,
        FileInfo sourceInfo,
        FileInfo targetInfo,
        SyncSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (HasConflict(operation, targetInfo) == false)
        {
            return new(ConflictAction.UseSource, false, "Конфликт отсутствует");
        }

        var conflictArgs = new SyncConflictEventArgs(operation,
            "Файл уже существует в целевой директории",
            sourceInfo,
            targetInfo);

        var userDecision = await _userPrompt(conflictArgs);

        var action = userDecision switch
        {
            ConflictResolutionStrategy.SourceWins => ConflictAction.UseSource,
            ConflictResolutionStrategy.TargetWins => ConflictAction.UseTarget,
            ConflictResolutionStrategy.Skip => ConflictAction.Skip,
            ConflictResolutionStrategy.NewerWins => sourceInfo.LastWriteTime > targetInfo.LastWriteTime
                ? ConflictAction.UseSource
                : ConflictAction.UseTarget,
            _ => ConflictAction.Skip,
        };

        return new(action, conflictArgs.ApplyToAll, $"Решение пользователя: {userDecision}");
    }
}
