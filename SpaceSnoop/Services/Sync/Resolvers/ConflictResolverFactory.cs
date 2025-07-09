namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Фабрика для создания разрешителей конфликтов.
/// </summary>
public static class ConflictResolverFactory
{
    /// <summary>
    /// Создает разрешитель конфликтов на основе стратегии.
    /// </summary>
    /// <param name="strategy">Стратегия разрешения конфликтов.</param>
    /// <param name="userPrompt">Функция для запроса решения у пользователя (только для стратегии AskUser).</param>
    /// <returns>Экземпляр разрешителя конфликтов.</returns>
    public static ISyncConflictResolver Create(
        ConflictResolutionStrategy strategy,
        Func<SyncConflictEventArgs, Task<ConflictResolutionStrategy?>>? userPrompt = null)
    {
        return strategy switch
        {
            ConflictResolutionStrategy.NewerWins => new NewerWinsConflictResolver(),
            ConflictResolutionStrategy.SourceWins => new SourceWinsConflictResolver(),
            ConflictResolutionStrategy.TargetWins => new TargetWinsConflictResolver(),
            ConflictResolutionStrategy.Skip => new SkipConflictResolver(),
            ConflictResolutionStrategy.AskUser => new AskUserConflictResolver(userPrompt ?? throw new ArgumentNullException(nameof(userPrompt), "Для стратегии AskUser требуется функция userPrompt")),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Неподдерживаемая стратегия разрешения конфликтов"),
        };
    }
}
