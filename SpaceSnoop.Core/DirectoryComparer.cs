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

    public ComparisonResult Compare(
        string leftPath,
        string rightPath,
        CancellationToken cancel,
        IProgress<OperationProgress>? progress = null,
        PathCaseRules? caseRules = null)
    {
        var leftDir = new DirectoryInfo(leftPath);
        var rightDir = new DirectoryInfo(rightPath);
        var rules = caseRules ?? PathCaseRules.For(leftPath, rightPath);

        var processed = 0;
        var root = CompareDirectories(leftDir, rightDir, "", rules, progress, ref processed, cancel);

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
        PathCaseRules rules,
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

        var leftLinks = new HashSet<string>(rules.Left);
        var rightLinks = new HashSet<string>(rules.Right);

        var leftFiles = GetFilteredFiles(leftDir, leftLinks, rules.Left, out var leftFilesIncomplete);
        var rightFiles = GetFilteredFiles(rightDir, rightLinks, rules.Right, out var rightFilesIncomplete);
        var leftDirs = GetFilteredDirectories(leftDir, leftLinks, rules.Left, out var leftDirsIncomplete);
        var rightDirs = GetFilteredDirectories(rightDir, rightLinks, rules.Right, out var rightDirsIncomplete);

        comparison.LeftIncomplete = leftFilesIncomplete || leftDirsIncomplete;
        comparison.RightIncomplete = rightFilesIncomplete || rightDirsIncomplete;
        comparison.SkippedLinks.AddRange(leftLinks.Union(rightLinks, rules.Match).Order(rules.Match));

        var typeConflicts = CollectTypeConflicts(leftFiles, rightFiles, leftDirs, rightDirs, leftLinks, rightLinks, rules);

        CompareFiles(comparison, leftFiles, rightFiles, typeConflicts, relativePath, rules);
        CompareSubDirectories(comparison, leftDirs, rightDirs, typeConflicts, relativePath, rules, progress, ref processed, cancel);

        comparison.Status = DetermineDirectoryStatus(comparison, leftDir, rightDir);

        progress?.Report(new(++processed, string.IsNullOrEmpty(relativePath) ? name : relativePath));

        return comparison;
    }

    private static Dictionary<string, FileTypeConflict> CollectTypeConflicts(
        Dictionary<string, FileInfo> leftFiles,
        Dictionary<string, FileInfo> rightFiles,
        Dictionary<string, DirectoryInfo> leftDirs,
        Dictionary<string, DirectoryInfo> rightDirs,
        HashSet<string> leftLinks,
        HashSet<string> rightLinks,
        PathCaseRules rules)
    {
        var conflicts = new Dictionary<string, FileTypeConflict>(rules.Match);

        foreach (var name in CollectCaseCollisions(leftFiles, rightFiles, leftDirs, rightDirs, leftLinks, rightLinks, rules))
        {
            conflicts[name] = FileTypeConflict.CaseCollision;
        }

        foreach (var name in leftFiles.Keys.Where(rightDirs.ContainsKey))
        {
            conflicts[name] = FileTypeConflict.LeftFileRightDirectory;
        }

        foreach (var name in rightFiles.Keys.Where(leftDirs.ContainsKey))
        {
            conflicts[name] = FileTypeConflict.RightFileLeftDirectory;
        }

        foreach (var name in leftLinks.Where(x => rightFiles.ContainsKey(x) || rightDirs.ContainsKey(x)))
        {
            conflicts[name] = FileTypeConflict.LeftLinkRightObject;
        }

        foreach (var name in rightLinks.Where(x => leftFiles.ContainsKey(x) || leftDirs.ContainsKey(x)))
        {
            conflicts[name] = FileTypeConflict.RightLinkLeftObject;
        }

        return conflicts;
    }

    private static IEnumerable<string> CollectCaseCollisions(
        Dictionary<string, FileInfo> leftFiles,
        Dictionary<string, FileInfo> rightFiles,
        Dictionary<string, DirectoryInfo> leftDirs,
        Dictionary<string, DirectoryInfo> rightDirs,
        HashSet<string> leftLinks,
        HashSet<string> rightLinks,
        PathCaseRules rules)
    {
        if (!rules.MatchIsSensitive || PathCase.IsSensitive(rules.Left) == PathCase.IsSensitive(rules.Right))
        {
            return [];
        }

        return leftFiles.Keys
            .Concat(rightFiles.Keys)
            .Concat(leftDirs.Keys)
            .Concat(rightDirs.Keys)
            .Concat(leftLinks)
            .Concat(rightLinks)
            .Distinct(StringComparer.Ordinal)
            .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Skip(1).Any())
            .SelectMany(x => x)
            .ToArray();
    }

    private static void CompareFiles(
        DirectoryComparison comparison,
        Dictionary<string, FileInfo> leftFiles,
        Dictionary<string, FileInfo> rightFiles,
        Dictionary<string, FileTypeConflict> typeConflicts,
        string relativePath,
        PathCaseRules rules)
    {
        var allNames = new HashSet<string>(leftFiles.Keys, rules.Match);
        allNames.UnionWith(rightFiles.Keys);
        allNames.UnionWith(typeConflicts.Keys);

        foreach (var fileName in allNames.Order(rules.Match))
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
        PathCaseRules rules,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        var allNames = new HashSet<string>(leftDirs.Keys, rules.Match);
        allNames.UnionWith(rightDirs.Keys);
        allNames.ExceptWith(typeConflicts.Keys);

        foreach (var dirName in allNames.Order(rules.Match))
        {
            var dirRelativePath = string.IsNullOrEmpty(relativePath)
                ? dirName
                : Path.Combine(relativePath, dirName);

            leftDirs.TryGetValue(dirName, out var leftSub);
            rightDirs.TryGetValue(dirName, out var rightSub);

            var subComparison = CompareDirectories(leftSub, rightSub, dirRelativePath, rules, progress, ref processed, cancel);
            comparison.SubDirectories.Add(subComparison);
        }
    }

    private Dictionary<string, FileInfo> GetFilteredFiles(DirectoryInfo? dir, ICollection<string> links, StringComparer names, out bool incomplete)
    {
        if (dir is not { Exists: true })
        {
            incomplete = dir is not null;
            return new(names);
        }

        incomplete = false;
        var result = new Dictionary<string, FileInfo>(names);

        try
        {
            foreach (var file in dir.EnumerateFiles().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                if (IsReparsePoint(file))
                {
                    links.Add(file.Name);
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

    private Dictionary<string, DirectoryInfo> GetFilteredDirectories(DirectoryInfo? dir, ICollection<string> links, StringComparer names, out bool incomplete)
    {
        if (dir is not { Exists: true })
        {
            incomplete = dir is not null;
            return new(names);
        }

        incomplete = false;
        var result = new Dictionary<string, DirectoryInfo>(names);

        try
        {
            foreach (var sub in dir.EnumerateDirectories().Where(x => !exclusionFilter.IsExcluded(x.Name)))
            {
                if (IsReparsePoint(sub))
                {
                    links.Add(sub.Name);
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
