using ModelContextProtocol;
using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.Mcp;

internal sealed class McpSyncTools(
    ISyncAutomation sync,
    CompareDirectoriesUseCase compareUseCase,
    McpPreferences preferences,
    ToastNotifier notifier,
    McpNavigator navigator,
    McpStateReader state,
    ILogger logger)
{
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
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        logger.McpToolInvoked("compare_directories", $"«{left}» → «{right}», режим {mode}, записей до {entryLimit}");

        McpGuards.Validate(left, right, mode);

        var patterns = exclusions?.Trim() ?? string.Empty;

        var model = await Task.Run(() =>
                {
                    var result = compareUseCase.Execute(new(left, right, patterns, mode, winner, mirror), cancellationToken);

                    return ComparisonExport.Build(result, new(mode, winner, mirror, patterns), AppInfo.Version, entryLimit);
                },
                cancellationToken)
            .ConfigureAwait(false);

        return ComparisonExport.ToJson(model);
    }

    public Task<string> GetCurrentComparisonAsync(int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);
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

        var comparison = McpDispatch.Run(() => PrepareSyncNavigation(left, right, mode, winner, mirror, exclusions, compare, cancellationToken));

        if (comparison.Run is not null)
        {
            await comparison.Run.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }

        return McpDispatch.Run(() => McpFormat.Serialize(new McpNavigationResult(navigator.CurrentSectionKey ?? SectionKey.Sync,
            state.ReadSyncState(),
            McpFormat.DescribeDeferredNavigation(comparison.Deferred),
            comparison.Ignored.Count == 0 ? null : $"Параметры {string.Join(", ", comparison.Ignored)} не применены: изменяющие операции выключены в настройках приложения.")));
    }

    public async Task<string> SyncCurrentAsync(bool dryRun, int entryLimit, CancellationToken cancellationToken)
    {
        entryLimit = McpGuards.ClampEntryLimit(entryLimit);

        if (dryRun)
        {
            logger.McpToolInvoked("sync_current", $"план, записей до {entryLimit}");

            var plan = McpDispatch.Run(() => sync.CapturePlanBuilder(entryLimit));

            if (plan is null)
            {
                throw new McpException("На странице «Синхронизация» сравнение ещё не выполнялось. Запустите open_sync с compare=true или compare_directories.");
            }

            var model = await Task.Run(plan, cancellationToken).ConfigureAwait(false);

            return SyncPlanExport.ToJson(model);
        }

        McpGuards.RequireMutations(preferences, logger, "sync_current");

        var run = McpDispatch.Run(() =>
        {
            if (!sync.HasComparison)
            {
                throw new McpException("Сначала выполните сравнение: open_sync с compare=true.");
            }

            if (sync.IsBusy)
            {
                throw new McpException("Страница «Синхронизация» сейчас занята другой операцией.");
            }

            if (sync.HasPendingConflicts)
            {
                throw new McpException("Есть неразрешённые спорные элементы – разрешите их в приложении.");
            }

            logger.McpMutationRequested("sync_current", $"«{sync.LeftPath}» → «{sync.RightPath}», режим {sync.Mode}, зеркало {sync.Mirror}");
            notifier.Notify("Агент запустил синхронизацию", StatusSeverity.Warning);

            return sync.SyncFromAutomationAsync(cancellationToken);
        });

        var outcome = await run.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (outcome is null)
        {
            throw new McpException("Синхронизация не доведена до конца: операция отменена или сравнение сброшено. Часть файлов могла быть уже перенесена – сравните каталоги заново.");
        }

        var report = outcome.Report;

        return McpDispatch.Run(() => McpFormat.Serialize(new McpSyncResult(report.CopiedCount,
            report.DeletedCount,
            report.SuccessCount,
            Math.Round(outcome.Elapsed.TotalSeconds, 2),
            report.CopiedBytes,
            SizeFormatter.Format(report.CopiedBytes),
            outcome.Verify,
            McpFormat.DescribeVerifyState(outcome.Verify, report.Errors.Count),
            report.Errors.Count,
            report.Mismatches.Count,
            Math.Max(0, report.Errors.Count - entryLimit),
            Math.Max(0, report.Mismatches.Count - entryLimit),
            [.. report.Errors.Take(entryLimit)],
            [.. report.Mismatches.Take(entryLimit)],
            sync.SummaryText,
            state.ReadSyncState())));
    }

    private static async Task<string> BuildJsonAsync(Func<ComparisonExportModel> build, CancellationToken cancellationToken)
    {
        var model = await Task.Run(build, cancellationToken).ConfigureAwait(false);

        return ComparisonExport.ToJson(model);
    }

    private Task<string> ExportCurrentAsync(int entryLimit, CancellationToken cancellationToken)
    {
        var build = McpDispatch.Run(() => sync.CaptureExportBuilder(entryLimit));

        if (build is null)
        {
            throw new McpException("На странице «Синхронизация» сравнение ещё не выполнялось. Запустите open_sync с compare=true или compare_directories.");
        }

        return BuildJsonAsync(build, cancellationToken);
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
        var targetMode = mode ?? sync.Mode;

        if (compare)
        {
            McpGuards.Validate(targetLeft, targetRight, targetMode);
        }

        ApplySyncPaths(targetLeft, targetRight, targetMode, exclusions);
        var ignored = ApplyGuardedSyncParameters(winner, mirror);

        var deferred = navigator.DeferOrNavigate(SectionKey.Sync);

        notifier.Notify((compare, deferred) switch
        {
            (true, _) => $"Агент запустил сравнение: {targetLeft} → {targetRight}",
            (false, true) => "Агент подготовил страницу «Синхронизация»",
            _ => "Агент открыл страницу «Синхронизация»",
        });

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

        sync.Mode = mode;

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
                sync.Winner = side;
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
}
