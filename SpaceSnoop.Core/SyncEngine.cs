using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace SpaceSnoop.Core;

public sealed class SyncEngine(ILogger<SyncEngine> logger)
{
    public SyncReport Execute(ComparisonResult comparisonResult, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var report = new SyncReport();
        ExecuteRecursive(comparisonResult.Root, comparisonResult.LeftPath, comparisonResult.RightPath, report, progress, cancel);
        return report;
    }

    private static void ExecuteFileAction(FileComparison file, string leftBase, string rightBase)
    {
        var leftPath = Path.Combine(leftBase, file.RelativePath);
        var rightPath = Path.Combine(rightBase, file.RelativePath);

        switch (file.Action)
        {
            case SyncAction.CopyToRight:
                EnsureDirectoryExists(rightPath);
                File.Copy(leftPath, rightPath, true);
                break;

            case SyncAction.CopyToLeft:
                EnsureDirectoryExists(leftPath);
                File.Copy(rightPath, leftPath, true);
                break;

            case SyncAction.DeleteLeft:
                if (File.Exists(leftPath))
                {
                    FileSystem.DeleteFile(leftPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }

                break;

            case SyncAction.DeleteRight:
                if (File.Exists(rightPath))
                {
                    FileSystem.DeleteFile(rightPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                }

                break;
        }
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }
    }

    private void ExecuteRecursive(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        SyncReport report,
        IProgress<OperationProgress>? progress,
        CancellationToken cancel)
    {
        foreach (var file in dir.Files)
        {
            cancel.ThrowIfCancellationRequested();

            if (file.Action is SyncAction.None or SyncAction.Skip)
            {
                continue;
            }

            try
            {
                ExecuteFileAction(file, leftBase, rightBase);

                if (file.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
                {
                    report.DeletedCount++;
                }
                else
                {
                    report.CopiedCount++;
                }

                logger.SyncFileApplied(file.Action, file.RelativePath);
            }
            catch (Exception ex)
            {
                report.Errors.Add(new(file.RelativePath, file.Action, ex.Message));
                logger.SyncFileFailed(ex, file.Action, file.RelativePath);
            }

            progress?.Report(new(report.SuccessCount + report.Errors.Count, file.RelativePath));
        }

        foreach (var sub in dir.SubDirectories)
        {
            ExecuteRecursive(sub, leftBase, rightBase, report, progress, cancel);
        }
    }
}

public sealed class SyncReport
{
    public int CopiedCount { get; set; }
    public int DeletedCount { get; set; }
    public int SuccessCount => CopiedCount + DeletedCount;
    public List<SyncError> Errors { get; } = [];
}

public sealed record SyncError(string RelativePath, SyncAction Action, string Message);
