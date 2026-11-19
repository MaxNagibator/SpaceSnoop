using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32.SafeHandles;
using System.Security;

namespace SpaceSnoop.Core.Cleanup;

public sealed class NuGetCleanupService
{
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Func<CancellationToken, Task<NuGetCacheLocations>> _discover;
    private readonly ILogger<NuGetCleanupService> _logger;

    public NuGetCleanupService(string logsDirectory, ILogger<NuGetCleanupService>? logger = null)
        : this(x => NuGetCacheDiscovery.DiscoverAsync(logsDirectory, x), logger)
    {
    }

    internal NuGetCleanupService(Func<CancellationToken, Task<NuGetCacheLocations>> discover, ILogger<NuGetCleanupService>? logger = null)
    {
        _discover = discover;
        _logger = logger ?? NullLogger<NuGetCleanupService>.Instance;
    }

    public async Task<NuGetCleanupPlan> ScanAsync(CancellationToken token)
    {
        var locations = await _discover(token).ConfigureAwait(false);
        return await Task.Run(
                () =>
                {
                    var scans = new List<NuGetCacheScan>();
                    foreach (var kind in NuGetCacheDiscovery.Names.Keys)
                    {
                        token.ThrowIfCancellationRequested();
                        var path = locations.Paths[kind];
                        var files = new List<CacheFileStamp>();
                        var errors = new List<string>();
                        var warnings = new List<string>();
                        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        CacheFileStamp root = default;
                        try
                        {
                            ValidateRoot(path, locations.Paths.Values);
                            try
                            {
                                File.GetAttributes(path);
                            }
                            catch (Exception missing) when (missing is DirectoryNotFoundException or FileNotFoundException)
                            {
                                scans.Add(new(kind, path, root, files.AsReadOnly(), errors.AsReadOnly(), false));
                                _logger.NuGetCacheScanCompleted(kind.ToString(), path, 0, 0, 0, 0);
                                continue;
                            }

                            using var lease = CacheFileAccess.LockDirectory(path);
                            root = lease.Stamp;
                            Walk(path, kind, files, errors, warnings, excluded, token);
                            if (kind == NuGetCacheKind.GlobalPackages && errors.Count == 0)
                            {
                                ValidatePackages(path, files, warnings, excluded);
                            }
                        }
                        catch (Exception exception) when (IsFileError(exception))
                        {
                            AddError(errors, exception.Message);
                            _logger.NuGetCacheScanFailed(exception, kind.ToString(), path, false);
                        }

                        if (excluded.Count > 0)
                        {
                            files.RemoveAll(file => excluded.Contains(PackageRoot(path, file.Path)!));
                        }

                        var scan = new NuGetCacheScan(
                            kind,
                            path,
                            root,
                            files.AsReadOnly(),
                            errors.AsReadOnly(),
                            warnings: warnings.AsReadOnly(),
                            excludedPackages: excluded.Count);
                        scans.Add(scan);
                        _logger.NuGetCacheScanCompleted(kind.ToString(), path, scan.Files, scan.Bytes, errors.Count, excluded.Count);
                    }

                    return new NuGetCleanupPlan(_owner, Fingerprint(locations), scans.AsReadOnly());
                },
                token)
            .ConfigureAwait(false);
    }

    public async Task<CleanupReport> CleanAsync(
        NuGetCleanupPlan plan,
        IReadOnlyCollection<NuGetCacheKind> selected,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        if (plan.Owner != _owner || Interlocked.Exchange(ref plan.Used, 1) != 0)
        {
            return new(0, 0, 0, ["План очистки уже использован или принадлежит другому сеансу. Повторите сканирование."]);
        }

        if ((DateTime.UtcNow - plan.CreatedUtc) > CleanupDefaults.PlanLifetime)
        {
            return new(0, 0, 0, ["Замер устарел. Повторите сканирование."]);
        }

        try
        {
            var locations = await _discover(token).ConfigureAwait(false);
            if (!string.Equals(plan.Context, Fingerprint(locations), StringComparison.Ordinal))
            {
                return new(0, 0, 0, ["Пользователь или пути NuGet изменились после замера. Повторите сканирование."]);
            }

            var caches = plan.Caches.Where(cache => selected.Contains(cache.Kind)).ToArray();
            if (caches.Length == 0 || caches.Length != selected.Distinct().Count() || caches.Any(cache => !cache.CanClean))
            {
                return new(0, 0, 0, ["Для очистки выбраны недоступные или пустые категории."]);
            }

            _logger.NuGetCleanupStarted(caches.Length);
            var report = await Task.Run(() => Clean(caches, locations, progress, token), CancellationToken.None).ConfigureAwait(false);
            _logger.NuGetCleanupCompleted(report.Deleted, report.FreedBytes, report.Skipped, report.Errors.Count, report.Cancelled);
            return report;
        }
        catch (OperationCanceledException)
        {
            return new(0, 0, 0, [], true);
        }
        catch (Exception exception) when (IsFileError(exception) || exception is InvalidOperationException or TimeoutException)
        {
            _logger.NuGetCleanupFailed(exception);
            return new(0, 0, 0, [exception.Message]);
        }
    }

