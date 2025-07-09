namespace SpaceSnoop.Services.Sync.Executors;

/// <summary>
/// Исполнитель операций удаления файлов.
/// </summary>
public class FileDeleteExecutor : ISyncOperationExecutor
{
    /// <inheritdoc />
    public bool CanExecute(SyncOperation operation)
    {
        return operation is
        {
            OperationType: SyncOperationType.Delete,
            IsDirectoryOperation: false,
        };
    }

    /// <inheritdoc />
    public Task<bool> ExecuteAsync(SyncOperation operation, SyncSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var targetFile = new FileInfo(operation.TargetPath);

            if (targetFile.Exists == false)
            {
                return Task.FromResult(true);
            }

            if (settings.CreateBackups)
            {
                var backupPath = operation.TargetPath + settings.BackupSuffix;
                File.Copy(operation.TargetPath, backupPath, true);
            }

            if (settings.DryRun == false)
            {
                targetFile.Delete();
            }

            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }
}
