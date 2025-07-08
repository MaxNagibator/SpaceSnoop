namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Представляет отдельную операцию синхронизации.
/// </summary>
public class SyncOperation
{
    /// <summary>
    /// Создает новую операцию синхронизации.
    /// </summary>
    /// <param name="operationType">Тип операции.</param>
    /// <param name="sourcePath">Путь к источнику.</param>
    /// <param name="targetPath">Путь к цели.</param>
    /// <param name="isDirectoryOperation">Является ли операция операцией с директорией.</param>
    /// <param name="fileSize">Размер файла (для файловых операций).</param>
    /// <param name="description">Описание операции.</param>
    public SyncOperation(
        SyncOperationType operationType,
        string sourcePath,
        string targetPath,
        bool isDirectoryOperation = false,
        long? fileSize = null,
        string? description = null)
    {
        OperationType = operationType;
        SourcePath = sourcePath ?? throw new ArgumentNullException(nameof(sourcePath));
        TargetPath = targetPath ?? throw new ArgumentNullException(nameof(targetPath));
        IsDirectoryOperation = isDirectoryOperation;
        FileSize = fileSize;
        Description = description;
        CreatedAt = DateTime.Now;
        Priority = GetPriorityForOperation(operationType);
    }

    /// <summary>
    /// Тип операции синхронизации.
    /// </summary>
    public SyncOperationType OperationType { get; }

    /// <summary>
    /// Путь к исходному файлу или директории.
    /// </summary>
    public string SourcePath { get; }

    /// <summary>
    /// Путь к целевому файлу или директории.
    /// </summary>
    public string TargetPath { get; }

    /// <summary>
    /// Время создания операции.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Размер файла для операций с файлами (в байтах).
    /// </summary>
    public long? FileSize { get; }

    // TODO: Жиденько, но может походит
    /// <summary>
    /// Указывает, является ли операция операцией с директорией.
    /// </summary>
    public bool IsDirectoryOperation { get; }

    /// <summary>
    /// Приоритет операции (меньшее значение = выше приоритет).
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Дополнительная информация об операции.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Возвращает строковое представление операции.
    /// </summary>
    public override string ToString()
    {
        var operationText = OperationType switch
        {
            SyncOperationType.Copy => "Копирование",
            SyncOperationType.Update => "Обновление",
            SyncOperationType.Delete => "Удаление",
            SyncOperationType.CreateDirectory => "Создание директории",
            SyncOperationType.Skip => "Пропуск",
            _ => "Неизвестная операция",
        };

        var typeText = IsDirectoryOperation ? "директории" : "файла";
        var sizeText = FileSize.HasValue ? $" ({SizeFormatter.Format(FileSize.Value)})" : "";

        return $"{operationText} {typeText}{sizeText}: {SourcePath} → {TargetPath}";
    }

    /// <summary>
    /// Сравнивает операции по приоритету.
    /// </summary>
    /// <param name="other">Другая операция для сравнения.</param>
    /// <returns>Результат сравнения.</returns>
    public int CompareTo(SyncOperation? other)
    {
        if (other == null)
        {
            return 1;
        }

        return Priority.CompareTo(other.Priority);
    }

    /// <summary>
    /// Определяет приоритет для типа операции.
    /// </summary>
    /// <param name="operationType">Тип операции.</param>
    /// <returns>Приоритет операции.</returns>
    private static int GetPriorityForOperation(SyncOperationType operationType)
    {
        return operationType switch
        {
            SyncOperationType.CreateDirectory => 1,
            SyncOperationType.Copy => 2,
            SyncOperationType.Update => 3,
            SyncOperationType.Delete => 4,
            SyncOperationType.Skip => 5,
            _ => 10,
        };
    }
}
