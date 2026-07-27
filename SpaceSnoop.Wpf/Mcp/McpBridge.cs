using ModelContextProtocol;
using SpaceSnoop.Core.Export;
using System.IO;
using System.Security;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Mcp;

public sealed class McpBridge(
    ISettingsStore settings,
    SyncViewModel sync,
    ScanViewModel scan,
    McpPreferences preferences,
    ScanPreferences scanPreferences,
    DiskSpaceCalculator calculator,
    DockerService docker,
    ToastNotifier notifier,
    ILogger<DirectoryComparer> comparerLogger,
    ILogger<McpBridge> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    private ShellViewModel? _shell;

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
    }

    public string GetState()
    {
        logger.McpToolInvoked("get_app_state", "-");

        return Dispatch(() => Serialize(new McpAppState(
            AppInfo.Name,
            AppInfo.Version,
            AdminElevation.IsElevated,
            _shell?.CurrentSectionKey,
            preferences.AllowMutations,
            SyncProfileStore.Load(settings).Count,
            ReadScanState(),
            ReadSyncState())));
    }

    public string ListProfiles()
    {
        logger.McpToolInvoked("list_profiles", "-");

        var profiles = SyncProfileStore.Load(settings)
            .Select(static profile => new McpProfile(
                profile.Id,
                profile.Name,
                profile.Left,
                profile.Right,
                HeadlessSync.MapMode(profile.Mode),
                profile.Winner,
                profile.Mirror,
                profile.Exclusions,
                profile.Enabled,
                profile.SkipInBatch,
                $"{profile.Interval} {profile.Time}",
                DescribeUnavailable(profile)))
            .ToList();

        return Serialize(profiles);
    }

    public async Task<string> CompareAsync(
        string left,
        string right,
        SyncMode mode,
        SyncWinner winner,
        bool mirror,
        string? exclusions,
        int entryLimit,
        CancellationToken cancellationToken)
    {
        left = left.Trim();
        right = right.Trim();
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("compare_directories", $"«{left}» → «{right}», режим {mode}, записей до {entryLimit}");

        Validate(left, right, mode);

        var patterns = exclusions?.Trim() ?? string.Empty;

        var model = await Task.Run(
            () =>
            {
                var comparer = new DirectoryComparer(new ExclusionFilter(patterns), comparerLogger);
                var result = comparer.Compare(left, right, cancellationToken);
                result.ApplyMode(mode, mirror, winner);

                return ComparisonExport.Build(result, new(mode, winner, mirror, patterns), AppInfo.Version, entryLimit);
            },
            cancellationToken)
            .ConfigureAwait(false);

        return ComparisonExport.ToJson(model);
    }

    public async Task<string> ScanAsync(string path, int depth, int entryLimit, CancellationToken cancellationToken)
    {
        path = path.Trim();
        depth = ClampDepth(depth);
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("scan_directory", $"«{path}», глубина {depth}, записей до {entryLimit}");

        ValidateScanPath(path);

        // Те же параметры обхода, что и у человека на странице «Сканирование».
        var multithreaded = scanPreferences.UseMultithreading;
        var parallelism = scanPreferences.MaxParallelism;

        var model = await Task.Run(
            () =>
            {
                var directory = new DirectoryInfo(path);

                var root = multithreaded
                    ? calculator.CalculateMultithreaded(directory, parallelism, cancellationToken)
                    : calculator.Calculate(directory, cancellationToken);

                return ScanExport.Build(root, directory.FullName, new(depth, multithreaded, parallelism), AppInfo.Version, entryLimit);
            },
            cancellationToken)
            .ConfigureAwait(false);

        return ScanExport.ToJson(model);
    }

    public string ListDrives()
    {
        logger.McpToolInvoked("list_drives", "-");

        var drives = DriveInfo.GetDrives().Select(Describe).ToList();

        return Serialize(drives);
    }

    public Task<string> GetCurrentScanAsync(int depth, int entryLimit, CancellationToken cancellationToken)
    {
        depth = ClampDepth(depth);
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("get_current_scan", $"глубина {depth}, записей до {entryLimit}");

        var build = Dispatch(() => scan.CaptureExportBuilder(depth, entryLimit));

        if (build is null)
        {
            throw new McpException("На странице «Сканирование» результата ещё нет. Запустите open_scan с scan=true или scan_directory.");
        }

        return BuildScanJsonAsync(build, cancellationToken);
    }

    public async Task<string> OpenScanAsync(string? path, bool start, CancellationToken cancellationToken)
    {
        logger.McpToolInvoked("open_scan", $"«{path ?? "как есть"}», сканирование {start}");

        var run = Dispatch(() =>
        {
            if (scan.IsScanning)
            {
                logger.McpToolRejected("open_scan", "страница занята операцией");
                throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
            }

            var target = string.IsNullOrWhiteSpace(path) ? scan.SelectedDrive.Trim() : path.Trim();

            if (start)
            {
                ValidateScanPath(target);
            }

            if (target.Length > 0)
            {
                scan.SelectPathForAutomation(target);
            }

            _shell?.TryNavigate(SectionKey.Scan);

            notifier.Notify(start
                ? $"Агент запустил сканирование: {target}"
                : "Агент открыл страницу «Сканирование»");

            return start ? scan.ScanFromAutomationAsync(target) : null;
        });

        if (run is not null)
        {
            await run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Dispatch(() => Serialize(new McpScanNavigation(_shell?.CurrentSectionKey ?? SectionKey.Scan, ReadScanState())));
    }

    public async Task<string> GetDockerUsageAsync(bool includeObjects, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("docker_usage", $"объекты {includeObjects}, записей до {entryLimit}");

        var snapshot = await docker.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var buckets = snapshot.Buckets
            .Select(static bucket => new McpDockerBucket(
                bucket.Type,
                bucket.TotalCount,
                bucket.Active,
                bucket.Size,
                DockerSize.ToBytes(bucket.Size),
                bucket.Reclaimable,
                DockerSize.ToBytes(bucket.Reclaimable)))
            .ToList();

        if (!snapshot.Available || !includeObjects)
        {
            return Serialize(new McpDockerReport(snapshot.Available, snapshot.Error, buckets, null, 0));
        }

        var inventory = await docker.GetInventoryAsync(cancellationToken).ConfigureAwait(false);

        var objects = inventory
            .OrderByDescending(static item => item.SizeBytes)
            .Take(entryLimit)
            .Select(static item => new McpDockerObject(item.Kind, item.Id, item.Name, item.Size, item.SizeBytes, item.InUse, item.Detail))
            .ToList();

        return Serialize(new McpDockerReport(true, snapshot.Error, buckets, objects, Math.Max(0, inventory.Count - objects.Count)));
    }

    public Task<string> GetCurrentComparisonAsync(int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = ClampEntryLimit(entryLimit);
        logger.McpToolInvoked("get_current_comparison", $"записей до {entryLimit}");

        return ExportCurrentAsync(entryLimit, cancellationToken);
    }

    public async Task<string> OpenSyncAsync(
        string? left,
        string? right,
        SyncMode? mode,
        SyncWinner? winner,
        bool? mirror,
        string? exclusions,
        bool compare,
        CancellationToken cancellationToken)
    {
        logger.McpToolInvoked("open_sync", $"«{left ?? "как есть"}» → «{right ?? "как есть"}», режим {mode?.ToString() ?? "как есть"}, сравнение {compare}");

        var comparison = Dispatch(() =>
        {
            if (sync.IsBusy)
            {
                logger.McpToolRejected("open_sync", "страница занята операцией");
                throw new McpException("Страница «Синхронизация» сейчас занята другой операцией.");
            }

            var targetLeft = string.IsNullOrWhiteSpace(left) ? sync.LeftPath.Trim() : left.Trim();
            var targetRight = string.IsNullOrWhiteSpace(right) ? sync.RightPath.Trim() : right.Trim();
            var targetMode = mode ?? sync.CurrentMode;

            if (compare)
            {
                Validate(targetLeft, targetRight, targetMode);
            }

            if (!string.Equals(sync.LeftPath, targetLeft, StringComparison.Ordinal))
            {
                sync.LeftPath = targetLeft;
            }

            if (!string.Equals(sync.RightPath, targetRight, StringComparison.Ordinal))
            {
                sync.RightPath = targetRight;
            }

            sync.SelectedModeIndex = SyncProfile.IndexOfMode(targetMode);

            if (winner is { } side)
            {
                sync.SelectedWinnerIndex = SyncProfile.IndexOfWinner(side);
            }

            if (mirror is { } enabled)
            {
                sync.Mirror = enabled;
            }

            if (exclusions is not null)
            {
                sync.Exclusions = exclusions.Trim();
            }

            _shell?.TryNavigate(SectionKey.Sync);

            notifier.Notify(compare
                ? $"Агент запустил сравнение: {targetLeft} → {targetRight}"
                : "Агент открыл страницу «Синхронизация»");

            return compare ? sync.CompareCommand.ExecuteAsync(null) : null;
        });

        if (comparison is not null)
        {
            await comparison.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Dispatch(() => Serialize(new McpNavigationResult(_shell?.CurrentSectionKey ?? SectionKey.Sync, ReadSyncState())));
    }

    public async Task<string> SyncCurrentAsync(bool dryRun, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = ClampEntryLimit(entryLimit);

        if (dryRun)
        {
            logger.McpToolInvoked("sync_current", $"план, записей до {entryLimit}");
            return await ExportCurrentAsync(entryLimit, cancellationToken).ConfigureAwait(false);
        }

        if (!preferences.AllowMutations)
        {
            logger.McpToolRejected("sync_current", "изменяющие операции запрещены");
            throw new McpException("Изменяющие операции запрещены. Включите «Разрешить изменяющие операции» в настройках приложения.");
        }

        var run = Dispatch(() =>
        {
            if (!sync.HasResult)
            {
                throw new McpException("Сначала выполните сравнение: open_sync с compare=true.");
            }

            if (sync.IsBusy)
            {
                throw new McpException("Страница «Синхронизация» сейчас занята другой операцией.");
            }

            if (sync.HasPending)
            {
                throw new McpException("Есть неразрешённые спорные элементы – разрешите их в приложении.");
            }

            logger.McpMutationRequested("sync_current", $"«{sync.LeftPath}» → «{sync.RightPath}», режим {sync.CurrentMode}, зеркало {sync.Mirror}");
            notifier.Notify("Агент запустил синхронизацию", StatusSeverity.Warning);

            return sync.SyncFromAutomationAsync();
        });

        var report = await run.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (report is null)
        {
            throw new McpException("Синхронизация не выполнена: операция отменена или сравнение сброшено.");
        }

        return Dispatch(() => Serialize(new McpSyncResult(
            report.CopiedCount,
            report.DeletedCount,
            report.SuccessCount,
            report.Errors,
            report.Mismatches,
            ReadSyncState())));
    }

    internal static int ClampEntryLimit(int entryLimit)
    {
        return Math.Clamp(entryLimit, AppDefaults.McpEntryLimitMin, AppDefaults.McpEntryLimitMax);
    }

    internal static int ClampDepth(int depth)
    {
        return Math.Clamp(depth, ScanExport.MinDepth, ScanExport.MaxDepth);
    }

    internal static void ValidateScanPath(string path)
    {
        if (path.Length == 0)
        {
            throw new McpException("Путь к каталогу должен быть задан.");
        }

        if (!Directory.Exists(path))
        {
            throw new McpException($"Каталог «{path}» не найден или недоступен.");
        }
    }

    internal static void Validate(string left, string right, SyncMode mode)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            throw new McpException("Оба каталога должны быть заданы.");
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            throw new McpException("Каталоги совпадают или вложены друг в друга.");
        }

        if (SyncProfile.SourceMissing(left, right, mode))
        {
            throw new McpException("Каталог-источник недоступен.");
        }
    }

    private static string? DescribeUnavailable(SyncProfile profile)
    {
        return OverviewPipeline.Classify(profile) switch
        {
            OverviewRunStatus.Unavailable => "каталог недоступен или не задан",
            OverviewRunStatus.Overlap => "каталоги совпадают или вложены",
            _ => null,
        };
    }

    private static McpDrive Describe(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady)
            {
                return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0, "–", "–", "–", "диск не готов");
            }

            var total = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            var used = Math.Max(0, total - free);

            return new(
                drive.Name,
                drive.VolumeLabel,
                drive.DriveType.ToString(),
                drive.DriveFormat,
                true,
                total,
                free,
                used,
                SizeFormatter.Format(total),
                SizeFormatter.Format(free),
                SizeFormatter.Format(used),
                null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0, "–", "–", "–", exception.Message);
        }
    }

    private static async Task<string> BuildJsonAsync(Func<ComparisonExportModel> build, CancellationToken cancellationToken)
    {
        var model = await Task.Run(build, cancellationToken).ConfigureAwait(false);

        return ComparisonExport.ToJson(model);
    }

    private static async Task<string> BuildScanJsonAsync(Func<ScanExportModel> build, CancellationToken cancellationToken)
    {
        var model = await Task.Run(build, cancellationToken).ConfigureAwait(false);

        return ScanExport.ToJson(model);
    }

    private static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, JsonOptions);
    }

    private static T Dispatch<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return dispatcher.Invoke(
                action,
                DispatcherPriority.Normal,
                CancellationToken.None,
                TimeSpan.FromSeconds(AppDefaults.McpDispatchTimeoutSeconds));
        }
        catch (TimeoutException)
        {
            throw new McpException("Окно приложения занято и не ответило вовремя – повторите позже.");
        }
    }

    private Task<string> ExportCurrentAsync(int entryLimit, CancellationToken cancellationToken)
    {
        var build = Dispatch(() => sync.CaptureExportBuilder(entryLimit));

        if (build is null)
        {
            throw new McpException("На странице «Синхронизация» сравнение ещё не выполнялось. Запустите open_sync с compare=true или compare_directories.");
        }

        return BuildJsonAsync(build, cancellationToken);
    }

    private McpScanState ReadScanState()
    {
        return new(
            scan.SelectedDrive,
            scan.ResultPath,
            scan.IsScanning,
            scan.HasResult,
            scan.ResultSizeText,
            scan.ResultFileCountText,
            scan.ResultDirCountText,
            scan.MarkedCount);
    }

    private McpSyncState ReadSyncState()
    {
        return new(
            sync.LeftPath,
            sync.RightPath,
            sync.CurrentMode,
            sync.CurrentWinner,
            sync.Mirror,
            sync.Exclusions,
            sync.IsBusy,
            sync.HasResult,
            new Dictionary<string, int>
            {
                ["Identical"] = sync.IdenticalCount,
                ["LeftOnly"] = sync.LeftOnlyCount,
                ["RightOnly"] = sync.RightOnlyCount,
                ["Modified"] = sync.ModifiedCount,
                ["Conflict"] = sync.ConflictCount,
            });
    }
}
