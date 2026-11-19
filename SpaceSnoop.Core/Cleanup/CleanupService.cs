using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security;

namespace SpaceSnoop.Core.Cleanup;

public readonly record struct CleanupMeasurement(
    long Bytes,
    int Files,
    IReadOnlyList<string> Unreadable,
    CleanupAvailability Availability,
    string? ResolvedPath = null);

public readonly record struct CleanupReport(
    long FreedBytes,
    int Deleted,
    int Skipped,
    IReadOnlyList<string> Errors,
    bool Cancelled = false);

public sealed class CleanupService(ILogger<CleanupService>? logger = null)
{
    private readonly ILogger<CleanupService> _logger = logger ?? NullLogger<CleanupService>.Instance;

    public Task<CleanupMeasurement> MeasureAsync(CleanupTarget target, CancellationToken token)
    {
        return Task.Run(() => Measure(target, token), token);
    }

    public Task<CleanupReport> CleanAsync(CleanupTarget target, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        return Task.Run(() => Clean(target, progress, token), token);
    }

    public CleanupMeasurement Measure(CleanupTarget target, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var resolution = Resolve(target);

        if (target.Kind == CleanupTargetKind.RecycleBin)
        {
            return new(resolution.RecycleBin.Bytes, (int)Math.Min(resolution.RecycleBin.Items, int.MaxValue), [], resolution.Availability);
        }

        if (resolution.Availability is not (CleanupAvailability.Available or CleanupAvailability.Unsupported))
        {
            return new(0, 0, [], resolution.Availability, resolution.ResolvedPath);
        }

        long total = 0;
        var count = 0;
        var unreadable = new List<string>(resolution.UnreadableRoots);

        Walk(
            target,
            resolution.ReadableRoots,
            token,
            (_, file) =>
            {
                total += file.Length;
                count++;
            },
            unreadable,
            null);

        return new(total, count, unreadable, resolution.Availability, resolution.ResolvedPath);
    }

    public CleanupReport Clean(CleanupTarget target, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var resolution = Resolve(target);

        if (resolution.Availability != CleanupAvailability.Available)
        {
            _logger.CleanupTargetUnavailable(target.Id, resolution.Availability);

            return new(0, 0, 1, [Describe(target, resolution.Availability)]);
        }

        _logger.CleanupStarted(target.Id);

        var report = target.Kind == CleanupTargetKind.RecycleBin
            ? EmptyRecycleBin(resolution.RecycleBin, progress, token)
            : CleanDirectory(target, resolution, progress, token);

        _logger.CleanupFinished(target.Id, report.Deleted, report.FreedBytes);

        return report;
    }

