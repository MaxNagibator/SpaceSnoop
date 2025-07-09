namespace SpaceSnoop.Services.Sync.Executors;

/// <summary>
/// Интерфейс для выполнения операций синхронизации.
/// </summary>
public interface ISyncOperationExecutor
{
    /// <summary>
    /// Проверяет, может ли данный исполнитель выполнить указанную операцию.
    /// </summary>
    /// <param name="operation">Операция для проверки.</param>
    /// <returns>True, если операция может быть выполнена, иначе false.</returns>
    bool CanExecute(SyncOperation operation);

    /// <summary>
    /// Выполняет операцию синхронизации.
    /// </summary>
    /// <param name="operation">Операция для выполнения.</param>
    /// <param name="settings">Настройки синхронизации.</param>
    /// <param name="cancellationToken">Токен отмены операции.</param>
    /// <returns>True, если операция выполнена успешно, иначе false.</returns>
    Task<bool> ExecuteAsync(SyncOperation operation, SyncSettings settings, CancellationToken cancellationToken = default);
}