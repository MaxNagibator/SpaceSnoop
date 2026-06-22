using Microsoft.Extensions.Logging;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class DirectoryComparer(ExclusionFilter exclusionFilter, ILogger<DirectoryComparer> logger)
{
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

        CompareFiles(comparison, leftDir, rightDir, relativePath);
        CompareSubDirectories(comparison, leftDir, rightDir, relativePath, progress, ref processed, cancel);

        comparison.Status = DetermineDirectoryStatus(comparison, leftDir, rightDir);

        progress?.Report(new(++processed, string.IsNullOrEmpty(relativePath) ? name : relativePath));

        return comparison;
    }

    private void CompareFiles(
        DirectoryComparison comparison,
        DirectoryInfo? leftDir,
        DirectoryInfo? rightDir,
        string relativePath)
    {
        var leftFiles = GetFilteredFiles(leftDir);
        var rightFiles = GetFilteredFiles(rightDir);

        var allNames = new HashSet<string>(leftFiles.Keys, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(rightFiles.Keys);

        foreach (var fileName in allNames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var fileRelativePath = string.IsNullOrEmpty(relativePath)
                ? fileName
                : Path.Combine(relativePath, fileName);

            var fileComparison = new FileComparison(fileName, fileRelativePath);
            var hasLeft = leftFiles.TryGetValue(fileName, out var leftFile);
            var hasRight = rightFiles.TryGetValue(fileName, out var rightFile);

            if (hasLeft && hasRight)
            {
                fileComparison.LeftSize = leftFile!.Length;
                fileComparison.RightSize = rightFile!.Length;
                fileComparison.LeftModified = leftFile.LastWriteTime;
                fileComparison.RightModified = rightFile.LastWriteTime;

                var sizeDiffers = leftFile.Length != rightFile.Length;
                var timeDiffers = leftFile.LastWriteTime != rightFile.LastWriteTime;

                fileComparison.Status = sizeDiffers || timeDiffers
                    ? ComparisonStatus.Modified
                    : ComparisonStatus.Identical;
            }
            else if (hasLeft)
            {
                fileComparison.LeftSize = leftFile!.Length;
                fileComparison.LeftModified = leftFile.LastWriteTime;
                fileComparison.Status = ComparisonStatus.LeftOnly;
            }
            else
            {
                fileComparison.RightSize = rightFile!.Length;
                fileComparison.RightModified = rightFile.LastWriteTime;
                fileComparison.Status = ComparisonStatus.RightOnly;
            }

            comparison.Files.Add(fileComparison);
        }
    }

    private void CompareSubDirectories(
        DirectoryComparison comparison,
        DirectoryInfo? leftDir,
        DirectoryInfo? rightDir,
        string relativePath,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        var leftDirs = GetFilteredDirectories(leftDir);
        var rightDirs = GetFilteredDirectories(rightDir);

        var allNames = new HashSet<string>(leftDirs.Keys, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(rightDirs.Keys);

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

    private Dictionary<string, FileInfo> GetFilteredFiles(DirectoryInfo? dir)
    {
        if (dir is not { Exists: true })
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var file in dir.EnumerateFiles().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                result[file.Name] = file;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            logger.CompareDirectorySkipped(ex, dir.FullName);
        }

        return result;
    }

    private Dictionary<string, DirectoryInfo> GetFilteredDirectories(DirectoryInfo? dir)
    {
        if (dir is not { Exists: true })
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, DirectoryInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var sub in dir.EnumerateDirectories().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                result[sub.Name] = sub;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            logger.CompareDirectorySkipped(ex, dir.FullName);
        }

        return result;
    }
}