    private TargetResolution Resolve(CleanupTarget target)
    {
        if (target.Kind == CleanupTargetKind.RecycleBin)
        {
            var content = RecycleBin.Query();
            var availability = content.Ok ? CleanupAvailability.Available : CleanupAvailability.Failed;

            return new(availability, content, [], [], string.Empty);
        }

        var candidates = target.Paths;

        if (candidates.Count == 0)
        {
            return new(CleanupAvailability.Missing, default, [], [], target.Path);
        }

        var existing = new List<string>(candidates.Count);
        var canonicals = new List<string>(candidates.Count);

        foreach (var path in candidates)
        {
            if (SyncRoots.Normalize(path) is not { } canonical)
            {
                return new(CleanupAvailability.Unsafe, default, [], [], target.Path);
            }

            var duplicate = false;

            foreach (var seen in canonicals)
            {
                if (SyncRoots.SamePath(seen, canonical))
                {
                    duplicate = true;
                    break;
                }

                if (SyncRoots.Nested(seen, canonical))
                {
                    return new(CleanupAvailability.Unsafe, default, [], [], path);
                }
            }

            if (duplicate)
            {
                continue;
            }

            canonicals.Add(canonical);

            if (!Directory.Exists(path))
            {
                continue;
            }

            if (!IsSafeRoot(path))
            {
                return new(CleanupAvailability.Unsafe, default, [], [], path);
            }

            existing.Add(path);
        }

        if (existing.Count == 0)
        {
            return new(CleanupAvailability.Missing, default, [], [], target.Path);
        }

        var readable = new List<string>(existing.Count);
        var unreadable = new List<string>();
        var needsAdmin = false;

        foreach (var path in existing)
        {
            try
            {
                using var probe = new DirectoryInfo(path).EnumerateFileSystemInfos().GetEnumerator();
                probe.MoveNext();
                readable.Add(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                if (exception is UnauthorizedAccessException or SecurityException)
                {
                    needsAdmin = true;
                }

                if (unreadable.Count < CleanupDefaults.MaxReportedErrors)
                {
                    _logger.CleanupDirectorySkipped(exception, path);
                }

                unreadable.Add(path);
            }
        }

        if (readable.Count == 0 && needsAdmin)
        {
            return new(CleanupAvailability.NeedsAdmin, default, [], unreadable, existing[0]);
        }

        var resolved = target.Supported ? CleanupAvailability.Available : CleanupAvailability.Unsupported;
        var primary = readable.Count > 0 ? readable[0] : existing[0];

        return new(resolved, default, readable, unreadable, primary);
    }

    private static string Describe(CleanupTarget target, CleanupAvailability availability)
    {
        return $"«{target.Name}»: {CleanupText.Availability(availability)}";
    }

    private static bool IsSafeRoot(string path)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));

            if (directory.Parent is null)
            {
                return false;
            }

            for (var current = directory; current is not null; current = current.Parent)
            {
                if (IsReparsePoint(current))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine(exception);
            return false;
        }
    }

    private static bool IsReparsePoint(FileSystemInfo info)
    {
        return (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    private static bool IsFresh(FileInfo file, DateTime now, TimeSpan minimumAge)
    {
        if (minimumAge <= TimeSpan.Zero)
        {
            return false;
        }

        var touched = file.LastWriteTimeUtc > file.CreationTimeUtc ? file.LastWriteTimeUtc : file.CreationTimeUtc;

        return (now - touched) < minimumAge;
    }

    private void Walk(
        CleanupTarget target,
        IReadOnlyList<string> roots,
        CancellationToken token,
        Action<string, FileInfo> onFile,
        List<string> unreadable,
        List<string>? directories)
    {
        var now = DateTime.UtcNow;
        var stack = new Stack<DirectoryInfo>();

        foreach (var root in roots)
        {
            stack.Push(new(root));

            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();

                var directory = stack.Pop();

                try
                {
                    ReadDirectory(directory, target, now, x => onFile(root, x), stack, directories, token);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
                {
                    if (unreadable.Count < CleanupDefaults.MaxReportedErrors)
                    {
                        _logger.CleanupDirectorySkipped(exception, directory.FullName);
                    }

                    unreadable.Add(directory.FullName);
                }
            }
        }
    }

    private static void ReadDirectory(
        DirectoryInfo directory,
        CleanupTarget target,
        DateTime now,
        Action<FileInfo> onFile,
        Stack<DirectoryInfo> stack,
        List<string>? directories,
        CancellationToken token)
    {
        foreach (var file in directory.EnumerateFiles(target.SearchPattern))
        {
            token.ThrowIfCancellationRequested();

            if (IsReparsePoint(file) || IsFresh(file, now, target.MinimumAge))
            {
                continue;
            }

            onFile(file);
        }

        if (!target.Recursive)
        {
            return;
        }

        foreach (var sub in directory.EnumerateDirectories())
        {
            token.ThrowIfCancellationRequested();

            if (IsReparsePoint(sub))
            {
                continue;
            }

            stack.Push(sub);
            directories?.Add(sub.FullName);
        }
    }

    private CleanupReport EmptyRecycleBin(RecycleBinContent content, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var items = (int)Math.Min(content.Items, int.MaxValue);

        if (items == 0)
        {
            return new(0, 0, 0, []);
        }

        if (token.IsCancellationRequested)
        {
            return new(0, 0, 0, [], true);
        }

        try
        {
            RecycleBin.Empty();
        }
        catch (IOException exception)
        {
            _logger.CleanupFileFailed(exception, "Корзина");

            return new(0, 0, items, [exception.Message]);
        }

        progress?.Report(new(items, "Корзина", content.Bytes));

        return new(content.Bytes, items, 0, []);
    }

    private CleanupReport CleanDirectory(
        CleanupTarget target,
        TargetResolution resolution,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var files = new List<WalkedFile>();
        var directories = new List<string>();
        var unreadable = new List<string>(resolution.UnreadableRoots);
        var errors = new List<string>();

        try
        {
            Walk(target, resolution.ReadableRoots, token, (root, file) => files.Add(new(root, file)), unreadable, directories);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new(0, 0, ReportUnreadable(unreadable, errors), errors, true);
        }

        var (freed, deleted, skipped, cancelled) = DeleteFiles(target, files, errors, progress, token);

        cancelled |= token.IsCancellationRequested;

        if (!cancelled)
        {
            var (dirSkipped, dirCancelled) = RemoveEmptyDirectories(directories, errors, token);

            skipped += dirSkipped;
            cancelled |= dirCancelled;
        }

        skipped += ReportUnreadable(unreadable, errors);

        return new(freed, deleted, skipped, errors, cancelled);
    }

    private static int ReportUnreadable(List<string> unreadable, List<string> errors)
    {
        foreach (var path in unreadable)
        {
            Report(errors, $"«{path}»: каталог не читается");
        }

        return unreadable.Count;
    }

    private (long Freed, int Deleted, int Skipped, bool Cancelled) DeleteFiles(
        CleanupTarget target,
        List<WalkedFile> files,
        List<string> errors,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var now = DateTime.UtcNow;

        foreach (var (root, file) in files)
        {
            if (token.IsCancellationRequested)
            {
                return (freed, deleted, skipped, true);
            }

            if (Changed(file, now, target.MinimumAge))
            {
                skipped++;
                Report(errors, $"«{file.FullName}»: файл изменился после обхода");
                continue;
            }

            var length = file.Length;

            try
            {
                File.Delete(file.FullName);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.CleanupFileFailed(exception, file.FullName);
                skipped++;
                Report(errors, $"«{file.FullName}»: {exception.Message}");
                continue;
            }

            freed += length;
            deleted++;
            progress?.Report(new(deleted, Relative(root, file.FullName), freed));
        }

        return (freed, deleted, skipped, false);
    }

    private static void Report(List<string> errors, string message)
    {
        if (errors.Count < CleanupDefaults.MaxReportedErrors)
        {
            errors.Add(message);
        }
    }

    // TODO: подмену на другой файл с тем же размером и mtime здесь не отличить – закрывается удалением по дескриптору, заводить при появлении цели в недоверенном каталоге
    private static bool Changed(FileInfo file, DateTime now, TimeSpan minimumAge)
    {
        try
        {
            var fresh = new FileInfo(file.FullName);

            return !fresh.Exists
                || IsReparsePoint(fresh)
                || IsFresh(fresh, now, minimumAge)
                || fresh.Length != file.Length
                || fresh.LastWriteTimeUtc != file.LastWriteTimeUtc;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            Debug.WriteLine(exception);
            return true;
        }
    }

    private (int Skipped, bool Cancelled) RemoveEmptyDirectories(List<string> directories, List<string> errors, CancellationToken token)
    {
        directories.Sort(static (left, right) => right.Length.CompareTo(left.Length));

        var reported = 0;
        var skipped = 0;

        foreach (var directory in directories)
        {
            if (token.IsCancellationRequested)
            {
                return (skipped, true);
            }

            try
            {
                if (Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory, false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                skipped++;

                if (reported++ < CleanupDefaults.MaxReportedErrors)
                {
                    _logger.CleanupDirectorySkipped(exception, directory);
                }

                Report(errors, $"«{directory}»: каталог не удалён – {exception.Message}");
            }
        }

        return (skipped, token.IsCancellationRequested);
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path);
    }

    private readonly record struct TargetResolution(
        CleanupAvailability Availability,
        RecycleBinContent RecycleBin,
        IReadOnlyList<string> ReadableRoots,
        IReadOnlyList<string> UnreadableRoots,
        string ResolvedPath);

    private readonly record struct WalkedFile(string Root, FileInfo File);
}
