namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Действия при разрешении конфликта.
/// </summary>
public enum ConflictAction
{
    /// <summary>
    /// Использовать исходный файл.
    /// </summary>
    UseSource,

    /// <summary>
    /// Использовать целевой файл.
    /// </summary>
    UseTarget,

    /// <summary>
    /// Пропустить операцию.
    /// </summary>
    Skip,

    /// <summary>
    /// Запросить решение у пользователя.
    /// </summary>
    AskUser,
}
