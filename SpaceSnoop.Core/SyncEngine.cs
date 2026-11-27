using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class SyncEngine
{
    private const string TempSuffix = ".sstmp";
    private const int TempNameAttempts = 20;
    private const uint DeleteAccess = 0x0001_0000;
    private const uint FileReadAttributes = 0x0080;
    private const uint FileWriteAttributes = 0x0100;
    private const uint OpenExisting = 3;
    private const uint FileFlagOpenReparsePoint = 0x0020_0000;
    private const int FileBasicInfoClass = 0;
    private const int FileDispositionInfoClass = 4;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int ShortPathLimit = 259;
    private const string LongPrefix = @"\\?\";
    private const string LongUncPrefix = @"\\?\UNC\";

    private readonly ILogger<SyncEngine> _logger;
    private readonly bool _showDeleteUi;
    private readonly bool _recycleOverwritten;
    private readonly bool _allowPermanentNetworkDelete;
    private readonly Func<string, bool> _supportsRecycleBin;

    public SyncEngine(
        ILogger<SyncEngine> logger,
        bool showDeleteUi = true,
        bool recycleOverwritten = false,
        bool allowPermanentNetworkDelete = false)
        : this(logger, showDeleteUi, recycleOverwritten, allowPermanentNetworkDelete, RecycleBin.SupportsRecycleBin)
    {
    }

    internal SyncEngine(
        ILogger<SyncEngine> logger,
        bool showDeleteUi,
        bool recycleOverwritten,
        bool allowPermanentNetworkDelete,
        Func<string, bool> supportsRecycleBin)
    {
        _logger = logger;
        _showDeleteUi = showDeleteUi;
        _recycleOverwritten = recycleOverwritten;
        _allowPermanentNetworkDelete = allowPermanentNetworkDelete;
        _supportsRecycleBin = supportsRecycleBin;
    }

    public SyncReport Execute(ComparisonResult comparisonResult, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        return Execute(comparisonResult, new SyncReport(), cancel, progress);
    }

    public SyncReport Execute(ComparisonResult comparisonResult, SyncReport report, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        // TODO: корни сверяются один раз до прогона – каталог, подменённый на junction во время прогона, пишется по новой цели;
        // при появлении недоверенного процесса с правом записи в корни – открывать корни дескриптором и писать относительно него
        SyncRoots.EnsureDisjoint(comparisonResult.LeftPath, comparisonResult.RightPath);

        var blocked = new Dictionary<(DirectoryComparison Directory, SyncAction Action), bool>();

        if (!_allowPermanentNetworkDelete
            && FindPermanentNetworkDeletePath(comparisonResult, _supportsRecycleBin, blocked) is { } noRecycleBinPath)
        {
            throw new RecycleBinUnavailableException(noRecycleBinPath);
        }

        var tracker = new TransferTracker(progress, report);
        ExecuteRecursive(comparisonResult.Root, comparisonResult.LeftPath, comparisonResult.RightPath, report, tracker, cancel, DeleteGate.Open, blocked);
        return report;
    }

    public static string? FindPermanentNetworkDeletePath(
        ComparisonResult comparisonResult,
        Func<string, bool> supportsRecycleBin)
    {
        return FindPermanentNetworkDeletePath(comparisonResult, supportsRecycleBin, new());
    }

    private static string? FindPermanentNetworkDeletePath(
        ComparisonResult comparisonResult,
        Func<string, bool> supportsRecycleBin,
        Dictionary<(DirectoryComparison Directory, SyncAction Action), bool> blocked)
    {
        return FindPermanentNetworkDeleteRecursive(
            comparisonResult.Root,
            comparisonResult.LeftPath,
            comparisonResult.RightPath,
            supportsRecycleBin,
            DeleteGate.Open,
            blocked);
    }

    private static string? FindPermanentNetworkDeleteRecursive(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        Func<string, bool> supportsRecycleBin,
        DeleteGate gate,
        Dictionary<(DirectoryComparison Directory, SyncAction Action), bool> blocked)
    {
        gate = gate.Inherit(dir);

        if (dir.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight
            && !(gate.Blocks(dir.Action) || SubtreeBlocks(dir, dir.Action, blocked)))
        {
            var targetPath = TargetPath(dir.Action, leftBase, rightBase, dir.RelativePath);
            return supportsRecycleBin(targetPath) ? null : targetPath;
        }

        foreach (var file in dir.Files)
        {
            if (!ShouldApply(file)
                || gate.Blocks(file.Action, file.DeleteLeftBlocked, file.DeleteRightBlocked)
                || file.TypeConflict != FileTypeConflict.None
                || file.Action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                continue;
            }

            var targetPath = TargetPath(file.Action, leftBase, rightBase, file.RelativePath);

            if (!supportsRecycleBin(targetPath))
            {
                return targetPath;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            if (FindPermanentNetworkDeleteRecursive(sub, leftBase, rightBase, supportsRecycleBin, gate, blocked) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static string TargetPath(SyncAction action, string leftBase, string rightBase, string relativePath)
    {
        var basePath = action == SyncAction.DeleteLeft ? leftBase : rightBase;
        return Path.Combine(basePath, relativePath);
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
        _logger.SyncDeleteBlocked(action, relativePath);

        return true;
    }

    private void RejectTypeConflict(FileComparison file, SyncReport report)
    {
        var reason = file.TypeConflict switch
        {
            FileTypeConflict.LeftLinkRightObject => "слева ссылка, справа настоящий объект – разрешается вручную",
            FileTypeConflict.RightLinkLeftObject => "справа ссылка, слева настоящий объект – разрешается вручную",
            FileTypeConflict.LeftFileRightDirectory => "слева файл, справа каталог – разрешается вручную",
            FileTypeConflict.CaseCollision => "имена различаются только регистром, приёмник их не различит – разрешается вручную",
            _ => "справа файл, слева каталог – разрешается вручную",
        };

        report.Errors.Add(new(file.RelativePath, file.Action, $"действие отклонено: {reason}"));
        _logger.SyncTypeConflictBlocked(file.Action, file.RelativePath, file.TypeConflict);
    }

    private static bool SubtreeBlocks(
        DirectoryComparison dir,
        SyncAction action,
        Dictionary<(DirectoryComparison Directory, SyncAction Action), bool> cache)
    {
        if (cache.TryGetValue((dir, action), out var known))
        {
            return known;
        }

        var blocked = false;

        foreach (var file in dir.Files)
        {
            if (DeleteGate.Open.Blocks(action, file.DeleteLeftBlocked, file.DeleteRightBlocked))
            {
                blocked = true;
                break;
            }
        }

        if (!blocked)
        {
            foreach (var sub in dir.SubDirectories)
            {
                if (DeleteGate.Open.Inherit(sub).Blocks(action) || SubtreeBlocks(sub, action, cache))
                {
                    blocked = true;
                    break;
                }
            }
        }

        cache[(dir, action)] = blocked;
        return blocked;
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
                if (File.Exists(ExtendedPath(leftPath)))
                {
                    DeleteFile(leftPath, file, cancel);
                }

                break;

            case SyncAction.DeleteRight:
                if (File.Exists(ExtendedPath(rightPath)))
                {
                    DeleteFile(rightPath, file, cancel);
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
        if (!_recycleOverwritten || !File.Exists(destination) || !_supportsRecycleBin(destination))
        {
            return;
        }

        RecycleLocalFile(destination);
    }

    private void DeleteFile(string path, FileComparison plan, CancellationToken cancel)
    {
        if (!_supportsRecycleBin(path))
        {
            if (!_allowPermanentNetworkDelete)
            {
                throw new RecycleBinUnavailableException(path);
            }

            DeleteVerifiedFile(path, plan, cancel);
            _logger.SyncPermanentNetworkDelete("файл", path);
            return;
        }

        ClearReadOnly(path);
        RecycleLocalFile(path);
    }

    private void RecycleLocalFile(string path)
    {
        if (_showDeleteUi)
        {
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            RecycleBin.DeleteSilent(path);
        }
    }

    private void DeleteDirectory(string path, DirectoryComparison dir, SyncReport report, TransferTracker tracker, CancellationToken cancel)
    {
        if (!Directory.Exists(ExtendedPath(path)))
        {
            return;
        }

        if (!_supportsRecycleBin(path))
        {
            if (!_allowPermanentNetworkDelete)
            {
                throw new RecycleBinUnavailableException(path);
            }

            DeleteVerifiedDirectory(path, dir, report, tracker, cancel);
            _logger.SyncPermanentNetworkDelete("каталог", path);
            return;
        }

        if (_showDeleteUi)
        {
            FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        }
        else
        {
            RecycleBin.DeleteSilent(path);
        }
    }

    private void DeleteVerifiedDirectory(string rootPath, DirectoryComparison rootDir, SyncReport report, TransferTracker tracker, CancellationToken cancel)
    {
        if ((File.GetAttributes(ExtendedPath(rootPath)) & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException($"Каталог является ссылкой «{rootPath}» – безвозвратное удаление отменено.");
        }

        var expectedAction = rootDir.Action;
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        var stack = new Stack<(string Path, DirectoryComparison Plan)>();
        var directories = new List<(string Path, string RelativePath, SyncAction Action)>();
        var files = new List<(string Path, FileComparison Plan)>();
        stack.Push((rootPath, rootDir));

        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var (currentPath, currentPlan) = stack.Pop();
            directories.Add((currentPath, currentPlan.RelativePath, expectedAction));
            var expectedFiles = new Dictionary<string, FileComparison>(currentPlan.Files.Count, StringComparer.Ordinal);

            foreach (var file in currentPlan.Files)
            {
                if (!expectedFiles.TryAdd(file.Name, file))
                {
                    throw new IOException("Каталог изменился после сравнения – безвозвратное удаление отменено.");
                }
            }

            var expectedDirs = new Dictionary<string, DirectoryComparison>(currentPlan.SubDirectories.Count, StringComparer.Ordinal);

            foreach (var sub in currentPlan.SubDirectories)
            {
                if (!expectedDirs.TryAdd(sub.Name, sub))
                {
                    throw new IOException("Каталог изменился после сравнения – безвозвратное удаление отменено.");
                }
            }

            foreach (var entry in new DirectoryInfo(ExtendedPath(currentPath)).EnumerateFileSystemInfos("*", options))
            {
                cancel.ThrowIfCancellationRequested();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"В каталоге обнаружена ссылка «{entry.FullName}» – безвозвратное удаление отменено.");
                }

                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    if (!expectedDirs.TryGetValue(entry.Name, out var subPlan) || subPlan.Action != expectedAction)
                    {
                        throw new IOException("Каталог изменился после сравнения – безвозвратное удаление отменено.");
                    }

                    stack.Push((Path.Combine(currentPath, entry.Name), subPlan));
                    continue;
                }

                if (entry is not FileInfo fileInfo
                    || !expectedFiles.TryGetValue(entry.Name, out var filePlan)
                    || filePlan.Action != expectedAction
                    || filePlan.TypeConflict != FileTypeConflict.None)
                {
                    throw new IOException("Каталог изменился после сравнения – безвозвратное удаление отменено.");
                }

                VerifyFileMatchesPlan(fileInfo, filePlan);
                files.Add((Path.Combine(currentPath, entry.Name), filePlan));
            }
        }

        cancel.ThrowIfCancellationRequested();

        var deletedFiles = new List<FileComparison>();
        var deletedDirectories = new List<(string RelativePath, SyncAction Action)>();

        try
        {
            foreach (var (path, plan) in files)
            {
                DeleteVerifiedFile(path, plan, cancel);
                deletedFiles.Add(plan);
                tracker.Report(plan.RelativePath);
            }

            for (var index = directories.Count - 1; index >= 0; index--)
            {
                cancel.ThrowIfCancellationRequested();
                var (dirPath, dirRelativePath, dirAction) = directories[index];
                var directory = new DirectoryInfo(ExtendedPath(dirPath));

                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Каталог является ссылкой «{dirPath}» – безвозвратное удаление отменено.");
                }

                if ((directory.Attributes & FileAttributes.ReadOnly) != 0)
                {
                    directory.Attributes &= ~FileAttributes.ReadOnly;
                }

                directory.Delete(recursive: false);
                deletedDirectories.Add((dirRelativePath, dirAction));
                tracker.Report(dirRelativePath);
            }
        }
        catch
        {
            RecordPartialDirectoryDeletions(report, deletedFiles, deletedDirectories);
            throw;
        }
    }

    private void RecordPartialDirectoryDeletions(
        SyncReport report,
        List<FileComparison> deletedFiles,
        List<(string RelativePath, SyncAction Action)> deletedDirectories)
    {
        foreach (var file in deletedFiles)
        {
            report.DeletedCount++;
            report.AddApplied(file.Action, file.RelativePath, AppliedBytes(file));
            _logger.SyncFileApplied(file.Action, file.RelativePath);
        }

        foreach (var (relativePath, action) in deletedDirectories)
        {
            report.DeletedCount++;
            report.AddApplied(action, relativePath, 0);
            _logger.SyncFileApplied(action, relativePath);
        }
    }

    private static void VerifyFileMatchesPlan(FileInfo file, FileComparison plan)
    {
        var deleteLeft = plan.Action == SyncAction.DeleteLeft;
        var size = deleteLeft ? plan.LeftSize : plan.RightSize;
        var modified = deleteLeft ? plan.LeftModified : plan.RightModified;

        if (!file.Exists
            || (file.Attributes & FileAttributes.ReparsePoint) != 0
            || file.Length != size
            || file.LastWriteTime != modified)
        {
            throw new IOException($"Файл «{file.FullName}» изменился после сравнения – безвозвратное удаление отменено.");
        }
    }

    private static void DeleteVerifiedFile(string path, FileComparison plan, CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        var deleteLeft = plan.Action == SyncAction.DeleteLeft;
        var expectedSize = deleteLeft ? plan.LeftSize : plan.RightSize;
        var expectedModified = deleteLeft ? plan.LeftModified : plan.RightModified;

        using var handle = OpenFileForVerifiedDelete(path);

        if (!GetFileInformationByHandle(handle, out var info))
        {
            throw Win32Error(path, Marshal.GetLastWin32Error());
        }

        if ((info.FileAttributes & ((uint)FileAttributes.Directory | (uint)FileAttributes.ReparsePoint)) != 0)
        {
            throw new IOException($"Файл «{path}» изменился после сравнения – безвозвратное удаление отменено.");
        }

        var actualSize = checked((long)(((ulong)info.FileSizeHigh << 32) | info.FileSizeLow));
        var actualModified = DateTime.FromFileTime(checked((long)(((ulong)info.LastWriteTimeHigh << 32) | info.LastWriteTimeLow)));

        if (actualSize != expectedSize || actualModified != expectedModified)
        {
            throw new IOException($"Файл «{path}» изменился после сравнения – безвозвратное удаление отменено.");
        }

        cancel.ThrowIfCancellationRequested();

        if ((info.FileAttributes & (uint)FileAttributes.ReadOnly) != 0)
        {
            var cleared = info.FileAttributes & ~(uint)FileAttributes.ReadOnly;
            var basic = new FileBasicInfo
            {
                FileAttributes = cleared == 0 ? (uint)FileAttributes.Normal : cleared,
            };

            if (!SetFileInformationByHandle(handle, FileBasicInfoClass, ref basic, (uint)Marshal.SizeOf<FileBasicInfo>()))
            {
                throw Win32Error(path, Marshal.GetLastWin32Error());
            }
        }

        byte deleteFlag = 1;

        if (!SetFileInformationByHandle(handle, FileDispositionInfoClass, ref deleteFlag, 1))
        {
            throw Win32Error(path, Marshal.GetLastWin32Error());
        }
    }

    private static SafeFileHandle OpenFileForVerifiedDelete(string path)
    {
        var handle = CreateFileW(
            ExtendedPath(path),
            DeleteAccess | FileReadAttributes | FileWriteAttributes,
            0,
            IntPtr.Zero,
            OpenExisting,
            FileFlagOpenReparsePoint,
            IntPtr.Zero);

        if (!handle.IsInvalid)
        {
            return handle;
        }

        var error = Marshal.GetLastWin32Error();
        handle.Dispose();

        if (error is ErrorFileNotFound or ErrorPathNotFound)
        {
            throw new IOException($"Файл «{path}» изменился после сравнения – безвозвратное удаление отменено.", new Win32Exception(error));
        }

        throw Win32Error(path, error);
    }

    private static IOException Win32Error(string path, int errorCode)
    {
        var error = new Win32Exception(errorCode);
        return new($"«{path}»: {error.Message} (Win32: {error.NativeErrorCode}).", error);
    }

    private static string ExtendedPath(string path)
    {
        if (path.StartsWith(LongPrefix, StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
        {
            return path;
        }

        if (path.Length <= ShortPathLimit && !HasTrailingSpaceOrDotSegment(path))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal)
            ? LongUncPrefix + path[2..]
            : LongPrefix + path;
    }

    private static bool HasTrailingSpaceOrDotSegment(string path)
    {
        var span = path.AsSpan();

        while (span.Length > 0)
        {
            var sep = span.IndexOfAny('\\', '/');
            var segment = sep >= 0 ? span[..sep] : span;

            if (segment.Length > 0 && segment is not "." and not ".." && (segment[^1] == ' ' || segment[^1] == '.'))
            {
                return true;
            }

            if (sep < 0)
            {
                break;
            }

            span = span[(sep + 1)..];
        }

        return false;
    }

    private void ApplyDirectoryAction(DirectoryComparison dir, string leftBase, string rightBase, SyncReport report, TransferTracker tracker, CancellationToken cancel)
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
                    DeleteDirectory(Path.Combine(leftBase, dir.RelativePath), dir, report, tracker, cancel);
                    report.DeletedCount++;
                    break;

                case SyncAction.DeleteRight:
                    DeleteDirectory(Path.Combine(rightBase, dir.RelativePath), dir, report, tracker, cancel);
                    report.DeletedCount++;
                    break;
            }

            report.AddApplied(dir.Action, dir.RelativePath, 0);
            _logger.SyncFileApplied(dir.Action, dir.RelativePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Errors.Add(new(dir.RelativePath, dir.Action, ex.Message));
            _logger.SyncFileFailed(ex, dir.Action, dir.RelativePath);
        }
    }

    private void ExecuteRecursive(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        SyncReport report,
        TransferTracker tracker,
        CancellationToken cancel,
        DeleteGate gate,
        Dictionary<(DirectoryComparison Directory, SyncAction Action), bool> blocked)
    {
        cancel.ThrowIfCancellationRequested();

        gate = gate.Inherit(dir);

        if (dir.Action is SyncAction.DeleteLeft or SyncAction.DeleteRight)
        {
            if (gate.Blocks(dir.Action) || SubtreeBlocks(dir, dir.Action, blocked))
            {
                gate = gate with
                {
                    Reported = Reject(dir.Action, dir.RelativePath, report, gate.Reported),
                };
            }
            else
            {
                cancel.ThrowIfCancellationRequested();
                ApplyDirectoryActionAndReport(dir, leftBase, rightBase, report, tracker, cancel);
                return;
            }
        }
        else if (dir.Action is SyncAction.CopyToRight or SyncAction.CopyToLeft)
        {
            ApplyDirectoryActionAndReport(dir, leftBase, rightBase, report, tracker, cancel);
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
                gate = gate with
                {
                    Reported = Reject(file.Action, file.RelativePath, report, gate.Reported),
                };

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
            ExecuteRecursive(sub, leftBase, rightBase, report, tracker, cancel, gate, blocked);
        }
    }

    private void ApplyDirectoryActionAndReport(
        DirectoryComparison dir,
        string leftBase,
        string rightBase,
        SyncReport report,
        TransferTracker tracker,
        CancellationToken cancel)
    {
        ApplyDirectoryAction(dir, leftBase, rightBase, report, tracker, cancel);
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
            _logger.SyncFileApplied(file.Action, file.RelativePath);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            report.Errors.Add(new(file.RelativePath, file.Action, ex.Message));
            _logger.SyncFileFailed(ex, file.Action, file.RelativePath);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string path,
        uint access,
        uint share,
        IntPtr security,
        uint disposition,
        uint flags,
        IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation info);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        ref FileBasicInfo information,
        uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(
        SafeFileHandle handle,
        int informationClass,
        ref byte information,
        uint size);

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

            if (_lastReport is { } last && (now - last) < ReportInterval)
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

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
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
