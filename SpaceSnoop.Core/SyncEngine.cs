using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class SyncEngine(ILogger<SyncEngine> logger, bool showDeleteUi = true)
{
    public SyncReport Execute(ComparisonResult comparisonResult, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var report = new SyncReport();
        ExecuteRecursive(comparisonResult.Root, comparisonResult.LeftPath, comparisonResult.RightPath, report, progress, cancel);
        return report;
    }

    public void Verify(SyncReport report, string leftBase, string rightBase, CancellationToken cancel)
    {
        foreach (var item in report.Applied)
        {
            if (cancel.IsCancellationRequested)
            {
                return;
            }

            var leftPath = Path.Combine(leftBase, item.RelativePath);
            var rightPath = Path.Combine(rightBase, item.RelativePath);

            var reason = item.Action switch
            {
                SyncAction.CopyToRight => VerifyCopy(leftPath, rightPath),
                SyncAction.CopyToLeft => VerifyCopy(rightPath, leftPath),
                SyncAction.DeleteLeft => VerifyGone(leftPath),
                SyncAction.DeleteRight => VerifyGone(rightPath),
                _ => null,
            };

            if (reason is not null)
            {
                report.Mismatches.Add(new(item.RelativePath, item.Action, reason));
            }
        }
    }

    private static string? VerifyCopy(string source, string destination)
    {
        try
        {
            if (Directory.Exists(destination))
            {
                return null;
            }

            var destinationFile = new FileInfo(destination);

            if (!destinationFile.Exists)
            {
                return "приёмник отсутствует после копирования";
            }

            var sourceFile = new FileInfo(source);

            return sourceFile.Exists && !DirectoryComparer.FilesIdentical(sourceFile, destinationFile)
                ? "содержимое расходится после копирования"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return $"не удалось проверить: {ex.Message}";
        }
    }

    private static string? VerifyGone(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path)
                ? "не удалён после синхронизации"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return $"не удалось проверить: {ex.Message}";
        }
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

            report.AddApplied(dir.Action, dir.RelativePath, 0);
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
            progress?.Report(new(report.SuccessCount + report.Errors.Count, dir.RelativePath, report.CopiedBytes));
            return;
        }

        if (dir.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            ApplyDirectoryAction(dir, leftBase, rightBase, report);
            progress?.Report(new(report.SuccessCount + report.Errors.Count, dir.RelativePath, report.CopiedBytes));
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

                report.AddApplied(file.Action, file.RelativePath, AppliedBytes(file));
                logger.SyncFileApplied(file.Action, file.RelativePath);
            }
            catch (Exception ex)
            {
                report.Errors.Add(new(file.RelativePath, file.Action, ex.Message));
                logger.SyncFileFailed(ex, file.Action, file.RelativePath);
            }

            progress?.Report(new(report.SuccessCount + report.Errors.Count, file.RelativePath, report.CopiedBytes));
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
    public long CopiedBytes { get; private set; }
    public long DeletedBytes { get; private set; }
    public List<SyncApplied> Applied { get; } = [];
    public List<SyncError> Errors { get; } = [];
    public List<SyncMismatch> Mismatches { get; } = [];

    public void AddApplied(SyncAction action, string relativePath, long bytes)
    {
        Applied.Add(new(action, relativePath, bytes));

        if (action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            DeletedBytes += bytes;
        }
        else
        {
            CopiedBytes += bytes;
        }
    }

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

        foreach (var mismatch in Mismatches)
        {
            writer.WriteLine($"  РАСХОЖДЕНИЕ: {mismatch.RelativePath} ({mismatch.Action}): {mismatch.Reason}");
        }
    }
}

public sealed record SyncApplied(SyncAction Action, string RelativePath, long Bytes);

public sealed record SyncError(string RelativePath, SyncAction Action, string Message);

public sealed record SyncMismatch(string RelativePath, SyncAction Action, string Reason);
