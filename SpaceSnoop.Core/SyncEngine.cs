using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using System.Diagnostics;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class SyncEngine(ILogger<SyncEngine> logger, bool showDeleteUi = true, bool recycleOverwritten = false)
{
    private const string TempSuffix = ".sstmp";
    private const int TempNameAttempts = 20;

    public SyncReport Execute(ComparisonResult comparisonResult, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var report = new SyncReport();
        var tracker = new TransferTracker(progress, report);
        ExecuteRecursive(comparisonResult.Root, comparisonResult.LeftPath, comparisonResult.RightPath, report, tracker, cancel, DeleteGate.Open);
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

        report.MarkVerified();
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

            if (!sourceFile.Exists)
            {
                return "источник исчез после копирования";
            }

            return DirectoryComparer.FilesIdentical(sourceFile, destinationFile)
                ? null
                : "содержимое расходится после копирования";
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

    private static string ReserveTempPath(string destination)
    {
        for (var index = 1; index <= TempNameAttempts; index++)
        {
            var temp = index == 1 ? destination + TempSuffix : $"{destination}.{index}{TempSuffix}";

            if (TryCreateExclusive(temp))
            {
                return temp;
            }
        }

        throw new IOException($"Не удалось занять временное имя рядом с «{destination}»");
    }

    private static bool TryCreateExclusive(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && (File.Exists(path) || Directory.Exists(path)))
        {
            return false;
        }
    }

    private void CopyAtomic(string source, string destination, TransferTracker tracker, CancellationToken cancel)
    {
        var temp = ReserveTempPath(destination);
        var moved = false;

        try
        {
            FileCopy.Copy(source, temp, tracker.Advance, cancel);
            cancel.ThrowIfCancellationRequested();
            RecyclePrevious(destination);
            File.Move(temp, destination, true);
            moved = true;
        }
        finally
        {
            if (!moved && File.Exists(temp))
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

    private static bool ShouldApply(FileComparison file)
    {
        return file.Action is not (SyncAction.None or SyncAction.Skip);
    }

    private bool Reject(SyncAction action, string relativePath, SyncReport report, bool alreadyReported)
    {
        if (alreadyReported)
        {
            return true;
        }

        report.Errors.Add(new(relativePath, action, "удаление отклонено: сторона-источник обойдена не полностью; остальные отказы в этой ветке не перечисляются"));
        logger.SyncDeleteBlocked(action, relativePath);

        return true;
    }

    private void RejectTypeConflict(FileComparison file, SyncReport report)
    {
        var reason = file.TypeConflict switch
        {
            FileTypeConflict.LeftLinkRightObject => "слева ссылка, справа настоящий объект – разрешается вручную",
            FileTypeConflict.RightLinkLeftObject => "справа ссылка, слева настоящий объект – разрешается вручную",
            FileTypeConflict.LeftFileRightDirectory => "слева файл, справа каталог – разрешается вручную",
            _ => "справа файл, слева каталог – разрешается вручную",
        };

        report.Errors.Add(new(file.RelativePath, file.Action, $"действие отклонено: {reason}"));
        logger.SyncTypeConflictBlocked(file.Action, file.RelativePath, file.TypeConflict);
    }

    private readonly record struct DeleteGate(bool Left, bool Right, bool Reported)
    {
        public static DeleteGate Open { get; } = new(false, false, false);

        public DeleteGate Inherit(DirectoryComparison dir)
        {
            return this with
            {
                Left = Left || dir.RightIncomplete || dir.DeleteLeftBlocked,
                Right = Right || dir.LeftIncomplete || dir.DeleteRightBlocked,
            };
        }

        public bool Blocks(SyncAction action, bool leftBlocked = false, bool rightBlocked = false)
        {
            return action switch
            {
                SyncAction.DeleteLeft => Left || leftBlocked,
                SyncAction.DeleteRight => Right || rightBlocked,
                _ => false,
            };
        }
    }

    private void ExecuteFileAction(FileComparison file, string leftBase, string rightBase, TransferTracker tracker, CancellationToken cancel)
    {
        var leftPath = Path.Combine(leftBase, file.RelativePath);
        var rightPath = Path.Combine(rightBase, file.RelativePath);

        switch (file.Action)
        {
            case SyncAction.CopyToRight:
                CopyFile(leftPath, rightPath, tracker, cancel);
                break;

            case SyncAction.CopyToLeft:
                CopyFile(rightPath, leftPath, tracker, cancel);
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

    private void CopyFile(string source, string destination, TransferTracker tracker, CancellationToken cancel)
    {
        if (Directory.Exists(destination))
        {
            throw new IOException($"Приёмник занят каталогом с тем же именем: {destination}");
        }

        EnsureDirectoryExists(destination);
        ClearReadOnly(destination);
        CopyAtomic(source, destination, tracker, cancel);
    }

    private void RecyclePrevious(string destination)
    {
        if (!recycleOverwritten || !File.Exists(destination))
        {
            return;
        }

        RecycleFile(destination);
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
        TransferTracker tracker,
        CancellationToken cancel,
        DeleteGate gate)
    {
        cancel.ThrowIfCancellationRequested();

        gate = gate.Inherit(dir);

        if (dir.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            if (gate.Blocks(dir.Action))
            {
                gate = gate with { Reported = Reject(dir.Action, dir.RelativePath, report, gate.Reported) };
            }
            else
            {
                ApplyDirectoryActionAndReport(dir, leftBase, rightBase, report, tracker);
                return;
            }
        }
        else if (dir.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            ApplyDirectoryActionAndReport(dir, leftBase, rightBase, report, tracker);
        }

        foreach (var file in dir.Files)
        {
            cancel.ThrowIfCancellationRequested();

            if (!ShouldApply(file))
            {
                continue;
            }

            if (gate.Blocks(file.Action, file.DeleteLeftBlocked, file.DeleteRightBlocked))
            {
                gate = gate with { Reported = Reject(file.Action, file.RelativePath, report, gate.Reported) };
                continue;
            }

            if (file.TypeConflict != FileTypeConflict.None)
            {
                RejectTypeConflict(file, report);
                continue;
            }

            tracker.BeginFile(file.RelativePath);
            ApplyFileActionAndReport(file, leftBase, rightBase, report, tracker, cancel);
            tracker.EndFile();
            tracker.Report(file.RelativePath);
        }

        foreach (var sub in dir.SubDirectories)
        {
            ExecuteRecursive(sub, leftBase, rightBase, report, tracker, cancel, gate);
        }
    }

    private void ApplyDirectoryActionAndReport(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        SyncReport report,
        TransferTracker tracker)
    {
        ApplyDirectoryAction(dir, leftBase, rightBase, report);
        tracker.Report(dir.RelativePath);
    }

    private void ApplyFileActionAndReport(
        FileComparison file,
        string leftBase,
        string rightBase,
        SyncReport report,
        TransferTracker tracker,
        CancellationToken cancel)
    {
        try
        {
            ExecuteFileAction(file, leftBase, rightBase, tracker, cancel);

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
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Errors.Add(new(file.RelativePath, file.Action, ex.Message));
            logger.SyncFileFailed(ex, file.Action, file.RelativePath);
        }
    }

    private sealed class TransferTracker(IProgress<OperationProgress>? progress, SyncReport report)
    {
        private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(120);

        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private long _appliedBytes;
        private long _currentBytes;
        private string _currentPath = string.Empty;
        private TimeSpan? _lastReport;

        public void BeginFile(string relativePath)
        {
            _currentPath = relativePath;
            _currentBytes = 0;
            _lastReport = null;
        }

        public void Advance(long transferredOfCurrentFile)
        {
            _currentBytes = transferredOfCurrentFile;

            var now = _clock.Elapsed;

            if (_lastReport is { } last && now - last < ReportInterval)
            {
                return;
            }

            _lastReport = now;
            Report(_currentPath);
        }

        public void EndFile()
        {
            _appliedBytes += _currentBytes;
            _currentBytes = 0;
        }

        public void Report(string relativePath)
        {
            progress?.Report(new(report.SuccessCount + report.Errors.Count, relativePath, _appliedBytes + _currentBytes));
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
    public bool Verified { get; private set; }
    public List<SyncApplied> Applied { get; } = [];
    public List<SyncError> Errors { get; } = [];
    public List<SyncMismatch> Mismatches { get; } = [];

    public void MarkVerified()
    {
        Verified = true;
    }

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
