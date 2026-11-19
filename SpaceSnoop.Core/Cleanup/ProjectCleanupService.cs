using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;

namespace SpaceSnoop.Core.Cleanup;

public sealed class ProjectCleanupService
{
    private static readonly EnumerationOptions Entries = new()
    {
        AttributesToSkip = 0,
        IgnoreInaccessible = false,
    };

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Action<string> _recycle;
    private readonly ILogger<ProjectCleanupService> _logger;

    public ProjectCleanupService(ILogger<ProjectCleanupService>? logger = null) : this(ProjectRecycleBin.Delete, logger)
    {
    }

    internal ProjectCleanupService(Action<string> recycle, ILogger<ProjectCleanupService>? logger = null)
    {
        _recycle = recycle;
        _logger = logger ?? NullLogger<ProjectCleanupService>.Instance;
    }

    public Task<ProjectCleanupPlan> ScanAsync(string root, CancellationToken token)
    {
        return ScanAsync([root], [], token);
    }

    public Task<ProjectCleanupPlan> ScanAsync(
        IReadOnlyCollection<string> roots,
        IReadOnlyCollection<string> customOutputs,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(customOutputs);
        var rootSnapshot = roots.ToArray();
        var outputSnapshot = customOutputs.ToArray();
        return Task.Run(() => Scan(rootSnapshot, outputSnapshot, token), token);
    }

