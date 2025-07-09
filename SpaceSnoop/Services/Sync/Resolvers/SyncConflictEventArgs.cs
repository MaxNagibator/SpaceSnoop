namespace SpaceSnoop.Services.Sync.Resolvers;

/// <summary>
/// Аргументы события запроса разрешения конфликта.
/// </summary>
public class SyncConflictEventArgs : EventArgs
{
    /// <summary>
    /// Создает новый экземпляр аргументов события конфликта.
    /// </summary>
    /// <param name="operation">Операция, вызвавшая конфликт.</param>
    /// <param name="conflictReason">Причина конфликта.</param>
    /// <param name="sourceInfo">Информация об исходном файле.</param>
    /// <param name="targetInfo">Информация о целевом файле.</param>
    public SyncConflictEventArgs(
        SyncOperation operation,
        string conflictReason,
        FileInfo? sourceInfo = null,
        FileInfo? targetInfo = null)
    {
        Operation = operation;
        ConflictReason = conflictReason;
        SourceInfo = sourceInfo;
        TargetInfo = targetInfo;
    }

    /// <summary>
    /// Операция, вызвавшая конфликт.
    /// </summary>
    public SyncOperation Operation { get; }

    /// <summary>
    /// Причина конфликта.
    /// </summary>
    public string ConflictReason { get; }

    /// <summary>
    /// Информация об исходном файле.
    /// </summary>
    public FileInfo? SourceInfo { get; }

    /// <summary>
    /// Информация о целевом файле.
    /// </summary>
    public FileInfo? TargetInfo { get; }

    /// <summary>
    /// Решение пользователя по конфликту.
    /// </summary>
    public ConflictResolutionStrategy? Resolution { get; set; }

    /// <summary>
    /// Применить решение ко всем последующим конфликтам.
    /// </summary>
    public bool ApplyToAll { get; set; }
}
