using System.Diagnostics;

namespace SpaceSnoop.Services.Sync;

/// <summary>
/// Контекст выполнения синхронизации, содержащий состояние и статистику операций.
/// </summary>
public class SyncContext
{
    /// <summary>
    /// Время начала синхронизации.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Время завершения синхронизации.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Секундомер для измерения времени выполнения синхронизации.
    /// </summary>
    public Stopwatch Stopwatch { get; set; } = null!;

    /// <summary>
    /// Список успешно завершенных операций синхронизации.
    /// </summary>
    public List<SyncOperation> CompletedOperations { get; } = [];

    /// <summary>
    /// Список неудачных операций с описанием ошибок.
    /// </summary>
    public List<(SyncOperation Operation, string ErrorMessage)> FailedOperationDetails { get; } = [];

    /// <summary>
    /// Количество успешно выполненных операций (исключая пропущенные).
    /// </summary>
    public int SuccessfulOperations { get; set; }

    /// <summary>
    /// Количество неудачных операций.
    /// </summary>
    public int FailedOperations { get; set; }

    /// <summary>
    /// Количество пропущенных операций.
    /// </summary>
    public int SkippedOperations { get; set; }

    /// <summary>
    /// Общее количество синхронизированных байт.
    /// </summary>
    public long TotalSyncedBytes { get; set; }

    /// <summary>
    /// Общее сообщение об ошибке, если синхронизация завершилась с критической ошибкой.
    /// </summary>
    public string? GeneralErrorMessage { get; set; }
}
