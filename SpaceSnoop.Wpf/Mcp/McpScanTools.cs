using ModelContextProtocol;
using SpaceSnoop.Core.Export;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpScanTools(
    IScanAutomation scan,
    ScanPreferences scanPreferences,
    DiskSpaceCalculator calculator,
    DuplicateFinder duplicates,
    McpPreferences preferences,
    ToastNotifier notifier,
    McpNavigator navigator,
    McpStateReader state,
    PerformanceMonitor performance,
    PerformanceRunTracker runs,
    ILogger logger)
{
    public async Task<string> ScanAsync(string path, int depth, int entryLimit, bool show, CancellationToken cancellationToken)
    {
        path = path.Trim();
        depth = McpGuards.ClampDepth(depth);
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("scan_directory", $"«{path}», глубина {depth}, записей до {entryLimit}, показать в окне {show}");

        path = McpGuards.ValidateScanPath(path);

        if (show)
        {
            McpDispatch.Run(() =>
            {
                if (scan.IsScanning)
                {
                    logger.McpToolRejected("scan_directory", "страница занята операцией");
                    throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
                }
            });
        }

        // Те же параметры обхода, что и у человека на странице «Сканирование».
        var parallelism = scanPreferences.ResolveParallelism(path);
        var multithreaded = parallelism > 1;

        var directory = new DirectoryInfo(path);

        // Замер останавливается до сборки выгрузки: она обходит дерево ещё раз, и «время скана»
        // в окне означало бы не то же, что «время скана» у агента.
        var phases = new ScanPhases();

        var (tree, model, run) = await Task.Run(() =>
                {
                    using var probe = new BackgroundScanProbe(performance,
                        runs,
                        ScanProgressViewModel.EstimateTotalBytes(directory),
                        parallelism);

                    var root = parallelism > 1
                        ? calculator.CalculateMultithreaded(directory, parallelism, probe.Progress, cancellationToken)
                        : calculator.Calculate(directory, probe.Progress, cancellationToken);

                    var walked = probe.Finish();
                    phases.WalkMs = (long)walked.Elapsed.TotalMilliseconds;

                    var stopwatch = Stopwatch.StartNew();
                    var built = ScanExport.Build(root, directory.FullName, new(depth, multithreaded, parallelism), AppInfo.Version, entryLimit);
                    phases.ExportMs = stopwatch.ElapsedMilliseconds;

                    return (root, built, walked);
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (show)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();

            McpDispatch.Run(() =>
            {
                if (scan.IsScanning)
                {
                    logger.McpToolRejected("scan_directory", "страница занялась операцией, пока шёл обход");
                    throw new McpException("Страница «Сканирование» занялась другой операцией, пока шёл обход – результат не показан. Повторите с show=false, чтобы получить данные без окна.");
                }

                scan.ApplyScanResult(tree, run.Elapsed, run.Traversal);
                scan.SelectPathForAutomation(tree.AbsolutePath);
                navigator.DeferOrNavigate(SectionKey.Scan);
                notifier.Notify($"Агент показал сканирование: {tree.AbsolutePath} · {tree.TotalSizeText}");
            });

            phases.ApplyMs = stopwatch.ElapsedMilliseconds;
        }

        var serializing = Stopwatch.StartNew();
        var json = ScanExport.ToJson(model);
        phases.JsonMs = serializing.ElapsedMilliseconds;

        logger.McpScanPhases(phases.WalkMs, phases.ExportMs, phases.JsonMs, phases.ApplyMs, json.Length);

        return json;
    }

    private sealed class ScanPhases
    {
        public long WalkMs { get; set; }

        public long ExportMs { get; set; }

        public long JsonMs { get; set; }

        public long ApplyMs { get; set; }
    }

    public Task<string> GetCurrentScanAsync(int depth, int entryLimit, CancellationToken cancellationToken)
    {
        depth = McpGuards.ClampDepth(depth);
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("get_current_scan", $"глубина {depth}, записей до {entryLimit}");

        var build = McpDispatch.Run(() => scan.CaptureExportBuilder(depth, entryLimit));

        if (build is null)
        {
            throw new McpException("На странице «Сканирование» результата ещё нет. Запустите open_scan с scan=true или scan_directory.");
        }

        return BuildScanJsonAsync(build, cancellationToken);
    }

    public async Task<string> FindDuplicatesAsync(long minSize, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);
        minSize = Math.Max(1, minSize);

        logger.McpToolInvoked("find_duplicates", $"порог {minSize} Б, записей до {entryLimit}");

        var root = McpDispatch.Run(scan.CaptureScanRoot)
                   ?? throw new McpException("На странице «Сканирование» результата ещё нет. Запустите scan_directory с show=true или open_scan с scan=true.");

        var options = DuplicateOptions.Default with
        {
            MinSize = minSize,
            MaxParallelism = scanPreferences.ResolveParallelism(root.AbsolutePath),
            GroupLimit = entryLimit,
            MemberLimit = entryLimit,
        };

        var report = await duplicates.FindAsync(root, options, null, cancellationToken).ConfigureAwait(false);

        return McpFormat.Serialize(DescribeDuplicates(root.AbsolutePath, minSize, report));
    }

    internal static McpDuplicates DescribeDuplicates(string root, long minSize, DuplicateReport report)
    {
        return new(root,
            minSize,
            report.Examined,
            report.Groups.Count,
            report.OmittedGroups,
            report.ReclaimableBytes,
            SizeFormatter.Format(report.ReclaimableBytes),
            report.UnreadableDirectories,
            report.Errors.Count,
            [.. report.Groups.Select(static x => new McpDuplicateGroup(x.Size,
                SizeFormatter.Format(x.Size),
                x.Members.Count,
                x.DistinctFiles,
                x.ReclaimableBytes,
                SizeFormatter.Format(x.ReclaimableBytes),
                x.OmittedMembers,
                [.. x.Members.Select(static member => new McpDuplicateMember(member.Path, member.Kind, member.ReclaimsSpace))]))]);
    }

    public async Task<string> OpenScanAsync(string? path, bool start, CancellationToken cancellationToken)
    {
        logger.McpToolInvoked("open_scan", $"«{path ?? "как есть"}», сканирование {start}");

        var run = McpDispatch.Run(() => PrepareScanNavigation(path, start, cancellationToken));

        if (run.Run is not null)
        {
            await run.Run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return McpDispatch.Run(() => McpFormat.Serialize(new McpScanNavigation(navigator.CurrentSectionKey ?? SectionKey.Scan,
            state.ReadScanState(),
            McpFormat.DescribeDeferredNavigation(run.Deferred))));
    }

    public string MarkForDeletion(IReadOnlyList<string> paths, bool mark)
    {
        McpGuards.RequireMutations(preferences, logger, "mark_for_deletion");

        List<string> wanted = [.. paths.Where(static path => path is not null).Select(static path => path.Trim()).Where(static path => path.Length > 0)];

        logger.McpToolInvoked("mark_for_deletion", $"путей {wanted.Count}, пометить {mark}");

        if (wanted.Count == 0)
        {
            throw new McpException("Список путей пуст.");
        }

        if (wanted.Count > AppDefaults.McpEntryLimitMax)
        {
            throw new McpException($"За один вызов можно пометить не больше {AppDefaults.McpEntryLimitMax} путей.");
        }

        return McpDispatch.Run(() => MarkOnScanPage(wanted, mark));
    }

    public async Task<string> ArchiveDirectoryAsync(string path, bool deleteOriginal, bool dryRun, CancellationToken cancellationToken)
    {
        path = path.Trim();

        if (dryRun)
        {
            logger.McpToolInvoked("archive_directory", $"«{path}», план");

            return McpDispatch.Run(() => BuildArchivePlan(path, deleteOriginal));
        }

        McpGuards.RequireMutations(preferences, logger, "archive_directory");

        var (prepared, run) = McpDispatch.Run(() => PrepareArchiveRun(path, deleteOriginal, cancellationToken));

        var outcome = await run.ConfigureAwait(false);

        return McpDispatch.Run(() =>
        {
            if (outcome.ArchivePath is null)
            {
                throw new McpException($"Архив не создан: {outcome.StatusText}");
            }

            return McpFormat.Serialize(new McpArchiveResult(prepared.SourcePath,
                outcome.ArchivePath,
                prepared.EstimatedFiles,
                outcome.OriginalDeleted,
                outcome.StatusText,
                state.ReadScanState()));
        });
    }

    private static async Task<string> BuildScanJsonAsync(Func<ScanExportModel> build, CancellationToken cancellationToken)
    {
        var model = await Task.Run(build, cancellationToken).ConfigureAwait(false);

        return ScanExport.ToJson(model);
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
            target = McpGuards.ValidateScanPath(target);
        }

        if (target.Length > 0)
        {
            scan.SelectPathForAutomation(target);
        }

        var deferred = navigator.DeferOrNavigate(SectionKey.Scan);

        notifier.Notify((start, deferred) switch
        {
            (true, _) => $"Агент запустил сканирование: {target}",
            (false, true) => "Агент подготовил страницу «Сканирование»",
            _ => "Агент открыл страницу «Сканирование»",
        });

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

        return McpFormat.Serialize(new McpMarkResult(changed,
            missing,
            rejected,
            rejected.Count == 0 ? null : "Корень открытого сканирования пометить целиком нельзя – выберите подкаталоги.",
            scan.MarkedCount,
            SizeFormatter.Format(scan.MarkedBytes()),
            state.ReadScanState()));
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

    private string BuildArchivePlan(string path, bool deleteOriginal)
    {
        var (_, request) = PrepareArchive(path, deleteOriginal);

        return McpFormat.Serialize(new McpArchivePlan(request.SourcePath,
            request.TargetPath,
            request.EstimatedFiles,
            request.TotalBytes,
            SizeFormatter.Format(request.TotalBytes),
            request.DeleteOriginal));
    }

    private (ArchiveRequest Request, Task<ArchiveOutcome> Run) PrepareArchiveRun(string path, bool deleteOriginal, CancellationToken cancellationToken)
    {
        if (scan.IsScanning)
        {
            logger.McpToolRejected("archive_directory", "страница занята операцией");
            throw new McpException("Страница «Сканирование» сейчас занята другой операцией.");
        }

        var (dir, request) = PrepareArchive(path, deleteOriginal);
        logger.McpMutationRequested("archive_directory", $"«{request.SourcePath}» → «{request.TargetPath}», файлов ≈{request.EstimatedFiles}, оригинал в корзину {request.DeleteOriginal}");
        notifier.Notify($"Агент упаковывает в архив: {request.SourcePath}", StatusSeverity.Warning);

        return (request, scan.ArchiveFromAutomationAsync(dir, request, cancellationToken));
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
}
