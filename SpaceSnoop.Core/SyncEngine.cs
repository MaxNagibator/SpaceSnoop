using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace SpaceSnoop.Core;

public sealed class SyncEngine(ILogger<SyncEngine> logger, bool showDeleteUi = true)
{
    public SyncReport Execute(ComparisonResult comparisonResult, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var report = new SyncReport();
        ExecuteRecursive(comparisonResult.Root, comparisonResult.LeftPath, comparisonResult.RightPath, report, progress, cancel);
        return report;
    }

    private static long AppliedBytes(FileComparison file)
    {
        return file.Action switch
        {
            SyncAction.CopyToRight or SyncAction.DeleteLeft => file.LeftSize ?? file.RightSize ?? 0,
            SyncAction.CopyToLeft or SyncAction.DeleteRight => file.RightSize ?? file.LeftSize ?? 0,
            _ => 0,
        };
    }

    private static void CopyAtomic(string source, string destination)
    {
        var temp = destination + ".sstmp";

        try
        {
            File.Copy(source, temp, true);
            File.Move(temp, destination, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var attributes = File.GetAttributes(path);

        if ((attributes & FileAttributes.ReadOnly) != 0)
        {
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
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

    private void ExecuteFileAction(FileComparison file, string leftBase, string rightBase)
    {
        var leftPath = Path.Combine(leftBase, file.RelativePath);
        var rightPath = Path.Combine(rightBase, file.RelativePath);

        switch (file.Action)
        {
            case SyncAction.CopyToRight:
                EnsureDirectoryExists(rightPath);
                ClearReadOnly(rightPath);
                CopyAtomic(leftPath, rightPath);
                break;

            case SyncAction.CopyToLeft:
                EnsureDirectoryExists(leftPath);
                ClearReadOnly(leftPath);
                CopyAtomic(rightPath, leftPath);
                break;

            case SyncAction.DeleteLeft:
                if (File.Exists(leftPath))
                {
                    ClearReadOnly(leftPath);
                    RecycleFile(leftPath);
                }

                break;

            case SyncAction.DeleteRight:
                if (File.Exists(rightPath))
                {
                    ClearReadOnly(rightPath);
                    RecycleFile(rightPath);
                }

                break;
        }
    }

    private void RecycleFile(string path)
    {
        if (showDeleteUi)
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            RecycleBin.DeleteSilent(path);
        }
    }

    private void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        if (showDeleteUi)
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            RecycleBin.DeleteSilent(path);
        }
    }

    private void ApplyDirectoryAction(DirectoryComparison dir, string leftBase, string rightBase, SyncReport report)
    {
        try
        {
            switch (dir.Action)
            {
                case SyncAction.CopyToRight:
                    Directory.CreateDirectory(Path.Combine(rightBase, dir.RelativePath));
                    report.CopiedCount++;
                    break;

                case SyncAction.CopyToLeft:
                    Directory.CreateDirectory(Path.Combine(leftBase, dir.RelativePath));
                    report.CopiedCount++;
                    break;

                case SyncAction.DeleteLeft:
                    DeleteDirectory(Path.Combine(leftBase, dir.RelativePath));
                    report.DeletedCount++;
                    break;

                case SyncAction.DeleteRight:
                    DeleteDirectory(Path.Combine(rightBase, dir.RelativePath));
                    report.DeletedCount++;
                    break;
            }

            report.Applied.Add(new(dir.Action, dir.RelativePath, 0));
            logger.SyncFileApplied(dir.Action, dir.RelativePath);
        }
        catch (Exception ex)
        {
            report.Errors.Add(new(dir.RelativePath, dir.Action, ex.Message));
            logger.SyncFileFailed(ex, dir.Action, dir.RelativePath);
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
        cancel.ThrowIfCancellationRequested();

        if (dir.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            ApplyDirectoryAction(dir, leftBase, rightBase, report);
            progress?.Report(new(report.SuccessCount + report.Errors.Count, dir.RelativePath));
            return;
        }

        if (dir.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            ApplyDirectoryAction(dir, leftBase, rightBase, report);
            progress?.Report(new(report.SuccessCount + report.Errors.Count, dir.RelativePath));
        }

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

                report.Applied.Add(new(file.Action, file.RelativePath, AppliedBytes(file)));
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
    public List<SyncApplied> Applied { get; } = [];
    public List<SyncError> Errors { get; } = [];

    public void WriteDetails(TextWriter writer)
    {
        foreach (var item in Applied)
        {
            var size = item.Bytes > 0 ? $" ({SizeFormatter.Format(item.Bytes)})" : string.Empty;
            writer.WriteLine($"  {item.Action} «{item.RelativePath}»{size}");
        }

        foreach (var error in Errors)
        {
            writer.WriteLine($"  ОШИБКА: {error.RelativePath} ({error.Action}): {error.Message}");
        }
    }
}

public sealed record SyncApplied(SyncAction Action, string RelativePath, long Bytes);

public sealed record SyncError(string RelativePath, SyncAction Action, string Message);
