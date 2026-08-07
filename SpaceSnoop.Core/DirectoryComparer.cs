using Microsoft.Extensions.Logging;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class DirectoryComparer(ExclusionFilter exclusionFilter, ILogger<DirectoryComparer> logger)
{
    public static readonly TimeSpan FatTimestampTolerance = TimeSpan.FromSeconds(2);

    public static bool FilesIdentical(FileInfo left, FileInfo right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return (left.LastWriteTime - right.LastWriteTime).Duration() <= FatTimestampTolerance;
    }

    public ComparisonResult Compare(string leftPath, string rightPath, CancellationToken cancel, IProgress<OperationProgress>? progress = null)
    {
        var leftDir = new DirectoryInfo(leftPath);
        var rightDir = new DirectoryInfo(rightPath);

        var processed = 0;
        var root = CompareDirectories(leftDir, rightDir, "", progress, ref processed, cancel);

        return new(leftPath, rightPath, root);
    }

    private static ComparisonStatus DetermineDirectoryStatus(
        DirectoryComparison comparison,
        DirectoryInfo? leftDir,
        DirectoryInfo? rightDir)
    {
        if (leftDir is null or { Exists: false })
        {
            return ComparisonStatus.RightOnly;
        }

        if (rightDir is null or { Exists: false })
        {
            return ComparisonStatus.LeftOnly;
        }

        var hasNonIdentical = comparison.Files.Any(x => x.Status != ComparisonStatus.Identical)
                              || comparison.SubDirectories.Any(x => x.Status != ComparisonStatus.Identical);

        return hasNonIdentical ? ComparisonStatus.Modified : ComparisonStatus.Identical;
    }

    private static bool IsReparsePoint(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private DirectoryComparison CompareDirectories(
        DirectoryInfo? leftDir,
        DirectoryInfo? rightDir,
        string relativePath,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        var name = leftDir?.Name ?? rightDir!.Name;
        var comparison = new DirectoryComparison(name, relativePath)
        {
            LeftModified = leftDir is { Exists: true } ? leftDir.LastWriteTime : null,
            RightModified = rightDir is { Exists: true } ? rightDir.LastWriteTime : null,
        };

        var leftFiles = GetFilteredFiles(leftDir, out var leftFilesIncomplete);
        var rightFiles = GetFilteredFiles(rightDir, out var rightFilesIncomplete);
        var leftDirs = GetFilteredDirectories(leftDir, out var leftDirsIncomplete);
        var rightDirs = GetFilteredDirectories(rightDir, out var rightDirsIncomplete);

        comparison.LeftIncomplete = leftFilesIncomplete || leftDirsIncomplete;
        comparison.RightIncomplete = rightFilesIncomplete || rightDirsIncomplete;

        var typeConflicts = CollectTypeConflicts(leftFiles, rightFiles, leftDirs, rightDirs);

        CompareFiles(comparison, leftFiles, rightFiles, typeConflicts, relativePath);
        CompareSubDirectories(comparison, leftDirs, rightDirs, typeConflicts, relativePath, progress, ref processed, cancel);

        comparison.Status = DetermineDirectoryStatus(comparison, leftDir, rightDir);

        progress?.Report(new(++processed, string.IsNullOrEmpty(relativePath) ? name : relativePath));

        return comparison;
    }

    private static Dictionary<string, FileTypeConflict> CollectTypeConflicts(
        Dictionary<string, FileInfo> leftFiles,
        Dictionary<string, FileInfo> rightFiles,
        Dictionary<string, DirectoryInfo> leftDirs,
        Dictionary<string, DirectoryInfo> rightDirs)
    {
        var conflicts = new Dictionary<string, FileTypeConflict>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in leftFiles.Keys.Where(rightDirs.ContainsKey))
        {
            conflicts[name] = FileTypeConflict.LeftFileRightDirectory;
        }

        foreach (var name in rightFiles.Keys.Where(leftDirs.ContainsKey))
        {
            conflicts[name] = FileTypeConflict.RightFileLeftDirectory;
        }

        return conflicts;
    }

    private static void CompareFiles(
        DirectoryComparison comparison,
        Dictionary<string, FileInfo> leftFiles,
        Dictionary<string, FileInfo> rightFiles,
        Dictionary<string, FileTypeConflict> typeConflicts,
        string relativePath)
    {
        var allNames = new HashSet<string>(leftFiles.Keys, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(rightFiles.Keys);

        foreach (var fileName in allNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileRelativePath = string.IsNullOrEmpty(relativePath)
                ? fileName
                : Path.Combine(relativePath, fileName);

            leftFiles.TryGetValue(fileName, out var leftFile);
            rightFiles.TryGetValue(fileName, out var rightFile);
            typeConflicts.TryGetValue(fileName, out var typeConflict);

            comparison.Files.Add(CompareFile(fileName, fileRelativePath, leftFile, rightFile, typeConflict));
        }
    }

    private static FileComparison CompareFile(
        string fileName,
        string relativePath,
        FileInfo? leftFile,
        FileInfo? rightFile,
        FileTypeConflict typeConflict)
    {
        var fileComparison = new FileComparison(fileName, relativePath);

        if (typeConflict != FileTypeConflict.None)
        {
            fileComparison.TypeConflict = typeConflict;
            fileComparison.Status = ComparisonStatus.Conflict;
            ApplyLeft(fileComparison, leftFile);
            ApplyRight(fileComparison, rightFile);

            return fileComparison;
        }

        ApplyLeft(fileComparison, leftFile);
        ApplyRight(fileComparison, rightFile);

        fileComparison.Status = (leftFile, rightFile) switch
        {
            (not null, not null) => FilesIdentical(leftFile, rightFile) ? ComparisonStatus.Identical : ComparisonStatus.Modified,
            (not null, null) => ComparisonStatus.LeftOnly,
            _ => ComparisonStatus.RightOnly,
        };

        return fileComparison;
    }

    private static void ApplyLeft(FileComparison comparison, FileInfo? file)
    {
        if (file is null)
        {
            return;
        }

        comparison.LeftSize = file.Length;
        comparison.LeftModified = file.LastWriteTime;
    }

    private static void ApplyRight(FileComparison comparison, FileInfo? file)
    {
        if (file is null)
        {
            return;
        }

        comparison.RightSize = file.Length;
        comparison.RightModified = file.LastWriteTime;
    }

    private void CompareSubDirectories(
        DirectoryComparison comparison,
        Dictionary<string, DirectoryInfo> leftDirs,
        Dictionary<string, DirectoryInfo> rightDirs,
        Dictionary<string, FileTypeConflict> typeConflicts,
        string relativePath,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        var allNames = new HashSet<string>(leftDirs.Keys, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(rightDirs.Keys);
        allNames.ExceptWith(typeConflicts.Keys);

        foreach (var dirName in allNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var dirRelativePath = string.IsNullOrEmpty(relativePath)
                ? dirName
                : Path.Combine(relativePath, dirName);

            leftDirs.TryGetValue(dirName, out var leftSub);
            rightDirs.TryGetValue(dirName, out var rightSub);

            var subComparison = CompareDirectories(leftSub, rightSub, dirRelativePath, progress, ref processed, cancel);
            comparison.SubDirectories.Add(subComparison);
        }
    }

    private Dictionary<string, FileInfo> GetFilteredFiles(DirectoryInfo? dir, out bool incomplete)
    {
        if (dir is not { Exists: true })
        {
            incomplete = dir is not null;
            return new(StringComparer.OrdinalIgnoreCase);
        }

        incomplete = false;
        var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in dir.EnumerateFiles().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                if (IsReparsePoint(file))
                {
                    logger.CompareReparsePointSkipped(file.FullName);
                    continue;
                }

                result[file.Name] = file;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            incomplete = true;
            logger.CompareDirectorySkipped(ex, dir.FullName);
        }

        return result;
    }

    private Dictionary<string, DirectoryInfo> GetFilteredDirectories(DirectoryInfo? dir, out bool incomplete)
    {
        if (dir is not { Exists: true })
        {
            incomplete = dir is not null;
            return new(StringComparer.OrdinalIgnoreCase);
        }

        incomplete = false;
        var result = new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var sub in dir.EnumerateDirectories().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                if (IsReparsePoint(sub))
                {
                    logger.CompareReparsePointSkipped(sub.FullName);
                    continue;
                }

                result[sub.Name] = sub;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            incomplete = true;
            logger.CompareDirectorySkipped(ex, dir.FullName);
        }

        return result;
    }
}
