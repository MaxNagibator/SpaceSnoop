using SpaceSnoop.Services.Sync.Utilities;

namespace SpaceSnoop.Services.Sync.Executors;

/// <summary>
/// Исполнитель операций обновления файлов.
/// </summary>
public class FileUpdateExecutor : ISyncOperationExecutor
{
    /// <inheritdoc />
    public bool CanExecute(SyncOperation operation)
    {
        return operation is
        {
            OperationType: SyncOperationType.Update,
            IsDirectoryOperation: false,
        };
    }

    /// <inheritdoc />
    public async Task<bool> ExecuteAsync(SyncOperation operation, SyncSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceFile = new FileInfo(operation.SourcePath);
            var targetFile = new FileInfo(operation.TargetPath);

            if (targetFile.DirectoryName != null
                && FileOperationUtilities.EnsureDirectoryExists(targetFile.DirectoryName) == false)
            {
                return false;
            }

            if (settings.CreateBackups && targetFile.Exists)
            {
                FileOperationUtilities.CreateBackup(operation.TargetPath, settings.BackupSuffix);
            }

            if (settings.DryRun)
            {
                return true;
            }

            await FileOperationUtilities.CopyFileAsync(sourceFile.FullName, targetFile.FullName, cancellationToken);

            if (settings.PreserveTimestamps)
            {
                FileOperationUtilities.CopyTimestamps(sourceFile, targetFile);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
