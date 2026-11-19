using ModelContextProtocol;
using SpaceSnoop.Core.Export;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpScanTools(
    IScanAutomation scan,
    ScanPreferences scanPreferences,
    ScanRunner runner,
    DuplicateFinder duplicates,
    McpPreferences preferences,
    ToastNotifier notifier,
    McpNavigator navigator,
    McpStateReader state,
    PerformanceOperations operations,
    NetworkDriveRestore networkDrives,
    ILogger logger)
{
    private const string BusyReason = "страница занята операцией";
    private const string ScanBusyMessage = "Страница «Сканирование» сейчас занята другой операцией.";
    private const string MarkTool = "mark_for_deletion";
    private const string ArchiveTool = "archive_directory";
    private static readonly TimeSpan NetworkDriveTimeout = TimeSpan.FromSeconds(AppDefaults.McpNetworkDriveTimeoutSeconds);

    public async Task<string> ScanAsync(string path, int depth, int entryLimit, bool show, CancellationToken cancellationToken)
    {
        path = path.Trim();
        depth = McpGuards.ClampDepth(depth);
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("scan_directory", $"«{path}», глубина {depth}, записей до {entryLimit}, показать в окне {show}");

        await networkDrives.WaitForPathAsync(path, NetworkDriveTimeout, cancellationToken).ConfigureAwait(false);
        path = McpGuards.ValidateScanPath(path, true);

        var parallelism = await scanPreferences.ResolveParallelismAsync(path).WaitAsync(cancellationToken).ConfigureAwait(false);
        var directory = new DirectoryInfo(path);
        var estimatedBytes = await Task.Run(() => ScanProgressViewModel.EstimateTotalBytes(directory), cancellationToken).ConfigureAwait(false);
        using var probe = new BackgroundScanProbe(
            operations,
            estimatedBytes,
            parallelism);

        try
        {
            probe.ReportPhase("Подготовка сканирования (агент)");

            if (show)
            {
                McpDispatch.Run(() =>
                {
                    if (scan.IsScanning)
                    {
                        logger.McpToolRejected("scan_directory", BusyReason);
                        throw new McpException(ScanBusyMessage);
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    scan.ReleaseBeforeScan(path);
                });
            }

            var phases = new ScanPhases();

            var (tree, model, run, notes) = await Task.Run(
                    () =>
                    {
                        probe.ReportPhase(BackgroundScanProbe.OperationName);
                        var outcome = runner.Run(directory, parallelism, probe.Progress, cancellationToken);

                        var walked = probe.Finish(false, outcome.Notes);
                        phases.WalkMs = (long)walked.Elapsed.TotalMilliseconds;

                        var stopwatch = Stopwatch.StartNew();
                        probe.ReportPhase("Экспорт сканирования");

                        var options = new ScanExportOptions(
                            depth,
                            outcome.Notes.Parallelism > 1,
                            outcome.Notes.Parallelism,
                            outcome.Notes.Engine);

                        var built = ScanExport.Build(outcome.Root, directory.FullName, options, AppInfo.Version, entryLimit) with
                        {
                            Notes = ScanExportNotes.From(
                                outcome.Notes.ExtraNameBytes,
                                outcome.Notes.DroppedObjects,
                                outcome.Notes.DroppedBytes,
                                outcome.Notes.UnknownSizeFiles,
                                outcome.Notes.PartialRecords),
                        };

                        phases.ExportMs = stopwatch.ElapsedMilliseconds;

                        return (outcome.Root, built, walked, outcome.Notes);
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (show)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var stopwatch = Stopwatch.StartNew();
                probe.ReportPhase("Показ результата сканирования");

                McpDispatch.Run(() =>
                {
                    if (scan.IsScanning)
                    {
                        logger.McpToolRejected("scan_directory", "страница занялась операцией, пока шёл обход");
                        throw new McpException(
                            "Страница «Сканирование» занялась другой операцией, пока шёл обход – результат не показан. Повторите с show=false, чтобы получить данные без окна.");
                    }

                    scan.ApplyScanResult(tree, run.Elapsed, run.Traversal, notes);
                    scan.SelectPathForAutomation(tree.AbsolutePath);
                    navigator.DeferOrNavigate(SectionKey.Scan);
                    notifier.Notify($"Агент показал сканирование: {tree.AbsolutePath} · {tree.TotalSizeText}");
                });

                phases.ApplyMs = stopwatch.ElapsedMilliseconds;

                await operations.FlushFramesAsync().ConfigureAwait(false);
                await McpDispatch.Run(() => scan.ReturnScanMemoryAsync(tree, probe.ReportPhase, cancellationToken)).ConfigureAwait(false);
            }

            probe.ReportPhase("Сериализация сканирования");
            var serializing = Stopwatch.StartNew();
            var json = ScanExport.ToJson(model);
            phases.JsonMs = serializing.ElapsedMilliseconds;

            logger.McpScanPhases(phases.WalkMs, phases.ExportMs, phases.JsonMs, phases.ApplyMs, json.Length);

            return json;
        }
        finally
        {
            await probe.CompleteMeasurementAsync().ConfigureAwait(false);
        }
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

        var parallelism = await scanPreferences.ResolveParallelismAsync(root.AbsolutePath).WaitAsync(cancellationToken).ConfigureAwait(false);

        var options = DuplicateOptions.Default with
        {
            MinSize = minSize,
            MaxParallelism = parallelism,
            GroupLimit = entryLimit,
            MemberLimit = entryLimit,
        };

        var report = await duplicates.FindAsync(root, options, null, cancellationToken).ConfigureAwait(false);

        return McpFormat.Serialize(DescribeDuplicates(root.AbsolutePath, minSize, report));
    }

    public async Task<string> OpenScanAsync(string? path, bool start, CancellationToken cancellationToken)
    {
        logger.McpToolInvoked("open_scan", $"«{path ?? "как есть"}», сканирование {start}");

        var candidate = string.Empty;

        if (!string.IsNullOrWhiteSpace(path))
        {
            candidate = path.Trim();
        }
        else if (start)
        {
            candidate = McpDispatch.Run(() => scan.SelectedDrive.Trim());
        }

        if (candidate.Length > 0)
        {
            await networkDrives.WaitForPathAsync(candidate, NetworkDriveTimeout, cancellationToken).ConfigureAwait(false);
        }

        var run = McpDispatch.Run(() => PrepareScanNavigation(path, start, cancellationToken));

        if (run.Run is not null)
        {
            await run.Run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return McpDispatch.Run(() => McpFormat.Serialize(
            new McpScanNavigation(
                navigator.CurrentSectionKey ?? SectionKey.Scan,
                state.ReadScanState(),
                McpFormat.DescribeDeferredNavigation(run.Deferred))));
    }

    public string MarkForDeletion(IReadOnlyList<string> paths, bool mark)
    {
        McpGuards.RequireMutations(preferences, logger, MarkTool);

        List<string> wanted = [.. paths.Where(static path => path is not null).Select(static path => path.Trim()).Where(static path => path.Length > 0)];

        logger.McpToolInvoked(MarkTool, $"путей {wanted.Count}, пометить {mark}");

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
            logger.McpToolInvoked(ArchiveTool, $"«{path}», план");

            return McpDispatch.Run(() => BuildArchivePlan(path, deleteOriginal));
        }

        McpGuards.RequireMutations(preferences, logger, ArchiveTool);

        var (prepared, run) = McpDispatch.Run(() => PrepareArchiveRun(path, deleteOriginal, cancellationToken));

        var outcome = await run.ConfigureAwait(false);

        return McpDispatch.Run(() =>
        {
            if (outcome.ArchivePath is null)
            {
                throw new McpException($"Архив не создан: {outcome.StatusText}");
            }

            return McpFormat.Serialize(
                new McpArchiveResult(
                    prepared.SourcePath,
                    outcome.ArchivePath,
                    prepared.EstimatedFiles,
                    outcome.OriginalDeleted,
                    outcome.StatusText,
                    state.ReadScanState()));
        });
    }

    internal static McpDuplicates DescribeDuplicates(string root, long minSize, DuplicateReport report)
    {
        return new(
            root,
            minSize,
            report.Examined,
            report.Groups.Count,
            report.OmittedGroups,
            report.ReclaimableBytes,
            SizeFormatter.Format(report.ReclaimableBytes),
            report.UnreadableDirectories,
            report.Errors.Count,
            [
                .. report.Groups.Select(static x => new McpDuplicateGroup(
                    x.Size,
                    SizeFormatter.Format(x.Size),
                    x.Members.Count,
                    x.DistinctFiles,
                    x.ReclaimableBytes,
                    SizeFormatter.Format(x.ReclaimableBytes),
                    x.OmittedMembers,
                    [.. x.Members.Select(static member => new McpDuplicateMember(member.Path, member.Kind, member.ReclaimsSpace))])),
            ]);
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
            logger.McpToolRejected("open_scan", BusyReason);
            throw new McpException(ScanBusyMessage);
        }

        var explicitPath = !string.IsNullOrWhiteSpace(path);
        var target = explicitPath ? path!.Trim() : scan.SelectedDrive.Trim();

        if (start || explicitPath)
        {
            target = McpGuards.ValidateScanPath(target, start);
        }

        if (target.Length > 0)
        {
            scan.SelectPathForAutomation(target);
        }

        var deferred = navigator.DeferOrNavigate(SectionKey.Scan);

        notifier.Notify(
            (start, deferred) switch
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
            logger.McpToolRejected(MarkTool, BusyReason);
            throw new McpException(ScanBusyMessage);
        }

        var (targets, missing, rejected) = CollectMarkTargets(paths, mark);
        var changed = scan.MarkForAutomation(targets, mark);

        if (changed > 0)
        {
            logger.McpMutationRequested(MarkTool, $"{(mark ? "помечено" : "снято")} {changed}, всего помечено {scan.MarkedCount}");

            notifier.Notify(
                mark
                    ? $"Агент пометил на удаление: {changed} · всего {SizeFormatter.Format(scan.MarkedBytes())}"
                    : $"Агент снял пометку удаления: {changed}",
                StatusSeverity.Warning);
        }

        return McpFormat.Serialize(
            new McpMarkResult(
                changed,
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

        return McpFormat.Serialize(
            new McpArchivePlan(
                request.SourcePath,
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
            logger.McpToolRejected(ArchiveTool, BusyReason);
            throw new McpException(ScanBusyMessage);
        }

        var (dir, request) = PrepareArchive(path, deleteOriginal);
        logger.McpMutationRequested(
            ArchiveTool,
            $"«{request.SourcePath}» → «{request.TargetPath}», файлов ≈{request.EstimatedFiles}, оригинал в корзину {request.DeleteOriginal}");

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
            throw new McpException(
                $"Каталог «{path}» не найден в открытом сканировании. Упаковывается только то, что уже отсканировано: откройте дерево через open_scan с scan=true.");
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

    private sealed class ScanPhases
    {
        public long WalkMs { get; set; }

        public long ExportMs { get; set; }

        public long JsonMs { get; set; }

        public long ApplyMs { get; set; }
    }
}
