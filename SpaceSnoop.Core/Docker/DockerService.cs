using System.Text;

namespace SpaceSnoop.Core.Docker;

public sealed class DockerService
{
    private readonly IDockerProcessRunner _processRunner;
    private readonly IDockerFileSystem _fileSystem;
    private readonly IDockerDesktopSettingsProvider _settingsProvider;
    private readonly IDockerLegacyVhdxProvider _legacyVhdxProvider;
    private readonly DockerCompactOptions _compactOptions;
    private readonly Func<bool> _administratorCheck;

    public DockerService(
        IDockerProcessRunner? processRunner = null,
        IDockerFileSystem? fileSystem = null,
        IDockerDesktopSettingsProvider? settingsProvider = null,
        DockerCompactOptions? compactOptions = null,
        IDockerLegacyVhdxProvider? legacyVhdxProvider = null,
        Func<bool>? administratorCheck = null)
    {
        _processRunner = processRunner ?? new SystemDockerProcessRunner();
        _fileSystem = fileSystem ?? new WindowsDockerFileSystem();
        _settingsProvider = settingsProvider ?? new DockerDesktopSettingsProvider();
        _legacyVhdxProvider = legacyVhdxProvider ?? new WindowsDockerLegacyVhdxProvider();
        _compactOptions = compactOptions ?? new DockerCompactOptions();
        _administratorCheck = administratorCheck ?? IsAdministrator;
    }

    public async Task<DockerSnapshot> GetSnapshotAsync(CancellationToken cancel = default)
    {
        var run = await RunAsync("docker", "system df --format \"{{json .}}\"", cancel: cancel);

        return run.Failed
            ? DockerSnapshot.Unavailable(DescribeFailure(run, "docker system df"))
            : DockerSnapshot.Ok(DockerUsage.Parse(run.StdOut));
    }

    public async Task<IReadOnlyList<DockerObject>> GetInventoryAsync(CancellationToken cancel = default)
    {
        var run = await RunAsync("docker", "system df -v --format \"{{json .}}\"", cancel: cancel);
        return run.Failed ? [] : DockerInventory.Parse(run.StdOut);
    }

    public async Task<string> RemoveAsync(DockerObject target, CancellationToken cancel = default)
    {
        var args = target.Kind switch
        {
            DockerObjectKind.Image => $"image rm {target.Id}",
            DockerObjectKind.Container => $"container rm {target.Id}",
            DockerObjectKind.Volume => $"volume rm {target.Id}",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target.Kind, null),
        };