    public Task<CleanupReport> CleanAsync(
        ProjectCleanupPlan plan,
        IReadOnlyCollection<string> selected,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(selected);
        if (plan.Owner != _owner
            || Interlocked.Exchange(ref plan.Used, 1) != 0
            || (DateTime.UtcNow - plan.CreatedUtc) > CleanupDefaults.PlanLifetime)
        {
            return Task.FromResult(new CleanupReport(0, 0, 0, ["План устарел, уже использован или принадлежит другому сеансу. Повторите поиск."]));
        }

        var targets = plan.Artifacts.Where(x => selected.Contains(x.Id, StringComparer.Ordinal)
                || (selected.Contains(x.Path, StringComparer.Ordinal)
                    && plan.Artifacts.Count(other => string.Equals(other.Path, x.Path, StringComparison.Ordinal)) == 1))
            .ToArray();

        if (targets.Length == 0
            || targets.Length != selected.Distinct(StringComparer.Ordinal).Count()
            || targets.Any(x => !x.CanClean))
        {
            return Task.FromResult(new CleanupReport(0, 0, 0, ["Выберите доступные непустые каталоги из просмотренного плана."]));
        }

        var completion = new TaskCompletionSource<CleanupReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Clean(plan, targets, progress, token));
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        })
        {
            IsBackground = true,
            Name = "SpaceSnoop project cleanup",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    internal static bool ContainsApplicationFiles(ProjectArtifactScan target, string appDirectory)
    {
        return Inside(target.Path, appDirectory)
            || (target.Kind != ProjectArtifactKind.Coverage && Inside(appDirectory, target.Path))
            || target.Entries.Any(file => Inside(file.Path, appDirectory));
    }

    private ProjectCleanupPlan Scan(string[] rootSnapshot, string[] outputSnapshot, CancellationToken token)
    {
        var roots = CollapseRoots(rootSnapshot);
        var outputs = CollapseRoots(outputSnapshot);
        if (roots.Count == 0 && outputs.Count == 0)
        {
            throw new IOException("Добавьте хотя бы один корень проектов или output-каталог.");
        }

        var artifacts = new List<ProjectArtifactScan>();
        var errors = new List<string>();
        var leases = new List<CacheFileAccess.DirectoryLease>();
        try
        {
            foreach (var path in roots.Concat(outputs).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                leases.Add(CacheFileAccess.LockDirectory(path));
            }

            var scanOutputs = outputs.Where(output => !roots.Any(root => Inside(root, output))).ToArray();
            foreach (var output in outputs.Except(scanOutputs, StringComparer.OrdinalIgnoreCase))
            {
                AddError(errors, $"«{output}»: output-каталог содержит корень поиска. Выберите отдельный каталог результатов.");
            }

            foreach (var root in roots)
            {
                Discover(root, scanOutputs, artifacts, errors, token);
            }

            foreach (var output in scanOutputs)
            {
                token.ThrowIfCancellationRequested();
                if (Inside(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), output))
                {
                    AddError(errors, $"«{output}»: output-каталог содержит запущенное приложение.");
                    continue;
                }

                if (Excluded(Path.GetFileName(output))
                    || output.Split(Path.DirectorySeparatorChar)
                        .Any(segment => segment.ToLowerInvariant() is ".git" or ".hg" or ".svn"
                            || segment.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase)))
                {
                    AddError(errors, $"«{output}»: служебный каталог не является output-каталогом.");
                    continue;
                }

                if (artifacts.Any(x => x.Kind == ProjectArtifactKind.Coverage
                    ? x.Entries.Any(file => Inside(file.Path, output))
                    : Inside(output, x.Path) || Inside(x.Path, output)))
                {
                    AddError(errors, $"«{output}»: output-каталог пересекается с уже найденной целью. Используйте отдельные строки найденных артефактов.");
                    continue;
                }

                Measure(output, ProjectArtifactKind.CustomOutput, output, artifacts, token);
            }

            return new(
                _owner,
                roots.AsReadOnly(),
                outputs.AsReadOnly(),
                leases.Select(x => x.Stamp).ToList().AsReadOnly(),
                artifacts.OrderBy(x => x.ProjectDirectory, StringComparer.Ordinal).ThenBy(x => x.Path, StringComparer.Ordinal).ToList().AsReadOnly(),
                errors.AsReadOnly());
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }
        }
    }

    private static void Discover(
        string root,
        IReadOnlyCollection<string> customOutputs,
        List<ProjectArtifactScan> artifacts,
        List<string> errors,
        CancellationToken token)
    {
        var pending = new Stack<(string Path, string? Project)>();
        pending.Push((root, null));
        while (pending.TryPop(out var next))
        {
            token.ThrowIfCancellationRequested();
            var directory = next.Path;
            try
            {
                using var current = CacheFileAccess.LockDirectory(directory);
                var entries = new DirectoryInfo(directory).GetFileSystemInfos("*", Entries);
                var project = entries.Any(x => (x.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    && IsProject(x.Extension));

                var solution = entries.Any(x => (x.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0
                    && (x.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        || x.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)));

                var projectDirectory = project || solution ? directory : next.Project;
                if (projectDirectory is not null)
                {
                    var coverage = entries.Where(x => (x.Attributes & FileAttributes.Directory) == 0 && IsCoverage(x.Name)).ToArray();
                    if (coverage.Length > 0)
                    {
                        MeasureCoverage(directory, projectDirectory, coverage, artifacts, token);
                    }
                }

                foreach (var entry in entries.Where(x => (x.Attributes & FileAttributes.Directory) != 0))
                {
                    token.ThrowIfCancellationRequested();
                    if (entry.Name.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase))
                    {
                        AddError(errors, $"Обнаружен каталог незавершённой очистки «{entry.FullName}». Проверьте files и SpaceSnoop-restore.json для восстановления.");
                        continue;
                    }

                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0 || Excluded(entry.Name) || Protected(entry.FullName))
                    {
                        continue;
                    }

                    if (customOutputs.Any(output => Inside(entry.FullName, output)))
                    {
                        continue;
                    }

                    var kind = Kind(entry.Name);
                    if (kind != ProjectArtifactKind.None)
                    {
                        if ((project || (solution && kind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks))
                            && !Inside(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), entry.FullName))
                        {
                            Measure(entry.FullName, kind, projectDirectory!, artifacts, token);
                        }

                        continue;
                    }

                    pending.Push((entry.FullName, projectDirectory));
                }
            }
            catch (Exception exception) when (FileError(exception))
            {
                AddError(errors, exception.Message);
            }
        }
    }

    private CleanupReport Clean(
        ProjectCleanupPlan plan,
        ProjectArtifactScan[] targets,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var totals = new CleanupTotals();
        var leases = new List<CacheFileAccess.DirectoryLease>();
        try
        {
            foreach (var stamp in plan.RootStamps)
            {
                ValidateRoot(stamp.Path);
                var lease = CacheFileAccess.LockDirectory(stamp.Path);
                leases.Add(lease);
                if (!SameDirectory(lease.Stamp, stamp))
                {
                    throw new IOException("Выбранный корень заменён после поиска. Повторите поиск.");
                }
            }

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                _logger.ProjectCleanupTargetStarted(target.Path);
                var deletedBefore = totals.Deleted;
                var bytesBefore = totals.Bytes;
                CleanTarget(plan, target, totals, progress, token);
                _logger.ProjectCleanupTargetFinished(target.Path, totals.Deleted - deletedBefore, totals.Bytes - bytesBefore);
            }
        }
        catch (OperationCanceledException)
        {
            return totals.Report(true);
        }
        catch (Exception exception) when (FileError(exception))
        {
            _logger.ProjectCleanupRootsRejected(exception, string.Join(", ", plan.Roots));
            AddError(totals.Errors, exception.Message);
        }
        finally
        {
            for (var index = leases.Count - 1; index >= 0; index--)
            {
                leases[index].Dispose();
            }
        }

        return totals.Report(token.IsCancellationRequested);
    }

    private void CleanTarget(
        ProjectCleanupPlan plan,
        ProjectArtifactScan target,
        CleanupTotals totals,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var batch = new StagingBatch();
        try
        {
            if (!plan.RootStamps.Any(x => Inside(target.Path, x.Path))
                || Protected(target.Path)
                || target.Entries.Any(x => Protected(x.Path))
                || ContainsApplicationFiles(target, Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)))
            {
                throw new IOException("Каталог находится за границами выбранной области или защищён.");
            }

            using var targetLease = CacheFileAccess.LockDirectory(target.Path);
            if (!SameDirectory(targetLease.Stamp, target.Root))
            {
                throw new IOException($"«{target.Path}»: каталог заменён после поиска.");
            }

            var stagingParent = plan.RootStamps.Any(x => string.Equals(x.Path, target.Path, StringComparison.OrdinalIgnoreCase))
                ? target.Path
                : Path.GetDirectoryName(target.Path)!;

            batch.Path = Path.Combine(stagingParent, $".SpaceSnoop-{Path.GetFileName(target.Path)}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(batch.Path);
            batch.CreatedDirectories.Add(batch.Path);
            using (var stagingLease = CacheFileAccess.LockDirectory(batch.Path))
            {
                WriteManifest(target, batch);
                StageFiles(target, batch, totals, progress, token);
            }

            token.ThrowIfCancellationRequested();
            if (batch.Files.Count > 0)
            {
                _recycle(batch.Path);
                totals.Deleted += batch.Files.Count;
                totals.Bytes += batch.Files.Sum(x => x.Stamp.Length);
                batch.Files.Clear();
                batch.Path = null;
            }
        }
        catch (Exception exception) when (FileError(exception) || exception is COMException)
        {
            _logger.ProjectCleanupTargetFailed(exception, target.Path);
            AddError(totals.Errors, exception.Message);
        }
        finally
        {
            RestoreFiles(batch, totals.Errors);
            RemoveStaging(batch, totals.Errors);
        }
    }

    private static void WriteManifest(ProjectArtifactScan target, StagingBatch batch)
    {
        var path = Path.Combine(batch.Path!, "SpaceSnoop-restore.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new
                {
                    OriginalDirectory = target.Path,
                    PreviewedFiles = target.Entries.Select(x => Path.GetRelativePath(target.Path, x.Path)).ToArray(),
                }));

        using var handle = CacheFileAccess.OpenFile(path, false);
        batch.Manifest = CacheFileAccess.Read(handle, path);
    }

    private void StageFiles(
        ProjectArtifactScan target,
        StagingBatch batch,
        CleanupTotals totals,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        foreach (var file in target.Entries)
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(totals.Deleted + batch.Files.Count, file.Path, totals.Bytes));
            try
            {
                using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Path)!);
                using var handle = CacheFileAccess.OpenFile(file.Path, true);
                if (CacheFileAccess.Read(handle, file.Path) != file)
                {
                    throw new IOException($"«{file.Path}»: файл изменился после поиска и оставлен на месте.");
                }

                var destination = Path.Combine(batch.Path!, "files", Path.GetRelativePath(target.Path, file.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                for (var directory = Path.GetDirectoryName(destination)!;
                    !string.Equals(directory, batch.Path, StringComparison.Ordinal);
                    directory = Path.GetDirectoryName(directory)!)
                {
                    batch.CreatedDirectories.Add(directory);
                }

                using var destinationLease = CacheFileAccess.LockDirectory(Path.GetDirectoryName(destination)!);
                CacheFileAccess.Move(handle, destination);
                batch.Files.Add(new(file, destination));
            }
            catch (Exception exception) when (FileError(exception))
            {
                totals.Skipped++;
                if (totals.Errors.Count < CleanupDefaults.MaxReportedErrors)
                {
                    _logger.ProjectCleanupFileSkipped(exception, file.Path);
                }

                AddError(totals.Errors, exception.Message);
            }
        }
    }

    private void RestoreFiles(StagingBatch batch, List<string> errors)
    {
        foreach (var file in batch.Files)
        {
            try
            {
                using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Stamp.Path)!);
                using var stagedParent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(file.Destination)!);
                using var handle = CacheFileAccess.OpenFile(file.Destination, true);
                if (CacheFileAccess.Read(handle, file.Destination)
                    != file.Stamp with
                    {
                        Path = file.Destination,
                    })
                {
                    throw new IOException("Файл в каталоге восстановления изменился.");
                }

                CacheFileAccess.Move(handle, file.Stamp.Path);
            }
            catch (Exception exception) when (FileError(exception))
            {
                if (errors.Count < CleanupDefaults.MaxReportedErrors)
                {
                    _logger.ProjectCleanupRestoreFailed(exception, file.Stamp.Path, file.Destination);
                }

                AddError(errors, $"Восстановите «{file.Stamp.Path}» из «{file.Destination}»: {exception.Message}");
            }
        }
    }

    private void RemoveStaging(StagingBatch batch, List<string> errors)
    {
        if (batch.Path is null)
        {
            return;
        }

        try
        {
            if (batch.Files.Any(x => File.Exists(x.Destination)))
            {
                errors.Add($"Файлы сохранены для восстановления: «{batch.Path}».");
                return;
            }

            if (batch.Manifest is { } stamp)
            {
                using var parent = CacheFileAccess.LockDirectory(batch.Path);
                using var handle = CacheFileAccess.OpenFile(stamp.Path, true);
                if (CacheFileAccess.Read(handle, stamp.Path) == stamp)
                {
                    CacheFileAccess.Delete(handle, stamp.Path);
                }
            }

            foreach (var directory in batch.CreatedDirectories.OrderByDescending(x => x.Length))
            {
                using var parent = CacheFileAccess.LockDirectory(Path.GetDirectoryName(directory)!);
                using var handle = CacheFileAccess.OpenDirectoryForDelete(directory);
                CacheFileAccess.Delete(handle, directory);
            }
        }
        catch (Exception exception) when (FileError(exception))
        {
            _logger.ProjectCleanupStagingKept(exception, batch.Path);
            errors.Add($"Проверьте каталог восстановления «{batch.Path}»: {exception.Message}");
        }
    }

    private static void Measure(
        string path,
        ProjectArtifactKind kind,
        string projectDirectory,
        List<ProjectArtifactScan> artifacts,
        CancellationToken token)
    {
        var files = new List<CacheFileStamp>();
        var coverage = new List<CacheFileStamp>();
        var errors = new List<string>();
        CacheFileStamp root = default;
        var directories = new Stack<string>();
        directories.Push(path);
        try
        {
            using var rootLease = CacheFileAccess.LockDirectory(path);
            root = rootLease.Stamp;
            while (directories.TryPop(out var current))
            {
                token.ThrowIfCancellationRequested();
                using var lease = CacheFileAccess.LockDirectory(current);
                foreach (var entry in new DirectoryInfo(current).EnumerateFileSystemInfos("*", Entries))
                {
                    token.ThrowIfCancellationRequested();
                    if ((entry.Attributes & FileAttributes.ReparsePoint) != 0
                        || Excluded(entry.Name)
                        || Protected(entry.FullName)
                        || IsProject(entry.Extension)
                        || entry.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
                        || entry.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException($"«{entry.FullName}»: ссылка, защищённый путь или вложенный проект; каталог недоступен для очистки.");
                    }

                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        var nestedKind = Kind(entry.Name);
                        if (nestedKind is ProjectArtifactKind.TestResults or ProjectArtifactKind.Benchmarks)
                        {
                            Measure(entry.FullName, nestedKind, projectDirectory, artifacts, token);
                        }
                        else
                        {
                            directories.Push(entry.FullName);
                        }
                    }
                    else
                    {
                        using var handle = CacheFileAccess.OpenFile(entry.FullName, false);
                        var stamp = CacheFileAccess.Read(handle, entry.FullName);
                        if (kind != ProjectArtifactKind.TestResults && IsCoverage(entry.Name))
                        {
                            coverage.Add(stamp);
                        }
                        else
                        {
                            files.Add(stamp);
                        }
                    }
                }
            }
        }
        catch (Exception exception) when (FileError(exception))
        {
            AddError(errors, exception.Message);
        }

        artifacts.Add(new(kind, path, root, files.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
        if (coverage.Count > 0)
        {
            artifacts.Add(new(ProjectArtifactKind.Coverage, path, root, coverage.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
        }
    }

    private static void MeasureCoverage(
        string path,
        string projectDirectory,
        FileSystemInfo[] entries,
        List<ProjectArtifactScan> artifacts,
        CancellationToken token)
    {
        using var lease = CacheFileAccess.LockDirectory(path);
        var files = new List<CacheFileStamp>();
        var errors = new List<string>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var handle = CacheFileAccess.OpenFile(entry.FullName, false);
                files.Add(CacheFileAccess.Read(handle, entry.FullName));
            }
            catch (Exception exception) when (FileError(exception))
            {
                AddError(errors, exception.Message);
            }
        }

        artifacts.Add(new(ProjectArtifactKind.Coverage, path, lease.Stamp, files.AsReadOnly(), errors.AsReadOnly(), projectDirectory));
    }

    private static List<string> CollapseRoots(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths.Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Length)
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ValidateRoot(path);
            if (!result.Any(root => Inside(path, root)))
            {
                result.Add(path);
            }
        }

        return result;
    }

    private static bool IsCoverage(string name)
    {
        return name.Equals("lcov.info", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".opencover.xml", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".coverage", StringComparison.OrdinalIgnoreCase)
            || (name.StartsWith("coverage", StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(name).ToLowerInvariant() is ".xml" or ".json" or ".info" or ".lcov");
    }

    private static string Normalize(string path)
    {
        if (!Path.IsPathFullyQualified(path)
            || path.Length < 3
            || path[1] != ':'
            || path[2] != '\\'
            || path.AsSpan(2).Contains(':')
            || path.Split('\\').Any(x => x.EndsWith(' ') || x.EndsWith('.')))
        {
            throw new IOException("Выберите локальный каталог с однозначным абсолютным путём.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static void ValidateRoot(string root)
    {
        if (new DirectoryInfo(root).Parent is null
            || Protected(root)
            || string.Equals(root, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Выберите отдельный каталог проектов, а не диск, домашний или системный каталог.");
        }
    }

    private static bool Protected(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return CleanupPaths.ProtectedRoots()
            .Append(Path.Combine(home, ".nuget"))
            .Any(x => Inside(path, Path.TrimEndingDirectorySeparator(x)));
    }

    private static bool Inside(string path, string root)
    {
        return string.Equals(path, root, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDirectory(CacheFileStamp left, CacheFileStamp right)
    {
        return left.Volume == right.Volume
            && left.Id == right.Id
            && left.Created == right.Created;
    }

    private static bool IsProject(string extension)
    {
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Excluded(string name)
    {
        return name.ToLowerInvariant() is ".git" or ".hg" or ".svn" or ".kc" or ".claude" or ".codex" or "node_modules"
            || name.StartsWith(".SpaceSnoop-", StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectArtifactKind Kind(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "bin" => ProjectArtifactKind.Bin,
            "obj" => ProjectArtifactKind.Obj,
            "testresults" => ProjectArtifactKind.TestResults,
            "benchmarkdotnet.artifacts" => ProjectArtifactKind.Benchmarks,
            _ => ProjectArtifactKind.None,
        };
    }

    private static bool FileError(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or SecurityException;
    }

    private static void AddError(List<string> errors, string error)
    {
        if (errors.Count < CleanupDefaults.MaxReportedErrors)
        {
            errors.Add(error);
        }
    }

    private sealed record StagedFile(CacheFileStamp Stamp, string Destination);

    private sealed class StagingBatch
    {
        public string? Path { get; set; }
        public CacheFileStamp? Manifest { get; set; }
        public List<StagedFile> Files { get; } = [];
        public HashSet<string> CreatedDirectories { get; } = new(StringComparer.Ordinal);
    }

    private sealed class CleanupTotals
    {
        public long Bytes { get; set; }
        public int Deleted { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; } = [];

        public CleanupReport Report(bool cancelled)
        {
            return new(Bytes, Deleted, Skipped, Errors.AsReadOnly(), cancelled);
        }
    }
}
