namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Результат выполнения операций синхронизации.
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Создает новый результат синхронизации.
    /// </summary>
    /// <param name="totalOperations">Общее количество операций.</param>
    /// <param name="successfulOperations">Количество успешных операций.</param>
    /// <param name="failedOperations">Количество неудачных операций.</param>
    /// <param name="skippedOperations">Количество пропущенных операций.</param>
    /// <param name="completedOperations">Список выполненных операций.</param>
    /// <param name="failedOperationDetails">Детали неудачных операций.</param>
    /// <param name="elapsedTime">Время выполнения.</param>
    /// <param name="totalSyncedBytes">Общий размер синхронизированных данных.</param>
    /// <param name="startTime">Время начала.</param>
    /// <param name="endTime">Время завершения.</param>
    /// <param name="generalErrorMessage">Общее сообщение об ошибке.</param>
    public SyncResult(
        int totalOperations,
        int successfulOperations,
        int failedOperations,
        int skippedOperations,
        IReadOnlyList<SyncOperation> completedOperations,
        IReadOnlyList<(SyncOperation Operation, string ErrorMessage)> failedOperationDetails,
        TimeSpan elapsedTime,
        long totalSyncedBytes,
        DateTime startTime,
        DateTime endTime,
        string? generalErrorMessage = null)
    {
        TotalOperations = totalOperations;
        SuccessfulOperations = successfulOperations;
        FailedOperations = failedOperations;
        SkippedOperations = skippedOperations;
        CompletedOperations = completedOperations ?? throw new ArgumentNullException(nameof(completedOperations));
        FailedOperationDetails = failedOperationDetails ?? throw new ArgumentNullException(nameof(failedOperationDetails));
        ElapsedTime = elapsedTime;
        TotalSyncedBytes = totalSyncedBytes;
        StartTime = startTime;
        EndTime = endTime;
        GeneralErrorMessage = generalErrorMessage;

        IsSuccess = failedOperations == 0 && string.IsNullOrEmpty(generalErrorMessage);
    }

    /// <summary>
    /// Указывает, была ли синхронизация успешной.
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// Общее количество операций, которые должны были быть выполнены.
    /// </summary>
    public int TotalOperations { get; }

    /// <summary>
    /// Количество успешно выполненных операций.
    /// </summary>
    public int SuccessfulOperations { get; }

    /// <summary>
    /// Количество неудачных операций.
    /// </summary>
    public int FailedOperations { get; }

    /// <summary>
    /// Количество пропущенных операций.
    /// </summary>
    public int SkippedOperations { get; }

    /// <summary>
    /// Список выполненных операций.
    /// </summary>
    public IReadOnlyList<SyncOperation> CompletedOperations { get; }

    /// <summary>
    /// Список операций, которые не удалось выполнить.
    /// </summary>
    public IReadOnlyList<(SyncOperation Operation, string ErrorMessage)> FailedOperationDetails { get; }

    /// <summary>
    /// Общее время выполнения синхронизации.
    /// </summary>
    public TimeSpan ElapsedTime { get; }

    /// <summary>
    /// Общий размер синхронизированных данных в байтах.
    /// </summary>
    public long TotalSyncedBytes { get; }

    /// <summary>
    /// Сообщение об общей ошибке, если синхронизация полностью не удалась.
    /// </summary>
    public string? GeneralErrorMessage { get; }

    /// <summary>
    /// Время начала синхронизации.
    /// </summary>
    public DateTime StartTime { get; }

    /// <summary>
    /// Время завершения синхронизации.
    /// </summary>
    public DateTime EndTime { get; }

    /// <summary>
    /// Создает результат для полностью неудачной синхронизации.
    /// </summary>
    /// <param name="errorMessage">Сообщение об ошибке.</param>
    /// <param name="startTime">Время начала.</param>
    /// <param name="endTime">Время завершения.</param>
    /// <returns>Результат синхронизации с ошибкой.</returns>
    public static SyncResult CreateFailure(string errorMessage, DateTime startTime, DateTime endTime)
    {
        return new(0,
            0,
            0,
            0,
            [],
            [],
            endTime - startTime,
            0,
            startTime,
            endTime,
            errorMessage);
    }

    /// <summary>
    /// Возвращает строковое представление результата синхронизации.
    /// </summary>
    public override string ToString()
    {
        if (IsSuccess == false)
        {
            return $"Синхронизация не удалась: {GeneralErrorMessage}";
        }

        var successRate = TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;

        return $"Синхронизация завершена: {SuccessfulOperations}/{TotalOperations} операций успешно ({successRate:F1}%), "
               + $"время: {ElapsedTime.TotalSeconds:F2}с, данных: {SizeFormatter.Format(TotalSyncedBytes)}";
    }
}
