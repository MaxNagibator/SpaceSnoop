using ModelContextProtocol;
using SpaceSnoop.Core.Export;
using System.Diagnostics;
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

    public event Action<string>? NavigationDeferred;

    public bool DeferNavigation { get; set; }

    public void Attach(ShellViewModel shell)
    {
        _shell = shell;
    }

    public string GetState()
    {
        logger.McpToolInvoked("get_app_state", "-");

        return Dispatch(() => Serialize(new McpAppState(AppInfo.Name,
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
            .Select(static profile => new McpProfile(profile.Id,
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

        var model = await Task.Run(() =>
                {
                    var comparer = new DirectoryComparer(new(patterns), comparerLogger);
                    var result = comparer.Compare(left, right, cancellationToken);
                    result.ApplyMode(mode, mirror, winner);

                    return ComparisonExport.Build(result, new(mode, winner, mirror, patterns), AppInfo.Version, entryLimit);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ComparisonExport.ToJson(model);
    }

    public async Task<string> ScanAsync(string path, int depth, int entryLimit, bool show, CancellationToken cancellationToken)
    {
        path = path.Trim();
        depth = ClampDepth(depth);
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("scan_directory", $"«{path}», глубина {depth}, записей до {entryLimit}, показать в окне {show}");

        ValidateScanPath(path);

        if (show)
        {
            Dispatch(() =>
            {
                if (scan.IsScanning)
                {
                    logger.McpToolRejected("scan_directory", "страница занята операцией");
                    throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
                }
            });
        }

        // Те же параметры обхода, что и у человека на странице «Сканирование».
        var multithreaded = scanPreferences.UseMultithreading;
        var parallelism = scanPreferences.MaxParallelism;
        var stopwatch = Stopwatch.StartNew();

        var (tree, model) = await Task.Run(() =>
                {
                    var directory = new DirectoryInfo(path);

                    var root = multithreaded
                        ? calculator.CalculateMultithreaded(directory, parallelism, cancellationToken)
                        : calculator.Calculate(directory, cancellationToken);

                    return (root, ScanExport.Build(root, directory.FullName, new(depth, multithreaded, parallelism), AppInfo.Version, entryLimit));
                },
                cancellationToken)
            .ConfigureAwait(false);

        stopwatch.Stop();

        if (show)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Dispatch(() =>
            {
                scan.ApplyScanResult(path, tree, stopwatch.Elapsed);
                scan.SelectPathForAutomation(tree.AbsolutePath);
                DeferOrNavigate(SectionKey.Scan);
                notifier.Notify($"Агент показал сканирование: {tree.AbsolutePath} · {tree.TotalSizeText}");
            });
        }

        return ScanExport.ToJson(model);
    }

    public string DescribeContext()
    {
        return Dispatch(() =>
        {
            List<string> parts = [$"страница «{DescribePage(_shell?.CurrentSectionKey)}»"];

            if (scan.IsScanning)
            {
                parts.Add($"идёт сканирование {scan.SelectedDrive}");
            }
            else if (scan.HasResult)
            {
                parts.Add($"открыт скан {scan.ResultPath} – {scan.ResultSizeText}, файлов {scan.ResultFileCountText}");
            }

            if (scan.MarkedCount > 0)
            {
                parts.Add($"помечено на удаление {scan.MarkedCount}");
            }

            if (sync.HasResult)
            {
                parts.Add($"открыто сравнение {sync.LeftPath} → {sync.RightPath}, различий {sync.LeftOnlyCount + sync.RightOnlyCount + sync.ModifiedCount + sync.ConflictCount}");
            }

            return $"[Состояние окна SpaceSnoop: {string.Join("; ", parts)}. Это служебная справка, отвечать на неё не надо.]";
        });
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

        var run = Dispatch(() => PrepareScanNavigation(path, start, cancellationToken));

        if (run.Run is not null)
        {
            await run.Run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Dispatch(() => Serialize(new McpScanNavigation(_shell?.CurrentSectionKey ?? SectionKey.Scan,
            ReadScanState(),
            DescribeDeferredNavigation(run.Deferred))));
    }

    public string CaptureView(string? section, string? element, double scale)
    {
        section = section?.Trim();
        element = element?.Trim();
        scale = Math.Clamp(scale, AppDefaults.ViewCaptureScaleMin, AppDefaults.ViewCaptureScaleMax);

        logger.McpToolInvoked("capture_view", $"страница {(section is { Length: > 0 } ? section : "текущая")}, элемент {(element is { Length: > 0 } ? element : "всё окно")}, масштаб {scale:0.##}");

        var deferred = false;

        if (section is { Length: > 0 })
        {
            if (!SectionKey.All.Contains(section, StringComparer.OrdinalIgnoreCase))
            {
                throw new McpException($"Неизвестная страница «{section}». Доступны: {string.Join(", ", SectionKey.All)}.");
            }

            deferred = Dispatch(() => DeferOrNavigate(section));
        }

        Dispatch(static () => true, DispatcherPriority.ContextIdle);

        return Dispatch(() => CaptureCore(element, scale, deferred));
    }

    public async Task<string> ArchiveDirectoryAsync(string path, bool deleteOriginal, bool dryRun, CancellationToken cancellationToken)
    {
        path = path.Trim();

        if (dryRun)
        {
            logger.McpToolInvoked("archive_directory", $"«{path}», план");

            return Dispatch(() => BuildArchivePlan(path, deleteOriginal));
        }

        if (!preferences.AllowMutations)
        {
            logger.McpToolRejected("archive_directory", "изменяющие операции запрещены");
            throw new McpException("Изменяющие операции запрещены. Включите «Разрешить изменяющие операции» в настройках приложения.");
        }

        var (prepared, run) = Dispatch(() => PrepareArchiveRun(path, deleteOriginal, cancellationToken));

        var dialog = await run.ConfigureAwait(false);

        return Dispatch(() =>
        {
            if (dialog.CreatedArchivePath is null)
            {
                throw new McpException($"Архив не создан: {dialog.StatusText}");
            }

            return Serialize(new McpArchiveResult(prepared.SourcePath,
                dialog.CreatedArchivePath,
                prepared.Files.Count,
                dialog.OriginalDeleted,
                dialog.StatusText,
                ReadScanState()));
        });
    }

    public string MarkForDeletion(IReadOnlyList<string> paths, bool mark)
    {
        if (!preferences.AllowMutations)
        {
            logger.McpToolRejected("mark_for_deletion", "изменяющие операции запрещены");
            throw new McpException("Изменяющие операции запрещены. Включите «Разрешить изменяющие операции» в настройках приложения.");
        }

        logger.McpToolInvoked("mark_for_deletion", $"путей {paths.Count}, пометить {mark}");

        if (paths.Count == 0)
        {
            throw new McpException("Список путей пуст.");
        }

        if (paths.Count > AppDefaults.McpEntryLimitMax)
        {
            throw new McpException($"За один вызов можно пометить не больше {AppDefaults.McpEntryLimitMax} путей.");
        }

        return Dispatch(() => MarkOnScanPage(paths, mark));
    }

    public async Task<string> GetDockerUsageAsync(bool includeObjects, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("docker_usage", $"объекты {includeObjects}, записей до {entryLimit}");

        var snapshot = await docker.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

        var report = await BuildDockerReportAsync(snapshot, includeObjects, entryLimit, cancellationToken).ConfigureAwait(false);
        return Serialize(report);
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

        var comparison = Dispatch(() => PrepareSyncNavigation(left, right, mode, winner, mirror, exclusions, compare, cancellationToken));

        if (comparison.Run is not null)
        {
            await comparison.Run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Dispatch(() => Serialize(new McpNavigationResult(_shell?.CurrentSectionKey ?? SectionKey.Sync,
            ReadSyncState(),
            DescribeDeferredNavigation(comparison.Deferred),
            comparison.Ignored.Count == 0 ? null : $"Параметры {string.Join(", ", comparison.Ignored)} не применены: изменяющие операции выключены в настройках приложения.")));
    }

    public async Task<string> SyncCurrentAsync(bool dryRun, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = ClampEntryLimit(entryLimit);

        if (dryRun)
        {
            logger.McpToolInvoked("sync_current", $"план, записей до {entryLimit}");

            var plan = Dispatch(() => sync.CapturePlanBuilder(entryLimit));

            if (plan is null)
            {
                throw new McpException("На странице «Синхронизация» сравнение ещё не выполнялось. Запустите open_sync с compare=true или compare_directories.");
            }

            var model = await Task.Run(plan, cancellationToken).ConfigureAwait(false);

            return SyncPlanExport.ToJson(model);
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

            return sync.SyncFromAutomationAsync(cancellationToken);
        });

        var report = await run.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (report is null)
        {
            throw new McpException("Синхронизация не доведена до конца: операция отменена или сравнение сброшено. Часть файлов могла быть уже перенесена – сравните каталоги заново.");
        }

        return Dispatch(() => Serialize(new McpSyncResult(report.CopiedCount,
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

    internal static string DescribePage(string? sectionKey)
    {
        return sectionKey switch
        {
            SectionKey.Scan => "Сканирование",
            SectionKey.Sync => "Синхронизация",
            SectionKey.Overview => "Обзор",
            SectionKey.Schedule => "Расписание",
            SectionKey.Docker => "Docker",
            SectionKey.Chat => "Чат",
            SectionKey.Logs => "Логи",
            SectionKey.About => "О программе",
            _ => "неизвестно",
        };
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
                return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0,
                    "–", "–", "–", "диск не готов");
            }

            var total = drive.TotalSize;
            var free = drive.TotalFreeSpace;
            var used = Math.Max(0, total - free);

            return new(drive.Name,
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
            return new(drive.Name, string.Empty, drive.DriveType.ToString(), null, false, 0, 0, 0,
                "–", "–", "–", exception.Message);
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

    private static string? DescribeDeferredNavigation(bool deferred)
    {
        return deferred
            ? "Страница подготовлена, но не открыта: идёт разговор в чате. Переход человек сделает кнопкой в ленте – пересказывать путь словами всё равно нужно."
            : null;
    }

    private static void Dispatch(Action action)
    {
        Dispatch(() =>
        {
            action();
            return true;
        });
    }

    private static T Dispatch<T>(Func<T> action, DispatcherPriority priority = DispatcherPriority.Normal)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        try
        {
            return dispatcher.Invoke(action,
                priority,
                CancellationToken.None,
                TimeSpan.FromSeconds(AppDefaults.McpDispatchTimeoutSeconds));
        }
        catch (TimeoutException)
        {
            throw new McpException("Окно приложения занято и не ответило вовремя – повторите позже.");
        }
    }

    private (Task? Run, bool Deferred) PrepareScanNavigation(string? path, bool start, CancellationToken cancellationToken)
    {
        if (scan.IsScanning)
        {
            logger.McpToolRejected("open_scan", "страница занята операцией");
            throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
        }

        var explicitPath = !string.IsNullOrWhiteSpace(path);
        var target = explicitPath ? path!.Trim() : scan.SelectedDrive.Trim();

        if (start || explicitPath)
        {
            ValidateScanPath(target);
        }

        if (target.Length > 0)
        {
            scan.SelectPathForAutomation(target);
        }

        var deferred = DeferOrNavigate(SectionKey.Scan);
        notifier.Notify(start
            ? $"Агент запустил сканирование: {target}"
            : deferred
                ? "Агент подготовил страницу «Сканирование»"
                : "Агент открыл страницу «Сканирование»");

        return (start ? scan.ScanFromAutomationAsync(target, cancellationToken) : null, deferred);
    }

    private string MarkOnScanPage(IReadOnlyList<string> paths, bool mark)
    {
        if (!scan.HasResult)
        {
            throw new McpException("На странице «Сканирование» результата ещё нет. Запустите open_scan с scan=true.");
        }

        if (scan.IsScanning)
        {
            logger.McpToolRejected("mark_for_deletion", "страница занята операцией");
            throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
        }

        var (targets, missing, rejected) = CollectMarkTargets(paths, mark);
        var changed = scan.MarkForAutomation(targets, mark);

        if (changed > 0)
        {
            logger.McpMutationRequested("mark_for_deletion", $"{(mark ? "помечено" : "снято")} {changed}, всего помечено {scan.MarkedCount}");

            notifier.Notify(mark
                    ? $"Агент пометил на удаление: {changed} · всего {SizeFormatter.Format(scan.MarkedBytes())}"
                    : $"Агент снял пометку удаления: {changed}",
                StatusSeverity.Warning);
        }

        return Serialize(new McpMarkResult(changed,
            missing,
            rejected,
            rejected.Count == 0 ? null : "Корень открытого сканирования пометить целиком нельзя – выберите подкаталоги.",
            scan.MarkedCount,
            SizeFormatter.Format(scan.MarkedBytes()),
            ReadScanState()));
    }

    private (List<SpaceBase> Targets, List<string> Missing, List<string> Rejected) CollectMarkTargets(IReadOnlyList<string> paths, bool mark)
    {
        List<SpaceBase> targets = [];
        List<string> missing = [];
        List<string> rejected = [];

        foreach (var path in paths.Select(static path => path.Trim()).Where(static path => path.Length > 0))
        {
            if (scan.FindForAutomation(path) is not { } space)
            {
                missing.Add(path);
            }
            else if (mark && scan.IsScanRoot(space))
            {
                rejected.Add(path);
            }
            else
            {
                targets.Add(space);
            }
        }

        return (targets, missing, rejected);
    }

    private (Task? Run, bool Deferred, IReadOnlyList<string> Ignored) PrepareSyncNavigation(
        string? left,
        string? right,
        SyncMode? mode,
        SyncWinner? winner,
        bool? mirror,
        string? exclusions,
        bool compare,
        CancellationToken cancellationToken)
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

        ApplySyncPaths(targetLeft, targetRight, targetMode, exclusions);
        var ignored = ApplyGuardedSyncParameters(winner, mirror);

        var deferred = DeferOrNavigate(SectionKey.Sync);

        notifier.Notify(compare
            ? $"Агент запустил сравнение: {targetLeft} → {targetRight}"
            : deferred
                ? "Агент подготовил страницу «Синхронизация»"
                : "Агент открыл страницу «Синхронизация»");

        return (compare ? sync.CompareFromAutomationAsync(cancellationToken) : null, deferred, ignored);
    }

    private void ApplySyncPaths(string left, string right, SyncMode mode, string? exclusions)
    {
        if (!string.Equals(sync.LeftPath, left, StringComparison.Ordinal))
        {
            sync.LeftPath = left;
        }

        if (!string.Equals(sync.RightPath, right, StringComparison.Ordinal))
        {
            sync.RightPath = right;
        }

        sync.SelectedModeIndex = SyncProfile.IndexOfMode(mode);

        if (exclusions is not null)
        {
            sync.Exclusions = exclusions.Trim();
        }
    }

    private IReadOnlyList<string> ApplyGuardedSyncParameters(SyncWinner? winner, bool? mirror)
    {
        List<string> ignored = [];
        var allowed = preferences.AllowMutations;

        if (winner is { } side)
        {
            if (allowed)
            {
                sync.SelectedWinnerIndex = SyncProfile.IndexOfWinner(side);
            }
            else
            {
                ignored.Add("winner");
            }
        }

        if (mirror is { } enabled)
        {
            if (allowed)
            {
                sync.Mirror = enabled;
            }
            else
            {
                ignored.Add("mirror");
            }
        }

        if (ignored.Count > 0)
        {
            logger.McpToolRejected("open_sync", $"параметры {string.Join(", ", ignored)} требуют разрешённых изменяющих операций");
        }

        return ignored;
    }

    private string BuildArchivePlan(string path, bool deleteOriginal)
    {
        var (_, request) = PrepareArchive(path, deleteOriginal);

        return Serialize(new McpArchivePlan(request.SourcePath,
            request.TargetPath,
            request.Files.Count,
            request.TotalBytes,
            SizeFormatter.Format(request.TotalBytes),
            request.DeleteOriginal));
    }

    private (ArchiveRequest Request, Task<ArchiveProgressDialogViewModel> Run) PrepareArchiveRun(string path, bool deleteOriginal, CancellationToken cancellationToken)
    {
        if (scan.IsScanning)
        {
            logger.McpToolRejected("archive_directory", "страница занята операцией");
            throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
        }

        var (dir, request) = PrepareArchive(path, deleteOriginal);
        logger.McpMutationRequested("archive_directory", $"«{request.SourcePath}» → «{request.TargetPath}», файлов {request.Files.Count}, оригинал в корзину {request.DeleteOriginal}");
        notifier.Notify($"Агент упаковывает в архив: {request.SourcePath}", StatusSeverity.Warning);

        return (request, scan.ArchiveFromAutomationAsync(dir, request, cancellationToken));
    }

    private async Task<McpDockerReport> BuildDockerReportAsync(
        DockerSnapshot snapshot,
        bool includeObjects,
        int entryLimit,
        CancellationToken cancellationToken)
    {
        var buckets = snapshot.Buckets
            .Select(static bucket => new McpDockerBucket(bucket.Type,
                bucket.TotalCount,
                bucket.Active,
                bucket.Size,
                DockerSize.ToBytes(bucket.Size),
                bucket.Reclaimable,
                DockerSize.ToBytes(bucket.Reclaimable)))
            .ToList();

        if (!snapshot.Available || !includeObjects)
        {
            return new(snapshot.Available, snapshot.Error, buckets, null, 0);
        }

        var inventory = await docker.GetInventoryAsync(cancellationToken).ConfigureAwait(false);
        var objects = inventory
            .OrderByDescending(static item => item.SizeBytes)
            .Take(entryLimit)
            .Select(static item => new McpDockerObject(item.Kind, item.Id, item.Name, item.Size, item.SizeBytes, item.InUse, item.Detail))
            .ToList();

        return new(true, snapshot.Error, buckets, objects, Math.Max(0, inventory.Count - objects.Count));
    }

    private bool DeferOrNavigate(string sectionKey)
    {
        if (DeferNavigation)
        {
            NavigationDeferred?.Invoke(sectionKey);
            return true;
        }

        _shell?.TryNavigate(sectionKey);
        return false;
    }

    private string CaptureCore(string? element, double scale, bool deferred)
    {
        if (Application.Current?.MainWindow is not { } window)
        {
            throw new McpException("Окно приложения не открыто – снимать нечего.");
        }

        window.UpdateLayout();

        var named = element is { Length: > 0 };

        var target = named
            ? ViewCapture.Find(window, element!) ?? throw new McpException($"В окне нет элемента с именем «{element}». Открыты, например: {string.Join(", ", ViewCapture.Names(window, AppDefaults.ViewCaptureNamesHint))}.")
            : window;

        var page = _shell?.CurrentSectionKey ?? "-";
        var label = named ? $"{page}-{element}" : page;
        var path = Path.Combine(ViewCapture.DirectoryPath, ViewCapture.FileName(label, DateTimeOffset.Now));

        ViewCapture.DropObsolete(ViewCapture.DirectoryPath, AppDefaults.ViewCaptureLimit - 1, logger.ViewCaptureFailed);

        int width;
        int height;

        try
        {
            (width, height) = ViewCapture.Save(target, path, scale);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.ViewCaptureFailed(exception, path);
            throw new McpException($"Снимок не записан в «{path}»: {exception.Message}");
        }

        if (width == 0 || height == 0)
        {
            throw new McpException(named
                ? $"Элемент «{element}» не отрисован – он скрыт или ещё не построен на текущей странице."
                : "Окно не отрисовано – оно свёрнуто или ещё не показано.");
        }

        logger.ViewCaptured(path, width, height);

        return Serialize(new McpCapture(path,
            page,
            named ? element : null,
            ThemeManager.Current?.Key ?? "-",
            width,
            height,
            DescribeDeferredNavigation(deferred)));
    }

    private (DirectorySpace Dir, ArchiveRequest Request) PrepareArchive(string path, bool deleteOriginal)
    {
        if (path.Length == 0)
        {
            throw new McpException("Путь к каталогу должен быть задан.");
        }

        if (scan.FindForAutomation(path) is not { } space)
        {
            throw new McpException($"Каталог «{path}» не найден в открытом сканировании. Упаковывается только то, что уже отсканировано: откройте дерево через open_scan с scan=true.");
        }

        if (space is not DirectorySpace dir)
        {
            throw new McpException($"«{path}» – файл, а упаковать можно только каталог.");
        }

        if (scan.IsScanRoot(space) || dir.Parent is not DirectorySpace)
        {
            throw new McpException($"«{path}» – корень сканирования, архив некуда положить. Выберите подкаталог.");
        }

        return (dir, scan.CreateArchiveRequest(dir, deleteOriginal));
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
        return new(scan.SelectedDrive,
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
        return new(sync.LeftPath,
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
