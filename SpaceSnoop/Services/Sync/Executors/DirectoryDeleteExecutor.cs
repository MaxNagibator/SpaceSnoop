namespace SpaceSnoop.Services.Sync.Executors;

/// <summary>
/// Исполнитель операций удаления директорий.
/// </summary>
public class DirectoryDeleteExecutor : ISyncOperationExecutor
{
    /// <inheritdoc />
    public bool CanExecute(SyncOperation operation)
    {
        return operation is
        {
            OperationType: SyncOperationType.Delete,
            IsDirectoryOperation: true,
        };
    }

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(SyncOperation operation, SyncSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetDirectory = new DirectoryInfo(operation.TargetPath);

            if (targetDirectory.Exists == false)
            {
                return Task.FromResult(true);
            }

            if (settings.DryRun == false)
            {
                targetDirectory.Delete(true);
            }

            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
