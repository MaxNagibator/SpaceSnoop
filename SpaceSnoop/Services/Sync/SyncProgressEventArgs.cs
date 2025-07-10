namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Аргументы события прогресса синхронизации.
/// </summary>
/// <remarks>
/// Создает новый экземпляр аргументов события прогресса.
/// </remarks>
/// <param name="currentOperation">Текущая выполняемая операция.</param>
/// <param name="completedOperations">Количество завершенных операций.</param>
/// <param name="totalOperations">Общее количество операций.</param>
/// <param name="processedBytes">Количество обработанных байт.</param>
/// <param name="totalBytes">Общее количество байт для обработки.</param>
/// <param name="elapsedTime">Прошедшее время.</param>
/// <param name="estimatedTimeRemaining">Оценочное время до завершения.</param>
public class SyncProgressEventArgs(
    SyncOperation? currentOperation,
    int completedOperations,
    int totalOperations,
    long processedBytes,
    long totalBytes,
    TimeSpan elapsedTime,
    TimeSpan? estimatedTimeRemaining = null) : EventArgs
{
    /// <summary>
    /// Текущая выполняемая операция.
    /// </summary>
    public SyncOperation? CurrentOperation { get; } = currentOperation;

    /// <summary>
    /// Количество завершенных операций.
    /// </summary>
    public int CompletedOperations { get; } = completedOperations;

    /// <summary>
    /// Общее количество операций.
    /// </summary>
    public int TotalOperations { get; } = totalOperations;

    /// <summary>
    /// Количество обработанных байт.
    /// </summary>
    public long ProcessedBytes { get; } = processedBytes;

    /// <summary>
    /// Общее количество байт для обработки.
    /// </summary>
    public long TotalBytes { get; } = totalBytes;

    /// <summary>
    /// Прошедшее время с начала синхронизации.
    /// </summary>
    public TimeSpan ElapsedTime { get; } = elapsedTime;

    /// <summary>
    /// Оценочное время до завершения синхронизации.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; } = estimatedTimeRemaining;

    /// <summary>
    /// Процент завершения по количеству операций.
    /// </summary>
    public double OperationProgress => TotalOperations > 0 ? (double)CompletedOperations / TotalOperations * 100 : 0;

    /// <summary>
    /// Процент завершения по количеству байт.
    /// </summary>
    public double ByteProgress => TotalBytes > 0 ? (double)ProcessedBytes / TotalBytes * 100 : 0;
}
