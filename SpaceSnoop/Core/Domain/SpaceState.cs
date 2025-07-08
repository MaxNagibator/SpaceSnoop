namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Состояния элементов файловой системы в SpaceSnoop.
/// </summary>
public enum SpaceState : byte
{
    /// <summary>
    /// Начальное состояние.
    /// </summary>
    None = 0,

    /// <summary>
    /// Элемент добавлен в дерево.
    /// </summary>
    Added = 1,

    /// <summary>
    /// Элемент помечен для удаления.
    /// </summary>
    Deleted = 2,

    /// <summary>
    /// Произошла ошибка при обработке элемента.
    /// </summary>
    Error = 3,

    // Состояния синхронизации

    /// <summary>
    /// Элемент ожидает синхронизации.
    /// </summary>
    SyncPending = 4,

    /// <summary>
    /// Элемент в процессе синхронизации.
    /// </summary>
    SyncInProgress = 5,

    /// <summary>
    /// Синхронизация элемента завершена успешно.
    /// </summary>
    SyncCompleted = 6,

    /// <summary>
    /// Произошла ошибка при синхронизации элемента.
    /// </summary>
    SyncFailed = 7,

    /// <summary>
    /// Обнаружен конфликт при синхронизации элемента.
    /// </summary>
    SyncConflict = 8,
}
