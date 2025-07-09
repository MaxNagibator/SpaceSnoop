namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Результат разрешения конфликта.
/// </summary>
public class ConflictResolutionResult
{
    /// <summary>
    /// Создает новый результат разрешения конфликта.
    /// </summary>
    /// <param name="action">Действие, которое нужно выполнить.</param>
    /// <param name="applyToAll">Применить решение ко всем последующим конфликтам.</param>
    /// <param name="reason">Причина принятого решения.</param>
    public ConflictResolutionResult(ConflictAction action, bool applyToAll = false, string? reason = null)
    {
        Action = action;
        ApplyToAll = applyToAll;
        Reason = reason;
    }

    /// <summary>
    /// Действие, которое нужно выполнить.
    /// </summary>
    public ConflictAction Action { get; }

    /// <summary>
    /// Применить решение ко всем последующим конфликтам.
    /// </summary>
    public bool ApplyToAll { get; }

    /// <summary>
    /// Причина принятого решения.
    /// </summary>
    public string? Reason { get; }
}