    internal static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path)
            || path.Length < 3
            || path[1] != ':'
            || path[2] != '\\'
            || path.AsSpan(2).Contains(':'))
        {
            throw new IOException("NuGet вернул не локальный абсолютный путь.");
        }

        if (path.Split(Path.DirectorySeparatorChar).Any(x => x.EndsWith(' ') || x.EndsWith('.')))
        {
            throw new IOException("Путь кэша содержит неоднозначное имя с точкой или пробелом в конце.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static CleanupReport Clean(
        IReadOnlyList<NuGetCacheScan> caches,
        NuGetCacheLocations locations,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var totals = new CleanupTotals();

        foreach (var cache in caches)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            try
            {
                ValidateRoot(cache.Path, locations.Paths.Values);
                using var rootLease = CacheFileAccess.LockDirectory(cache.Path);
                if (!SameIdentity(cache.Root, rootLease.Stamp))
                {
                    throw new IOException($"«{cache.Path}»: каталог заменён после замера.");
                }

                var groups = cache.Kind == NuGetCacheKind.GlobalPackages
                    ? cache.Entries.GroupBy(file => PackageDirectory(cache.Path, file.Path), StringComparer.OrdinalIgnoreCase)
                    : cache.Entries.GroupBy(_ => cache.Path, StringComparer.OrdinalIgnoreCase);

                foreach (var group in groups)
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        CleanGroup(group, cache.Kind, totals, progress, token);
                    }
                    catch (Exception exception) when (IsFileError(exception))
                    {
                        totals.Failed(exception, group.Count());
                    }
                }
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                totals.Failed(exception, cache.Files);
            }
        }

        return new(totals.Bytes, totals.Deleted, totals.Skipped, totals.Errors.AsReadOnly(), token.IsCancellationRequested);
    }

    private static void CleanGroup(
        IGrouping<string, CacheFileStamp> group,
        NuGetCacheKind kind,
        CleanupTotals totals,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        using var packageLease = kind == NuGetCacheKind.GlobalPackages ? CacheFileAccess.LockDirectory(group.Key) : null;
        var markerPath = kind == NuGetCacheKind.GlobalPackages ? Path.Combine(group.Key, ".nupkg.metadata") : null;
        using var markerHandle = RemoveMarker(group, markerPath, totals);
        foreach (var directory in group.Where(x => !string.Equals(x.Path, markerPath, StringComparison.OrdinalIgnoreCase))
            .GroupBy(x => Path.GetDirectoryName(x.Path)!))
        {
            if (token.IsCancellationRequested)
            {
                return;
            }

            try
            {
                using var parent = CacheFileAccess.LockDirectory(directory.Key);
                foreach (var file in directory)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    DeleteFile(file, totals, progress);
                }
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                totals.Failed(exception, directory.Count());
            }
        }
    }

    private static SafeFileHandle? RemoveMarker(IEnumerable<CacheFileStamp> group, string? path, CleanupTotals totals)
    {
        if (path is null)
        {
            return null;
        }

        var marker = group.Single(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase));
        var handle = OpenUnchanged(marker);
        try
        {
            CacheFileAccess.Delete(handle, path);
            totals.Bytes += marker.Length;
            totals.Deleted++;
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void DeleteFile(CacheFileStamp file, CleanupTotals totals, IProgress<OperationProgress>? progress)
    {
        try
        {
            using var handle = OpenUnchanged(file);
            CacheFileAccess.Delete(handle, file.Path);
            totals.Bytes += file.Length;
            totals.Deleted++;
            progress?.Report(new(totals.Deleted, file.Path, totals.Bytes));
        }
        catch (Exception exception) when (IsFileError(exception))
        {
            totals.Failed(exception, 1);
        }
    }

    private static SafeFileHandle OpenUnchanged(CacheFileStamp file)
    {
        var handle = CacheFileAccess.OpenFile(file.Path, true);
        try
        {
            if (CacheFileAccess.Read(handle, file.Path) != file)
            {
                throw new IOException($"«{file.Path}»: файл изменился после замера.");
            }

            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private void Walk(
        string root,
        NuGetCacheKind kind,
        List<CacheFileStamp> files,
        List<string> errors,
        List<string> warnings,
        HashSet<string> excluded,
        CancellationToken token)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.TryPop(out var path))
        {
            token.ThrowIfCancellationRequested();
            if (excluded.Count > 0 && excluded.Contains(PackageRoot(root, path)!))
            {
                continue;
            }

            try
            {
                using var lease = CacheFileAccess.LockDirectory(path);
                foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos(
                    "*",
                    new EnumerationOptions
                    {
                        AttributesToSkip = 0,
                        IgnoreInaccessible = false,
                    }))
                {
                    token.ThrowIfCancellationRequested();
                    if (excluded.Count > 0 && excluded.Contains(PackageRoot(root, path)!))
                    {
                        break;
                    }

                    try
                    {
                        if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new IOException($"«{entry.FullName}»: ссылка или рабочий репозиторий внутри кэша.");
                        }

                        if ((entry.Attributes & FileAttributes.Directory) != 0)
                        {
                            directories.Push(entry.FullName);
                            continue;
                        }

                        if (kind != NuGetCacheKind.GlobalPackages && IsProjectFile(entry.Name))
                        {
                            throw new IOException($"«{entry.FullName}»: рабочие проекты не очищаются.");
                        }

                        using var handle = CacheFileAccess.OpenFile(entry.FullName, false);
                        files.Add(CacheFileAccess.Read(handle, entry.FullName));
                    }
                    catch (Exception exception) when (IsFileError(exception))
                    {
                        RecordFailure(entry.Name.Equals(".git", StringComparison.OrdinalIgnoreCase) ? path : entry.FullName, exception);
                    }
                }
            }
            catch (Exception exception) when (IsFileError(exception))
            {
                RecordFailure(path, exception);
            }
        }

        void RecordFailure(string path, Exception exception)
        {
            var package = kind == NuGetCacheKind.GlobalPackages ? PackageRoot(root, path) : null;
            if (package is null)
            {
                AddError(errors, exception.Message);
                _logger.NuGetCacheScanFailed(exception, kind.ToString(), path, false);
            }
            else
            {
                ExcludePackage(package, exception, warnings, excluded);
            }
        }
    }

    private static void ValidateRoot(string path, IEnumerable<string> paths)
    {
        if (Normalize(path) != path || new DirectoryInfo(path).Parent is null)
        {
            throw new IOException($"«{path}»: небезопасный корень кэша.");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var protectedPaths = CleanupPaths.ProtectedRoots();

        if (string.Equals(path, home, StringComparison.OrdinalIgnoreCase)
            || protectedPaths.Where(item => !string.IsNullOrEmpty(item)).Any(item => Overlap(path, Normalize(item)))
            || paths.Count(other => Overlap(path, Normalize(other))) != 1)
        {
            throw new IOException($"«{path}»: кэш пересекается с защищённым каталогом или другой категорией.");
        }
    }

    private static bool Overlap(string left, string right)
    {
        return Inside(left, right) || Inside(right, left);
    }

    private static bool Inside(string path, string root)
    {
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameIdentity(CacheFileStamp left, CacheFileStamp right)
    {
        return left.Volume == right.Volume && left.Id == right.Id;
    }

    private static bool IsProjectFile(string name)
    {
        var extension = Path.GetExtension(name);
        return extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
            || new[]
            {
                ".sln", ".slnx", ".cs", ".fs", ".vb",
            }.Contains(extension, StringComparer.OrdinalIgnoreCase)
            || new[]
            {
                "global.json", "Directory.Build.props", "Directory.Build.targets",
            }.Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    private static string Fingerprint(NuGetCacheLocations locations)
    {
        return locations.Context
            + "|"
            + string.Join("|", NuGetCacheDiscovery.Names.Keys.Select(kind => locations.Paths[kind]));
    }

    private static string PackageDirectory(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        if (segments.Length < 3)
        {
            throw new IOException($"«{path}»: неизвестная структура global-packages.");
        }

        return Path.Combine(root, segments[0], segments[1]);
    }

    private static string? PackageRoot(string root, string path)
    {
        var segments = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
        return segments.Length >= 2 ? Path.Combine(root, segments[0], segments[1]) : null;
    }

    private void ValidatePackages(string root, IEnumerable<CacheFileStamp> files, List<string> warnings, HashSet<string> excluded)
    {
        foreach (var group in files.GroupBy(file => PackageDirectory(root, file.Path), StringComparer.OrdinalIgnoreCase))
        {
            if (excluded.Contains(group.Key))
            {
                continue;
            }

            var directory = new DirectoryInfo(group.Key);
            var id = directory.Parent!.Name;
            var names = group.Where(file => string.Equals(Path.GetDirectoryName(file.Path), group.Key, StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileName(file.Path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!names.Contains(".nupkg.metadata")
                || !names.Contains(id + ".nuspec")
                || !names.Contains($"{id}.{directory.Name}.nupkg"))
            {
                ExcludePackage(
                    group.Key,
                    new IOException($"«{group.Key}»: пакет не завершён или структура не подтверждена."),
                    warnings,
                    excluded);
            }
        }
    }

    private void ExcludePackage(string path, Exception exception, List<string> warnings, HashSet<string> excluded)
    {
        if (excluded.Add(path))
        {
            AddError(warnings, $"Пакет «{path}» исключён из очистки: {exception.Message}");
            _logger.NuGetCacheScanFailed(exception, NuGetCacheKind.GlobalPackages.ToString(), path, true);
        }
    }

    private static bool IsFileError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    }

    private static void AddError(List<string> errors, string error)
    {
        if (errors.Count < CleanupDefaults.MaxReportedErrors)
        {
            errors.Add(error);
        }
    }

    private sealed class CleanupTotals
    {
        internal long Bytes;
        internal int Deleted;
        internal int Skipped;
        internal List<string> Errors { get; } = [];

        internal void Failed(Exception exception, int count)
        {
            Skipped += count;
            AddError(Errors, exception.Message);
        }
    }
}
