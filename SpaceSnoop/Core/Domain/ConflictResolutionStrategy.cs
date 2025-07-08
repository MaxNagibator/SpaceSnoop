namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Стратегии разрешения конфликтов при синхронизации.
/// </summary>
public enum ConflictResolutionStrategy
{
    /// <summary>
    /// Побеждает файл с более поздней датой изменения.
    /// </summary>
    NewerWins,

    /// <summary>
    /// Всегда побеждает исходный файл.
    /// </summary>
    SourceWins,

    /// <summary>
    /// Всегда побеждает целевой файл.
    /// </summary>
    TargetWins,

    /// <summary>
    /// Пропускать конфликтующие файлы.
    /// </summary>
    Skip,

    /// <summary>
    /// Запрашивать у пользователя решение для каждого конфликта.
    /// </summary>
    AskUser,
}
