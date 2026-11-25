using Microsoft.Extensions.Logging;
using System.IO.Enumeration;
using System.Security;

namespace SpaceSnoop.Core;

public sealed class DirectoryComparer(ExclusionFilter exclusionFilter, ILogger<DirectoryComparer> logger)
{
    private static readonly EnumerationOptions Enumeration = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
    };

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
        var skips = new SkipJournal(logger);

        try
        {
            var left = new DirSide(leftDir.FullName, leftDir.Name, leftDir.Exists ? leftDir.LastWriteTime : default, leftDir.Exists);
            var right = new DirSide(rightDir.FullName, rightDir.Name, rightDir.Exists ? rightDir.LastWriteTime : default, rightDir.Exists);
            var root = CompareDirectories(left, right, "", rules, skips, progress, ref processed, cancel);
            return new(leftPath, rightPath, root);
        }
        finally
        {
            skips.ReportTotal();
        }
    }

    private static bool FilesIdentical(in FileEntryData left, in FileEntryData right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        return (left.LastWriteTime - right.LastWriteTime).Duration() <= FatTimestampTolerance;
    }

    private static ComparisonStatus DetermineDirectoryStatus(
        DirectoryComparison comparison,
        DirSide? leftDir,
        DirSide? rightDir)
    {
        if (leftDir is not { Exists: true })
        {
            return ComparisonStatus.RightOnly;
        }

        if (rightDir is not { Exists: true })
        {
            return ComparisonStatus.LeftOnly;
        }

        var files = comparison.Files;

        for (var i = 0; i < files.Count; i++)
        {
            if (files[i].Status != ComparisonStatus.Identical)
            {
                return ComparisonStatus.Modified;
            }
        }

        var subs = comparison.SubDirectories;

        for (var i = 0; i < subs.Count; i++)
        {
            if (subs[i].Status != ComparisonStatus.Identical)
            {
                return ComparisonStatus.Modified;
            }
        }

        return ComparisonStatus.Identical;
    }

    private static bool IsTraversalError(Exception exception)
    {
        return exception is UnauthorizedAccessException or SecurityException or IOException or ArgumentException or NotSupportedException;
    }

    private DirectoryComparison CompareDirectories(
        DirSide? leftDir,
        DirSide? rightDir,
        string relativePath,
        PathCaseRules rules,
        SkipJournal skips,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        var name = leftDir?.Name ?? rightDir!.Value.Name;
        var comparison = new DirectoryComparison(name, relativePath)
        {
            LeftModified = leftDir is { Exists: true } left ? left.LastWriteTime : null,
            RightModified = rightDir is { Exists: true } right ? right.LastWriteTime : null,
        };

        EnumerateSide(
            leftDir,
            rules.Left,
            skips,
            cancel,
            out var leftFiles,
            out var leftDirs,
            out var leftLinks,
            out var leftIncomplete);

        EnumerateSide(
            rightDir,
            rules.Right,
            skips,
            cancel,
            out var rightFiles,
            out var rightDirs,
            out var rightLinks,
            out var rightIncomplete);

        comparison.LeftIncomplete = leftIncomplete;
        comparison.RightIncomplete = rightIncomplete;

        if (leftLinks is { Count: > 0 } || rightLinks is { Count: > 0 })
        {
            var mergedLinks = new HashSet<string>((leftLinks?.Count ?? 0) + (rightLinks?.Count ?? 0), rules.Match);

            if (leftLinks is not null)
            {
                mergedLinks.UnionWith(leftLinks);
            }

            if (rightLinks is not null)
            {
                mergedLinks.UnionWith(rightLinks);
            }

            if (mergedLinks.Count > 0)
            {
                var orderedLinks = mergedLinks.ToArray();
                Array.Sort(orderedLinks, rules.Match);
                comparison.SkippedLinks.AddRange(orderedLinks);
            }
        }

        var typeConflicts = CollectTypeConflicts(leftFiles, rightFiles, leftDirs, rightDirs, leftLinks, rightLinks, rules);

        CompareFiles(comparison, leftFiles, rightFiles, typeConflicts, relativePath, rules);
        CompareSubDirectories(
            comparison,
            leftDirs,
            rightDirs,
            typeConflicts,
            relativePath,
            rules,
            skips,
            progress,
            ref processed,
            cancel);

        comparison.Status = DetermineDirectoryStatus(comparison, leftDir, rightDir);

        progress?.Report(new(++processed, string.IsNullOrEmpty(relativePath) ? name : relativePath));

        return comparison;
    }

    private static Dictionary<string, FileTypeConflict>? CollectTypeConflicts(
        Dictionary<string, FileEntryData> leftFiles,
        Dictionary<string, FileEntryData> rightFiles,
        Dictionary<string, DirEntryData> leftDirs,
        Dictionary<string, DirEntryData> rightDirs,
        HashSet<string>? leftLinks,
        HashSet<string>? rightLinks,
        PathCaseRules rules)
    {
        Dictionary<string, FileTypeConflict>? conflicts = null;

        if (rules.MatchIsSensitive && PathCase.IsSensitive(rules.Left) != PathCase.IsSensitive(rules.Right))
        {
            foreach (var name in CollectCaseCollisions(leftFiles, rightFiles, leftDirs, rightDirs, leftLinks, rightLinks))
            {
                (conflicts ??= new(rules.Match))[name] = FileTypeConflict.CaseCollision;
            }
        }

        if (leftFiles.Count > 0 && rightDirs.Count > 0)
        {
            foreach (var name in leftFiles.Keys)
            {
                if (rightDirs.ContainsKey(name))
                {
                    (conflicts ??= new(rules.Match))[name] = FileTypeConflict.LeftFileRightDirectory;
                }
            }
        }

        if (rightFiles.Count > 0 && leftDirs.Count > 0)
        {
            foreach (var name in rightFiles.Keys)
            {
                if (leftDirs.ContainsKey(name))
                {
                    (conflicts ??= new(rules.Match))[name] = FileTypeConflict.RightFileLeftDirectory;
                }
            }
        }

        if (leftLinks is { Count: > 0 } && (rightFiles.Count > 0 || rightDirs.Count > 0))
        {
            foreach (var name in leftLinks)
            {
                if (rightFiles.ContainsKey(name) || rightDirs.ContainsKey(name))
                {
                    (conflicts ??= new(rules.Match))[name] = FileTypeConflict.LeftLinkRightObject;
                }
            }
        }

        if (rightLinks is { Count: > 0 } && (leftFiles.Count > 0 || leftDirs.Count > 0))
        {
            foreach (var name in rightLinks)
            {
                if (leftFiles.ContainsKey(name) || leftDirs.ContainsKey(name))
                {
                    (conflicts ??= new(rules.Match))[name] = FileTypeConflict.RightLinkLeftObject;
                }
            }
        }

        return conflicts;
    }

    private static IEnumerable<string> CollectCaseCollisions(
        Dictionary<string, FileEntryData> leftFiles,
        Dictionary<string, FileEntryData> rightFiles,
        Dictionary<string, DirEntryData> leftDirs,
        Dictionary<string, DirEntryData> rightDirs,
        HashSet<string>? leftLinks,
        HashSet<string>? rightLinks)
    {
        IEnumerable<string> all = leftFiles.Keys
            .Concat(rightFiles.Keys)
            .Concat(leftDirs.Keys)
            .Concat(rightDirs.Keys);

        if (leftLinks is not null)
        {
            all = all.Concat(leftLinks);
        }

        if (rightLinks is not null)
        {
            all = all.Concat(rightLinks);
        }

        return all
            .Distinct(StringComparer.Ordinal)
            .GroupBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .Where(static x => x.Skip(1).Any())
            .SelectMany(static x => x)
            .ToArray();
    }

    private static void CompareFiles(
        DirectoryComparison comparison,
        Dictionary<string, FileEntryData> leftFiles,
        Dictionary<string, FileEntryData> rightFiles,
        Dictionary<string, FileTypeConflict>? typeConflicts,
        string relativePath,
        PathCaseRules rules)
    {
        if (leftFiles.Count == 0 && rightFiles.Count == 0 && typeConflicts is null)
        {
            return;
        }

        string[] ordered;

        if (rightFiles.Count == 0 && typeConflicts is null)
        {
            ordered = new string[leftFiles.Count];
            leftFiles.Keys.CopyTo(ordered, 0);
        }
        else if (leftFiles.Count == 0 && typeConflicts is null)
        {
            ordered = new string[rightFiles.Count];
            rightFiles.Keys.CopyTo(ordered, 0);
        }
        else
        {
            var allNames = new HashSet<string>(leftFiles.Count + rightFiles.Count + (typeConflicts?.Count ?? 0), rules.Match);
            allNames.UnionWith(leftFiles.Keys);
            allNames.UnionWith(rightFiles.Keys);

            if (typeConflicts is not null)
            {
                allNames.UnionWith(typeConflicts.Keys);
            }

            ordered = new string[allNames.Count];
            allNames.CopyTo(ordered);
        }

        Array.Sort(ordered, rules.Match);
        comparison.Files.EnsureCapacity(ordered.Length);

        for (var i = 0; i < ordered.Length; i++)
        {
            var fileName = ordered[i];
            var fileRelativePath = string.IsNullOrEmpty(relativePath)
                ? fileName
                : Path.Combine(relativePath, fileName);

            var hasLeft = leftFiles.TryGetValue(fileName, out var leftFile);
            var hasRight = rightFiles.TryGetValue(fileName, out var rightFile);
            var typeConflict = FileTypeConflict.None;
            typeConflicts?.TryGetValue(fileName, out typeConflict);

            comparison.Files.Add(CompareFile(fileName, fileRelativePath, hasLeft ? leftFile : null, hasRight ? rightFile : null, typeConflict));
        }
    }

    private static FileComparison CompareFile(
        string fileName,
        string relativePath,
        FileEntryData? leftFile,
        FileEntryData? rightFile,
        FileTypeConflict typeConflict)
    {
        var fileComparison = new FileComparison(fileName, relativePath);

        if (leftFile is { } left)
        {
            fileComparison.LeftSize = left.Length;
            fileComparison.LeftModified = left.LastWriteTime;
        }

        if (rightFile is { } right)
        {
            fileComparison.RightSize = right.Length;
            fileComparison.RightModified = right.LastWriteTime;
        }

        if (typeConflict != FileTypeConflict.None)
        {
            fileComparison.TypeConflict = typeConflict;
            fileComparison.Status = ComparisonStatus.Conflict;
            return fileComparison;
        }

        fileComparison.Status = (leftFile, rightFile) switch
        {
            ({ } l, { } r) => FilesIdentical(in l, in r) ? ComparisonStatus.Identical : ComparisonStatus.Modified,
            (not null, null) => ComparisonStatus.LeftOnly,
            _ => ComparisonStatus.RightOnly,
        };

        return fileComparison;
    }

    private void CompareSubDirectories(
        DirectoryComparison comparison,
        Dictionary<string, DirEntryData> leftDirs,
        Dictionary<string, DirEntryData> rightDirs,
        Dictionary<string, FileTypeConflict>? typeConflicts,
        string relativePath,
        PathCaseRules rules,
        SkipJournal skips,
        IProgress<OperationProgress>? progress,
        ref int processed,
        CancellationToken cancel)
    {
        if (leftDirs.Count == 0 && rightDirs.Count == 0)
        {
            return;
        }

        string[] ordered;

        if (rightDirs.Count == 0 && typeConflicts is null)
        {
            ordered = new string[leftDirs.Count];
            leftDirs.Keys.CopyTo(ordered, 0);
        }
        else if (leftDirs.Count == 0 && typeConflicts is null)
        {
            ordered = new string[rightDirs.Count];
            rightDirs.Keys.CopyTo(ordered, 0);
        }
        else
        {
            var allNames = new HashSet<string>(leftDirs.Count + rightDirs.Count, rules.Match);
            allNames.UnionWith(leftDirs.Keys);
            allNames.UnionWith(rightDirs.Keys);

            if (typeConflicts is not null)
            {
                allNames.ExceptWith(typeConflicts.Keys);
            }

            if (allNames.Count == 0)
            {
                return;
            }

            ordered = new string[allNames.Count];
            allNames.CopyTo(ordered);
        }

        Array.Sort(ordered, rules.Match);
        comparison.SubDirectories.EnsureCapacity(ordered.Length);

        for (var i = 0; i < ordered.Length; i++)
        {
            var dirName = ordered[i];
            var dirRelativePath = string.IsNullOrEmpty(relativePath)
                ? dirName
                : Path.Combine(relativePath, dirName);

            DirSide? leftSub = leftDirs.TryGetValue(dirName, out var leftEntry)
                ? new DirSide(leftEntry.FullPath, dirName, leftEntry.LastWriteTime, true)
                : null;

            DirSide? rightSub = rightDirs.TryGetValue(dirName, out var rightEntry)
                ? new DirSide(rightEntry.FullPath, dirName, rightEntry.LastWriteTime, true)
                : null;

            var subComparison = CompareDirectories(leftSub, rightSub, dirRelativePath, rules, skips, progress, ref processed, cancel);
            comparison.SubDirectories.Add(subComparison);
        }
    }

    private void EnumerateSide(
        DirSide? dir,
        StringComparer names,
        SkipJournal skips,
        CancellationToken cancel,
        out Dictionary<string, FileEntryData> files,
        out Dictionary<string, DirEntryData> dirs,
        out HashSet<string>? links,
        out bool incomplete)
    {
        files = new(names);
        dirs = new(names);
        links = null;

        if (dir is not { Exists: true } side)
        {
            incomplete = dir is not null;
            return;
        }

        incomplete = false;
        CompareEnumerator? enumerator = null;

        try
        {
            enumerator = new(side.FullName, exclusionFilter, logger, names, files, dirs, cancel);

            while (enumerator.MoveNext())
            {
                _ = enumerator.Current;
            }
        }
        catch (Exception ex) when (IsTraversalError(ex))
        {
            incomplete = true;
            skips.Report(ex, side.FullName);
        }
        finally
        {
            links = enumerator?.Links;
            enumerator?.Dispose();
        }
    }

    private readonly record struct DirSide(string FullName, string Name, DateTime LastWriteTime, bool Exists);

    private readonly record struct FileEntryData(long Length, DateTime LastWriteTime);

    private readonly record struct DirEntryData(string FullPath, DateTime LastWriteTime);

    private sealed class CompareEnumerator : FileSystemEnumerator<byte>
    {
        private readonly string _dirPath;
        private readonly ExclusionFilter _exclusionFilter;
        private readonly ILogger _logger;
        private readonly StringComparer _names;
        private readonly Dictionary<string, FileEntryData> _files;
        private readonly Dictionary<string, DirEntryData> _dirs;
        private readonly CancellationToken _cancel;

        public HashSet<string>? Links { get; private set; }

        public CompareEnumerator(
            string dirPath,
            ExclusionFilter exclusionFilter,
            ILogger logger,
            StringComparer names,
            Dictionary<string, FileEntryData> files,
            Dictionary<string, DirEntryData> dirs,
            CancellationToken cancel)
            : base(dirPath, Enumeration)
        {
            _dirPath = dirPath;
            _exclusionFilter = exclusionFilter;
            _logger = logger;
            _names = names;
            _files = files;
            _dirs = dirs;
            _cancel = cancel;
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry entry)
        {
            _cancel.ThrowIfCancellationRequested();

            var name = entry.FileName.ToString();

            if (_exclusionFilter.IsExcluded(name))
            {
                return false;
            }

            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                (Links ??= new(_names)).Add(name);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.CompareReparsePointSkipped(Path.Join(_dirPath, name));
                }

                return false;
            }

            if (entry.IsDirectory)
            {
                _dirs[name] = new(Path.Join(_dirPath, name), entry.LastWriteTimeUtc.LocalDateTime);
            }
            else
            {
                _files[name] = new(entry.Length, entry.LastWriteTimeUtc.LocalDateTime);
            }

            return false;
        }

        protected override byte TransformEntry(ref FileSystemEntry entry)
        {
            return 0;
        }
    }

    private sealed class SkipJournal(ILogger logger)
    {
        private readonly HashSet<string> _paths = new(StringComparer.Ordinal);

        public void Report(Exception exception, string path)
        {
            if (!_paths.Add(path))
            {
                return;
            }

            if (_paths.Count == 1)
            {
                logger.CompareDirectorySkipped(exception, path);
            }
            else
            {
                logger.CompareDirectorySkippedAgain(path, exception.GetType().Name, exception.Message);
            }
        }

        public void ReportTotal()
        {
            if (_paths.Count > 0)
            {
                logger.CompareDirectoriesSkippedTotal(_paths.Count);
            }
        }
    }
}