        var run = await RunAsync("docker", args, cancel: cancel);
        return run.Failed ? throw new InvalidOperationException(DescribeFailure(run, $"docker {args}")) : run.StdOut.Trim();
    }

    public async Task<string> PruneAsync(DockerCleanupTarget target, bool allUnused = false, CancellationToken cancel = default)
    {
        var args = target switch
        {
            DockerCleanupTarget.BuildCache => "builder prune -f",
            DockerCleanupTarget.DanglingImages => "image prune -f",
            DockerCleanupTarget.UnusedImages => "image prune -a -f",
            DockerCleanupTarget.StoppedContainers => "container prune -f",
            DockerCleanupTarget.UnusedVolumes => allUnused ? "volume prune -a -f" : "volume prune -f",
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, null),
        };

        var run = await RunAsync("docker", args, cancel: cancel);

        return run.Failed ? throw new InvalidOperationException(DescribeFailure(run, $"docker {args}")) : run.StdOut.Trim();
    }

    public async Task<DockerCompactResult> CompactAsync(
        CancellationToken cancel = default,
        bool allowWslShutdownFallback = false,
        IProgress<DockerCompactStage>? progress = null,
        Action<DockerCompactStage>? stageChanged = null)
    {
        var steps = new List<DockerCompactStep>();
        ReportStage(DockerCompactStage.Preparing, progress, stageChanged);

        if (cancel.IsCancellationRequested)
        {
            return Canceled(null, steps);
        }

        var settings = _settingsProvider.Read();
        if (settings is null)
        {
            steps.Add(new("Настройки Docker Desktop", false, "Не найден или не прочитан settings-store.json."));
            return Failed(null, null, null, steps);
        }

        if (!settings.WslEngineEnabled)
        {
            steps.Add(new("Проверка бэкенда", false, "Включён не WSL2-бэкенд Docker Desktop. Этот способ поддерживает только WSL2."));
            return Failed(null, null, null, steps);
        }

        var vhdx = LocateDataVhdx(settings);
        if (vhdx is null)
        {
            steps.Add(new(
                "Поиск образа данных",
                false,
                "Не найден образ данных в CustomWslDistroDir\\disk\\docker_data.vhdx, стандартном пути %LOCALAPPDATA%\\Docker\\wsl\\disk\\docker_data.vhdx или старой раскладке docker-desktop-data\\ext4.vhdx."));
            return Failed(null, null, null, steps);
        }

        steps.Add(new("Поиск образа данных", true, vhdx));

        if (!_administratorCheck())
        {
            steps.Add(new("Проверка прав администратора", false, "Для diskpart нужны права администратора. Запустите SpaceSnoop с повышением прав."));
            return Failed(null, null, null, steps);
        }

        steps.Add(new("Проверка прав администратора", true, "Права администратора доступны."));

        var distroResult = await ListDockerDistrosAsync(cancel);
        if (distroResult.IsCanceled)
        {
            steps.Add(new("Проверка WSL", false, "Операция отменена."));
            return Canceled(vhdx, steps);
        }

        if (distroResult.IsFailed)
        {
            steps.Add(new("Проверка WSL", false, DescribeFailure(distroResult.Run, "wsl --list --quiet")));
            return Failed(vhdx, null, null, steps, distroResult.Distributions);
        }

        var distributions = distroResult.Distributions;
        steps.Add(new("Проверка WSL", true, string.Join(", ", distributions)));

        // TODO: Проверить отдельным экспериментом fstrim в namespace держателя data-образа перед остановкой Docker.
        ReportStage(DockerCompactStage.Stopping, progress, stageChanged);
        var stop = allowWslShutdownFallback
            ? await HandleFallbackAsync(vhdx, distributions, true, steps, cancel)
            : await StopDockerAsync(vhdx, distributions, steps, cancel);
        if (stop.WasCanceled)
        {
            return Canceled(vhdx, steps, distributions: distributions);
        }

        if (stop.NeedsFallback)
        {
            return new(
                DockerCompactStatus.RequiresWslShutdownConfirmation,
                vhdx,
                null,
                null,
                steps.ToArray(),
                distributions);
        }

        if (!stop.IsReady)
        {
            return Failed(vhdx, null, null, steps, distributions);
        }

        DockerCompactMeasurement? before = null;
        try
        {
            before = ToMeasurement(_fileSystem.GetMetrics(vhdx));
            steps.Add(new("Измерение до сжатия", true, DescribeMeasurement(before.Value)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            steps.Add(new("Измерение до сжатия", false, ex.Message));
            return Failed(vhdx, null, null, steps, distributions);
        }

        ReportStage(DockerCompactStage.Compacting, progress, stageChanged);
        if (cancel.IsCancellationRequested)
        {
            steps.Add(new("Сжатие через diskpart", false, "Операция отменена до запуска diskpart."));
            return Canceled(vhdx, steps, before, distributions);
        }

        var escapedVhdx = vhdx.Replace("\"", "\"\"");
        var script = $"select vdisk file=\"{escapedVhdx}\"\r\ncompact vdisk\r\nexit\r\n";
        var compact = await RunAsync(
            "diskpart",
            string.Empty,
            stdin: script,
            cancel: CancellationToken.None,
            timeout: _compactOptions.CompactTimeout);
        if (compact.Canceled)
        {
            steps.Add(new("Сжатие через diskpart", false, "Операция отменена. Процесс завершён."));
            return Canceled(vhdx, steps, before, distributions);
        }

        var diskpartSucceeded = compact.Succeeded && !ContainsDiskpartError(compact);
        steps.Add(new(
            "Сжатие через diskpart",
            diskpartSucceeded,
            diskpartSucceeded ? "Выполнено без attach/detach." : DescribeFailure(compact, "diskpart")));

        DockerCompactMeasurement? after = null;
        try
        {
            after = ToMeasurement(_fileSystem.GetMetrics(vhdx));
            steps.Add(new("Измерение после сжатия", true, DescribeMeasurement(after.Value)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            steps.Add(new("Измерение после сжатия", false, ex.Message));
        }

        if (!diskpartSucceeded)
        {
            return Failed(vhdx, before, after, steps, distributions);
        }

        return new(
            DockerCompactStatus.Succeeded,
            vhdx,
            before,
            after,
            steps.ToArray(),
            distributions);
    }

    private async Task<DockerStopResult> StopDockerAsync(
        string vhdx,
        IReadOnlyList<string> distributions,
        List<DockerCompactStep> steps,
        CancellationToken cancel)
    {
        var stop = await RunAsync("docker", "desktop stop", cancel: cancel);
        if (stop.Canceled)
        {
            steps.Add(new("Остановка Docker Desktop", false, "Операция отменена. Процесс завершён."));
            return DockerStopResult.CanceledResult;
        }

        if (stop.Failed)
        {
            steps.Add(new("Остановка Docker Desktop", false, DescribeFailure(stop, "docker desktop stop")));
            return await HandleFallbackAsync(vhdx, distributions, false, steps, cancel);
        }

        steps.Add(new("Остановка Docker Desktop", true, "Команда завершена."));
        var wait = await WaitForDockerReleaseAsync(vhdx, cancel);
        if (wait.WasCanceled)
        {
            steps.Add(new("Ожидание освобождения образа", false, "Операция отменена."));
            return DockerStopResult.CanceledResult;
        }

        if (wait.IsReady)
        {
            steps.Add(new("Ожидание освобождения образа", true, "Docker Desktop остановлен, файл свободен."));
            return DockerStopResult.ReadyResult;
        }

        steps.Add(new("Ожидание освобождения образа", false, wait.Detail));
        return await HandleFallbackAsync(vhdx, distributions, false, steps, cancel);
    }

    private static void ReportStage(
        DockerCompactStage stage,
        IProgress<DockerCompactStage>? progress,
        Action<DockerCompactStage>? stageChanged)
    {
        progress?.Report(stage);
        stageChanged?.Invoke(stage);
    }

    private async Task<DockerStopResult> HandleFallbackAsync(
        string vhdx,
        IReadOnlyList<string> distributions,
        bool allowWslShutdownFallback,
        List<DockerCompactStep> steps,
        CancellationToken cancel)
    {
        if (!allowWslShutdownFallback)
        {
            steps.Add(new(
                "Подтверждение остановки WSL",
                false,
                $"Нужен fallback wsl --shutdown. Будут остановлены: {string.Join(", ", distributions)}."));
            return DockerStopResult.FallbackResult;
        }

        var shutdown = await RunAsync("wsl", "--shutdown", cancel: cancel);
        if (shutdown.Canceled)
        {
            steps.Add(new("Остановка WSL", false, "Операция отменена. Процесс завершён."));
            return DockerStopResult.CanceledResult;
        }

        if (shutdown.Failed)
        {
            steps.Add(new("Остановка WSL", false, DescribeFailure(shutdown, "wsl --shutdown")));
            return DockerStopResult.FailedResult;
        }

        steps.Add(new("Остановка WSL", true, "Подтверждённый fallback выполнен."));
        var wait = await WaitForFileReleaseAsync(vhdx, cancel);
        if (wait.WasCanceled)
        {
            steps.Add(new("Ожидание освобождения образа", false, "Операция отменена."));
            return DockerStopResult.CanceledResult;
        }

        if (!wait.IsReady)
        {
            steps.Add(new("Ожидание освобождения образа", false, wait.Detail));
            return DockerStopResult.FailedResult;
        }

        steps.Add(new("Ожидание освобождения образа", true, "Образ данных свободен."));
        return DockerStopResult.ReadyResult;
    }

    private async Task<DockerWaitResult> WaitForDockerReleaseAsync(string vhdx, CancellationToken cancel)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var detail = "Docker Desktop ещё не остановлен или образ занят.";

        while (stopwatch.Elapsed < _compactOptions.StopTimeout)
        {
            var status = await RunAsync("docker", "desktop status", cancel: cancel);
            if (status.Canceled)
            {
                return DockerWaitResult.CanceledResult;
            }

            if (status.Failed)
            {
                detail = DescribeFailure(status, "docker desktop status");
            }
            else if (IsDockerStopped(status.StdOut) && _fileSystem.IsAvailable(vhdx))
            {
                return DockerWaitResult.ReadyResult;
            }
            else
            {
                detail = "Docker Desktop ещё не остановлен или образ занят.";
            }

            try
            {
                await Task.Delay(_compactOptions.StatusPollInterval, cancel);
            }
            catch (OperationCanceledException)
            {
                return DockerWaitResult.CanceledResult;
            }
        }

        return new(false, false, $"Не удалось дождаться остановки Docker Desktop и освобождения образа за {_compactOptions.StopTimeout.TotalSeconds:0} с. {detail}");
    }

    private async Task<DockerWaitResult> WaitForFileReleaseAsync(string vhdx, CancellationToken cancel)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.Elapsed < _compactOptions.StopTimeout)
        {
            if (_fileSystem.IsAvailable(vhdx))
            {
                return DockerWaitResult.ReadyResult;
            }

            try
            {
                await Task.Delay(_compactOptions.StatusPollInterval, cancel);
            }
            catch (OperationCanceledException)
            {
                return DockerWaitResult.CanceledResult;
            }
        }

        return new(false, false, $"Образ остаётся занятым после остановки WSL более {_compactOptions.StopTimeout.TotalSeconds:0} с.");
    }

    private async Task<DockerDistroResult> ListDockerDistrosAsync(CancellationToken cancel)
    {
        var run = await RunAsync("wsl", "--list --quiet", Encoding.Unicode, cancel: cancel);
        if (run.Canceled)
        {
            return DockerDistroResult.CanceledResult;
        }

        if (run.Failed)
        {
            return new([], run, false);
        }

        var distros = run.StdOut
            .Split(['\r', '\n', '\0'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return distros.Length == 0
            || !distros.Any(x => x.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            ? new([], new(-1, string.Empty, "Дистрибутивы docker-desktop не найдены."), false)
            : new(distros, run, false);
    }

    private string? LocateDataVhdx(DockerDesktopSettings settings)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(settings.CustomWslDistroDir))
        {
            candidates.Add(Path.Combine(NormalizePath(settings.CustomWslDistroDir.Trim()), "disk", "docker_data.vhdx"));
        }

        var localDockerPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localDockerPath))
        {
            candidates.Add(Path.Combine(localDockerPath, "Docker", "wsl", "disk", "docker_data.vhdx"));
        }

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return _legacyVhdxProvider.Locate().FirstOrDefault(_fileSystem.FileExists);
    }

    private async Task<DockerProcessResult> RunAsync(
        string fileName,
        string arguments,
        Encoding? outputEncoding = null,
        CancellationToken cancel = default,
        string? stdin = null,
        TimeSpan? timeout = null)
    {
        return await _processRunner.RunAsync(
            fileName,
            arguments,
            new(timeout ?? _compactOptions.ProcessTimeout, outputEncoding, stdin),
            cancel);
    }

    private static DockerCompactMeasurement ToMeasurement(DockerFileMetrics metrics)
    {
        return new(metrics.LogicalBytes, metrics.AllocatedBytes);
    }

    private static string DescribeMeasurement(DockerCompactMeasurement measurement)
    {
        return $"логический размер {measurement.LogicalText}, занято на диске {measurement.AllocatedText}";
    }

    private static bool IsDockerStopped(string output)
    {
        var lines = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Trim())
            .ToArray();
        var statusLine = lines.FirstOrDefault(x => x.StartsWith("Status", StringComparison.OrdinalIgnoreCase));

        if (statusLine is not null)
        {
            var status = statusLine["Status".Length..].Trim().TrimStart(':').Trim();
            return IsStoppedValue(status);
        }

        return lines.Any(x => x.Contains("is not running", StringComparison.OrdinalIgnoreCase)
            || x.Contains("is stopped", StringComparison.OrdinalIgnoreCase)
            || x.Contains("не запущен", StringComparison.OrdinalIgnoreCase)
            || x.Contains("не работает", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStoppedValue(string status)
    {
        return status.Equals("stopped", StringComparison.OrdinalIgnoreCase)
            || status.Equals("not running", StringComparison.OrdinalIgnoreCase)
            || status.Equals("inactive", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDiskpartError(DockerProcessResult run)
    {
        if (!run.Succeeded)
        {
            return true;
        }

        return $"{run.StdOut}\n{run.StdErr}"
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(x => x.Contains("DiskPart error:", StringComparison.OrdinalIgnoreCase)
                || x.Contains("has encountered an error", StringComparison.OrdinalIgnoreCase)
                || x.Contains("failed to compact", StringComparison.OrdinalIgnoreCase)
                || x.Contains("не удалось сжать", StringComparison.OrdinalIgnoreCase)
                || x.Contains("обнаружил ошиб", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
        {
            return $@"\\{path[8..]}";
        }

        return path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase) ? path[4..] : path;
    }

    private static DockerCompactResult Failed(
        string? vhdx,
        DockerCompactMeasurement? before,
        DockerCompactMeasurement? after,
        IReadOnlyList<DockerCompactStep> steps,
        IReadOnlyList<string>? distributions = null)
    {
        return new(DockerCompactStatus.Failed, vhdx, before, after, steps, distributions ?? []);
    }

    private static DockerCompactResult Canceled(
        string? vhdx,
        IReadOnlyList<DockerCompactStep> steps,
        DockerCompactMeasurement? before = null,
        IReadOnlyList<string>? distributions = null)
    {
        return new(DockerCompactStatus.Canceled, vhdx, before, null, steps, distributions ?? []);
    }

    private static string DescribeFailure(DockerProcessResult run, string command)
    {
        if (run.Canceled)
        {
            return "Операция отменена.";
        }

        if (run.TimedOut)
        {
            return $"Команда «{command}» превысила таймаут и процесс был завершён.";
        }

        if (run.LaunchError is not null)
        {
            var elevation = run.LaunchError.Contains("elevation", StringComparison.OrdinalIgnoreCase)
                || run.LaunchError.Contains("740", StringComparison.Ordinal);
            return elevation
                ? $"Не удалось запустить «{command}»: нужны права администратора."
                : $"Не удалось запустить «{command}»: {run.LaunchError}.";
        }

        if (run.ExitCode == 740)
        {
            return $"Команда «{command}» требует прав администратора.";
        }

        var message = run.StdErr.Trim();
        return message.Length > 0 ? message : $"Команда «{command}» завершилась с кодом {run.ExitCode}.";
    }

    private static bool IsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private sealed record DockerStopResult(bool IsReady, bool NeedsFallback, bool WasCanceled)
    {
        public static DockerStopResult ReadyResult { get; } = new(true, false, false);

        public static DockerStopResult FallbackResult { get; } = new(false, true, false);

        public static DockerStopResult FailedResult { get; } = new(false, false, false);

        public static DockerStopResult CanceledResult { get; } = new(false, false, true);
    }

    private sealed record DockerWaitResult(bool IsReady, bool WasCanceled, string Detail)
    {
        public static DockerWaitResult ReadyResult { get; } = new(true, false, string.Empty);

        public static DockerWaitResult CanceledResult { get; } = new(false, true, "Операция отменена.");
    }

    private sealed record DockerDistroResult(
        IReadOnlyList<string> Distributions,
        DockerProcessResult Run,
        bool IsCanceled)
    {
        public bool IsFailed => !IsCanceled && !Run.Succeeded;

        public static DockerDistroResult CanceledResult { get; } = new([], new(-1, string.Empty, string.Empty, Canceled: true), true);
    }
}
