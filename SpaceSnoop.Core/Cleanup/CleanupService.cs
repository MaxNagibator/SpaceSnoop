using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.Security;

namespace SpaceSnoop.Core.Cleanup;

public readonly record struct CleanupMeasurement(
    long Bytes,
    int Files,
    IReadOnlyList<string> Unreadable,
    CleanupAvailability Availability);

public readonly record struct CleanupReport(
    long FreedBytes,
    int Deleted,
    int Skipped,
    IReadOnlyList<string> Errors,
    bool Cancelled = false);

public sealed class CleanupService(ILogger<CleanupService>? logger = null)
{
    private const int MaxReportedErrors = 50;

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
        var availability = Availability(target);

        if (target.Kind == CleanupTargetKind.RecycleBin)
        {
            var content = RecycleBin.Query();

            return new(content.Bytes, (int)Math.Min(content.Items, int.MaxValue), [], availability);
        }

        if (availability != CleanupAvailability.Available)
        {
            return new(0, 0, [], availability);
        }

        long total = 0;
        var count = 0;
        var unreadable = new List<string>();

        Walk(target, token, file =>
        {
            total += file.Length;
            count++;
        }, unreadable, null);

        return new(total, count, unreadable, availability);
    }

    public CleanupReport Clean(CleanupTarget target, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var availability = Availability(target);

        if (availability != CleanupAvailability.Available)
        {
            _logger.CleanupTargetUnavailable(target.Id, availability);

            return new(0, 0, 1, [Describe(target, availability)]);
        }

        _logger.CleanupStarted(target.Id);

        var report = target.Kind == CleanupTargetKind.RecycleBin
            ? EmptyRecycleBin(progress, token)
            : CleanDirectory(target, progress, token);

        _logger.CleanupFinished(target.Id, report.Deleted, report.FreedBytes);

        return report;
    }

    private static CleanupAvailability Availability(CleanupTarget target)
    {
        if (target.Kind == CleanupTargetKind.RecycleBin)
        {
            return RecycleBin.Query().Ok ? CleanupAvailability.Available : CleanupAvailability.Failed;
        }

        if (!Directory.Exists(target.Path))
        {
            return CleanupAvailability.Missing;
        }

        if (!target.Supported)
        {
            return CleanupAvailability.Unsupported;
        }

        if (!IsSafeRoot(target.Path))
        {
            return CleanupAvailability.Unsafe;
        }

        try
        {
            using var probe = new DirectoryInfo(target.Path).EnumerateFileSystemInfos().GetEnumerator();
            probe.MoveNext();
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            return CleanupAvailability.NeedsAdmin;
        }
        catch (IOException)
        {
            return CleanupAvailability.Missing;
        }

        return CleanupAvailability.Available;
    }

    private static string Describe(CleanupTarget target, CleanupAvailability availability)
    {
        var reason = availability switch
        {
            CleanupAvailability.Missing => "каталога нет",
            CleanupAvailability.NeedsAdmin => "нужны права администратора",
            CleanupAvailability.Unsupported => "очистка не поддерживается",
            CleanupAvailability.Unsafe => "путь ведёт в корень тома или наружу по ссылке",
            CleanupAvailability.Failed => "не удалось опросить",
            _ => "цель недоступна",
        };

        return $"«{target.Name}»: {reason}";
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

        return now - touched < minimumAge;
    }

    private void Walk(
        CleanupTarget target,
        CancellationToken token,
        Action<FileInfo> onFile,
        List<string> unreadable,
        List<string>? directories)
    {
        var now = DateTime.UtcNow;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new(target.Path));

        while (stack.Count > 0)
        {
            token.ThrowIfCancellationRequested();

            var directory = stack.Pop();

            try
            {
                foreach (var file in directory.GetFiles(target.SearchPattern))
                {
                    if (IsReparsePoint(file) || IsFresh(file, now, target.MinimumAge))
                    {
                        continue;
                    }

                    onFile(file);
                }

                if (!target.Recursive)
                {
                    continue;
                }

                foreach (var sub in directory.GetDirectories())
                {
                    if (IsReparsePoint(sub))
                    {
                        continue;
                    }

                    stack.Push(sub);
                    directories?.Add(sub.FullName);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.CleanupDirectorySkipped(exception, directory.FullName);
                unreadable.Add(directory.FullName);
            }
        }
    }

    private CleanupReport EmptyRecycleBin(IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var content = RecycleBin.Query();

        if (!content.Ok)
        {
            return new(0, 0, 1, ["Корзина: не удалось опросить"]);
        }

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

    private CleanupReport CleanDirectory(CleanupTarget target, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var files = new List<FileInfo>();
        var directories = new List<string>();
        var unreadable = new List<string>();
        var errors = new List<string>();

        Walk(target, token, files.Add, unreadable, directories);

        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var cancelled = false;

        var now = DateTime.UtcNow;

        foreach (var file in files)
        {
            if (token.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            if (Changed(file, now, target.MinimumAge))
            {
                skipped++;

                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add($"«{file.FullName}»: файл изменился после обхода");
                }

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

                if (errors.Count < MaxReportedErrors)
                {
                    errors.Add($"«{file.FullName}»: {exception.Message}");
                }

                continue;
            }

            freed += length;
            deleted++;
            progress?.Report(new(deleted, Relative(target.Path, file.FullName), freed));
        }

        cancelled |= token.IsCancellationRequested;

        if (!cancelled)
        {
            RemoveEmptyDirectories(directories);
        }

        foreach (var path in unreadable)
        {
            skipped++;

            if (errors.Count < MaxReportedErrors)
            {
                errors.Add($"«{path}»: каталог не читается");
            }
        }

        return new(freed, deleted, skipped, errors, cancelled);
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

    private void RemoveEmptyDirectories(List<string> directories)
    {
        directories.Sort(static (left, right) => right.Length.CompareTo(left.Length));

        foreach (var directory in directories)
        {
            try
            {
                Directory.Delete(directory, false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                _logger.CleanupDirectorySkipped(exception, directory);
            }
        }
    }

    private static string Relative(string root, string path)
    {
        return Path.GetRelativePath(root, path);
    }
}
