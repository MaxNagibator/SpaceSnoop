namespace SpaceSnoop.Core.Domain;

/// <summary>
/// Метаданные синхронизации для объектов SpaceBase.
/// </summary>
public class SyncMetadata
{
    /// <summary>
    /// Создает новый экземпляр SyncMetadata.
    /// </summary>
    public SyncMetadata()
    {
        IsModifiedSinceLastSync = false;
    }

    /// <summary>
    /// Время последней синхронизации.
    /// </summary>
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// Причина конфликта синхронизации, если таковой имеется.
    /// </summary>
    public string? SyncConflictReason { get; set; }

    /// <summary>
    /// Тип последней выполненной операции синхронизации.
    /// </summary>
    public SyncOperationType? LastSyncOperation { get; set; }

    /// <summary>
    /// Сообщение об ошибке, если синхронизация не удалась.
    /// </summary>
    public string? SyncErrorMessage { get; set; }

    /// <summary>
    /// Указывает, был ли этот элемент изменен с момента последней синхронизации.
    /// </summary>
    public bool IsModifiedSinceLastSync { get; set; }

    /// <summary>
    /// Путь к соответствующему элементу в целевой директории.
    /// </summary>
    public string? TargetPath { get; set; }

    /// <summary>
    /// Сбрасывает метаданные синхронизации к начальному состоянию.
    /// </summary>
    public void Reset()
    {
        LastSyncTime = null;
        SyncConflictReason = null;
        LastSyncOperation = null;
        SyncErrorMessage = null;
        IsModifiedSinceLastSync = false;
        TargetPath = null;
    }

    /// <summary>
    /// Отмечает успешную синхронизацию.
    /// </summary>
    /// <param name="operationType">Тип выполненной операции.</param>
    /// <param name="targetPath">Путь к целевому элементу.</param>
    public void MarkSyncSuccess(SyncOperationType operationType, string targetPath)
    {
        LastSyncTime = DateTime.Now;
        LastSyncOperation = operationType;
        TargetPath = targetPath;
        SyncErrorMessage = null;
        SyncConflictReason = null;
        IsModifiedSinceLastSync = false;
    }

    /// <summary>
    /// Отмечает неудачную синхронизацию.
    /// </summary>
    /// <param name="errorMessage">Сообщение об ошибке.</param>
    public void MarkSyncFailure(string errorMessage)
    {
        SyncErrorMessage = errorMessage;
        LastSyncTime = DateTime.Now;
    }

    /// <summary>
    /// Отмечает конфликт синхронизации.
    /// </summary>
    /// <param name="conflictReason">Причина конфликта.</param>
    public void MarkSyncConflict(string conflictReason)
    {
        SyncConflictReason = conflictReason;
        LastSyncTime = DateTime.Now;
    }
}
