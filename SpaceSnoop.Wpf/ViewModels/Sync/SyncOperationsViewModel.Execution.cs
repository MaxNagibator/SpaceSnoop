using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncOperationsViewModel
{
    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (Result is null)
        {
            return;
        }

        if (_planState.RefusalMessage is { } refusal)
        {
            _dialogs.Warning("Синхронизация", refusal);
            return;
        }

        if (Result.HasPendingResolution())
        {
            _dialogs.Warning("Неподтверждённые элементы", "Разрешите все неподтверждённые элементы перед синхронизацией.");
            return;
        }

        var hashes = new ConfirmChoice("Сверить хеши", ConfirmChoiceKind.Secondary);

        while (true)
        {
            if (Result is not { } current)
            {
                return;
            }

            var confirm = await _ledger.BuildSyncConfirmationAsync(current, hashes);

            if (confirm is null || !ReferenceEquals(Result, current))
            {
                return;
            }

            var shown = current.CountPlannedActions();

            if (!await _dialogs.ShowAsync(confirm))
            {
                return;
            }

            if (_session.IsBusy || !ReferenceEquals(Result, current) || current.CountPlannedActions() != shown)
            {
                return;
            }

            if (!ReferenceEquals(confirm.Chosen, hashes))
            {
                break;
            }

            await HashCommand.ExecuteAsync(null);

            if (!_ledger.HasActionableChanges() || Result is not { } hashed || hashed.HasPendingResolution())
            {
                return;
            }
        }

        await ExecuteSyncAsync(true, CancellationToken.None);
    }

    private async Task<SyncRunResult?> ExecuteSyncAsync(bool interactive, CancellationToken external = default)
    {
        if (Result is null)
        {
            return null;
        }

        if (_planState.RefusalMessage is { } refusal)
        {
            if (interactive)
            {
                _dialogs.Warning("Синхронизация", refusal);
                return null;
            }

            throw new SyncPlanStaleException(refusal);
        }

        var result = Result;
        var planned = _ledger.CurrentPlan;
        var stopwatch = Stopwatch.StartNew();

        _logger.SyncStarted(_setup.CurrentMode);

        var verify = _setup.Verify;
        var recycleOverwritten = _settings.GetBool(SettingsKeys.SyncRecycleOverwritten, AppDefaults.SyncRecycleOverwrittenDefault);

        var request = new ExecuteSyncRequest(result, SyncConflictPolicy.None, SyncDeleteUi.Interactive, verify, recycleOverwritten);

        var report = await _session.RunAsync(
            "Синхронизация:",
            (token, progress) => _sync.Execute(request, token, progress),
            planned.Total,
            external,
            planned.CopyBytes,
            true);

        stopwatch.Stop();

        var afterSync = _planState.AfterSync(report, _session.LastOperationCancelled || external.IsCancellationRequested);
        var same = ReferenceEquals(Result, result);

        if (same
            || (Result is { } replaced
                && await Task.Run(() => SharesRoot(replaced, result), CancellationToken.None)
                && ReferenceEquals(Result, replaced)))
        {
            ApplyPlanState(afterSync);
        }

        if (report is null)
        {
            if (afterSync.RefusalMessage is { } interrupted)
            {
                _notifier.Notify(interrupted, StatusSeverity.Warning);
            }

            DiscardComparisonIfInputChanged();
            return null;
        }

        var verifyState = SyncPlanNarrative.ResolveVerify(verify, report);

        if (same)
        {
            LastSyncElapsed = stopwatch.Elapsed;
            LastVerifyState = verifyState;
        }

        _logger.SyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

        if (verify)
        {
            _logger.SyncVerified(report.Applied.Count, report.Mismatches.Count);
        }

        SyncLog.AppendSafe(interactive ? SyncLogOrigin.Manual : SyncLogOrigin.Mcp, null, report, verifyState, _logger);

        if (same)
        {
            LastReport = report;
            Outcomes = SyncOutcomes.Build(result, report.Errors, report.Mismatches);
            RaiseComparisonChanged(SyncComparisonChange.Applied);
        }

        RaiseProfileRun(null, report, (long)stopwatch.Elapsed.TotalMilliseconds, verifyState);
        await ReadGitStateAsync();

        var verifyText = SyncPlanNarrative.DescribeVerify(verifyState, report.Mismatches.Count);
        var volumeText = report.CopiedBytes > 0 ? $" Перенесено: {SizeFormatter.Format(report.CopiedBytes)}." : string.Empty;
        var rateText = SyncSessionViewModel.DescribeRate("Синхронизация", report, stopwatch.Elapsed);
        var staleText = _planState.RefusalMessage is { } refused ? $" {refused}" : string.Empty;
        Report($"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с.{volumeText}{rateText} Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}{staleText}");

        var (toastMessage, toastSeverity) = SyncOutcomeNarrative.DescribeToast(report);
        _notifier.Notify(toastMessage, toastSeverity);

        if (interactive && SyncOutcomeNarrative.DescribeProblems(report) is { } problem)
        {
            _dialogs.Warning(problem.Title, problem.Message);
        }

        DiscardComparisonIfInputChanged();
        return new(report, stopwatch.Elapsed, verifyState);
    }

    private static bool SharesRoot(ComparisonResult comparison, ComparisonResult synced)
    {
        return SyncRoots.Overlap(comparison.LeftPath, synced.LeftPath)
            || SyncRoots.Overlap(comparison.LeftPath, synced.RightPath)
            || SyncRoots.Overlap(comparison.RightPath, synced.LeftPath)
            || SyncRoots.Overlap(comparison.RightPath, synced.RightPath);
    }
}
