namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Типы операций синхронизации.
/// </summary>
public enum SyncOperationType
{
    /// <summary>
    /// Копирование файла или директории.
    /// </summary>
    Copy,

    /// <summary>
    /// Обновление существующего файла или директории.
    /// </summary>
    Update,

    /// <summary>
    /// Удаление файла или директории.
    /// </summary>
    Delete,

    /// <summary>
    /// Создание новой директории.
    /// </summary>
    CreateDirectory,

    /// <summary>
    /// Пропуск элемента (нет необходимости в синхронизации).
    /// </summary>
    Skip,
}
