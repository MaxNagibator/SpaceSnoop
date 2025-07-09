using SpaceSnoop.Services.Sync.Utilities;

namespace SpaceSnoop.Services.Sync.Executors;

/// <summary>
/// Исполнитель операций создания директорий.
/// </summary>
public class DirectoryCreateExecutor : ISyncOperationExecutor
{
    /// <inheritdoc />
    public bool CanExecute(SyncOperation operation)
    {
        return operation is
        {
            OperationType: SyncOperationType.CreateDirectory,
            IsDirectoryOperation: true,
        };
    }

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(SyncOperation operation, SyncSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetDirectory = new DirectoryInfo(operation.TargetPath);

            if (targetDirectory.Exists || settings.DryRun)
            {
                return Task.FromResult(true);
            }

            targetDirectory.Create();

            if (settings.PreserveTimestamps==false)
            {
                return Task.FromResult(true);
            }

            var sourceDirectory = new DirectoryInfo(operation.SourcePath);

            if (sourceDirectory.Exists)
            {
                FileOperationUtilities.CopyTimestamps(sourceDirectory, targetDirectory);
            }

            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
