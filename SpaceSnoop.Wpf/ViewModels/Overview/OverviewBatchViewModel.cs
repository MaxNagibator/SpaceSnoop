using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewBatchViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ToastNotifier _notifier;
    private readonly ILogger _logger;
    private readonly CompareDirectoriesUseCase _compare;
    private readonly ExecuteSyncUseCase _sync;
    private readonly OverviewRowsViewModel _rows;

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _rowCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareAllCommand), nameof(SyncRowCommand), nameof(SyncAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isBatchRunning;

    [ObservableProperty]
    private string _statusCaption = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax;

    internal OverviewBatchViewModel(
        ISettingsStore settings,
        IDialogService dialogs,
        ToastNotifier notifier,
        ILogger logger,
        CompareDirectoriesUseCase compare,
        ExecuteSyncUseCase sync,
        OverviewRowsViewModel rows)
    {
        _settings = settings;
        _dialogs = dialogs;
        _notifier = notifier;
        _logger = logger;
        _compare = compare;
        _sync = sync;
        _rows = rows;
    }

    internal static string PairCaption(int index, int total, string name)
    {
        return $"Пара {index + 1} из {total} · {name}";
    }

    internal void ShowBusyForAutomation(int index, int total, string name)
    {
        IsBusy = true;
        IsBatchRunning = true;
        IsIndeterminate = false;
        ProgressMax = total;
        ProgressValue = index;
        StatusCaption = PairCaption(index, total, name);
    }

    internal void ClearBusyForAutomation()
    {
        IsBusy = false;
        IsBatchRunning = false;
        IsIndeterminate = false;
        ProgressMax = 0;
        ProgressValue = 0;
        StatusCaption = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanCompareAll))]
    private async Task CompareAll()
    {
        var targets = BatchTargets();
        var allRows = _rows.Rows.Count;

        await RunAsync(targets.Count, "Сравнение отменено.", _logger.OverviewCompareCancelled, async token =>
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.OverviewCompareStarted(targets.Count);

            await RunRowsAsync(targets, token, CompareRowAsync);

            stopwatch.Stop();
            var tally = OverviewNarrative.TallyCompare(targets, allRows);
            StatusCaption = OverviewNarrative.DescribeCompared(tally);
            _logger.OverviewCompareFinished(tally.Compared, tally.Failed, tally.Skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, tally.Compared, tally.Failed);
        });
    }

    [RelayCommand(CanExecute = nameof(CanSyncRow))]
    private async Task SyncRow(OverviewRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var planned = row.Comparison?.CountPlannedActions();
        var lines = OverviewNarrative.BuildRowLines(row, planned);

        if (!await ConfirmSyncAsync("Синхронизация профиля", lines, planned, OverviewNarrative.DescribeIncomplete([row])))
        {
            return;
        }

        await RunAsync(0, "Синхронизация отменена.", _logger.OverviewSyncCancelled, async token =>
        {
            StatusCaption = $"Синхронизация · {row.Name}";
            var stopwatch = Stopwatch.StartNew();
            _logger.OverviewSyncStarted(1);

            await SyncRowCore(row, token);

            stopwatch.Stop();
            var (synced, failed, skipped) = OverviewNarrative.Tally(row);
            StatusCaption = row.StatusText;
            _logger.OverviewSyncFinished(synced, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(row.StatusText, synced, failed);
        });
    }

    [RelayCommand(CanExecute = nameof(CanSyncAll))]
    private async Task SyncAll()
    {
        var targets = BatchTargets();
        var excluded = _rows.Rows.Count - targets.Count;
        var planned = OverviewNarrative.SumPlans(targets);
        var lines = OverviewNarrative.BuildBatchLines(targets.Count, excluded, planned);

        if (!await ConfirmSyncAsync("Синхронизация всех профилей", lines, planned, OverviewNarrative.DescribeIncomplete(targets)))
        {
            return;
        }

        await RunAsync(targets.Count, "Синхронизация отменена.", _logger.OverviewSyncCancelled, async token =>
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.OverviewSyncStarted(targets.Count);

            await RunRowsAsync(targets, token, SyncRowCore);

            stopwatch.Stop();
            var tally = OverviewNarrative.TallySync(targets, excluded);
            StatusCaption = OverviewNarrative.DescribeSynced(tally);
            _logger.OverviewSyncFinished(tally.Synced, tally.Failed, tally.Skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, tally.Synced, tally.Failed);
        });
    }

    private async Task RunAsync(int total, string cancelledCaption, Action logCancelled, Func<CancellationToken, Task> body)
    {
        _cts = new();

        IsBusy = true;
        IsBatchRunning = total > 0;
        IsIndeterminate = total == 0;
        ProgressMax = total;
        ProgressValue = 0;

        try
        {
            await body(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = cancelledCaption;
            logCancelled();
        }
        finally
        {
            IsBusy = false;
            IsBatchRunning = false;
            IsIndeterminate = false;
            _rows.RefreshView();
            _cts.Dispose();
            _cts = null;
        }
    }

    private async Task RunRowsAsync(
        IReadOnlyList<OverviewRowViewModel> targets,
        CancellationToken token,
        Func<OverviewRowViewModel, CancellationToken, Task> step)
    {
        for (var index = 0; index < targets.Count; index++)
        {
            token.ThrowIfCancellationRequested();

            var row = targets[index];
            ProgressValue = index;
            StatusCaption = PairCaption(index, targets.Count, row.Name);

            await step(row, token);
        }

        ProgressValue = targets.Count;
    }

    private async Task RunRowAsync(OverviewRowViewModel row, CancellationToken token, string? skipNote, Func<CancellationToken, Task> step)
    {
        var rowCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _rowCts = rowCts;

        try
        {
            await step(rowCts.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            MarkSkipped(row, skipNote);
        }
        catch (OperationCanceledException)
        {
            row.Status = OverviewRunStatus.None;
            throw;
        }
        catch (Exception exception)
        {
            row.Error = exception.Unwrap().Message;
            row.Status = OverviewRunStatus.Error;
        }
        finally
        {
            _rowCts = null;
            rowCts.Dispose();
        }
    }

    private async Task CompareRowAsync(OverviewRowViewModel row, CancellationToken token)
    {
        var preflight = OverviewPipeline.Classify(row.Profile);

        if (preflight is not null)
        {
            row.Error = null;
            row.Status = preflight.Value;
            return;
        }

        row.Comparison = null;
        row.Status = OverviewRunStatus.Comparing;
        var stopwatch = Stopwatch.StartNew();

        await RunRowAsync(row, token, null, async rowToken =>
        {
            var request = BuildCompareRequest(row.Profile);
            var result = await Task.Run(() => _compare.Execute(request, rowToken), rowToken);

            stopwatch.Stop();

            if (result.SkippedLinks() is { Count: > 0 } links)
            {
                _logger.CompareLinksSkipped(links.Count, links[0]);
            }

            row.ApplyStatistics(result.GetStatistics(), result.GetDirectoryStatistics());
            row.ApplyFreshness(SyncFreshness.Compute(result.Root));
            row.ElapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            row.Error = null;
            row.Comparison = result;
            row.Status = OverviewRunStatus.Compared;
        });
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void SkipCurrent()
    {
        _rowCts?.Cancel();
    }

    private async Task SyncRowCore(OverviewRowViewModel row, CancellationToken token)
    {
        var preflight = OverviewPipeline.Classify(row.Profile);

        if (preflight is not null)
        {
            row.Error = null;
            row.Status = preflight.Value;
            return;
        }

        var profile = row.Profile;
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();
        var mode = HeadlessSync.MapMode(profile.Mode);
        var mirror = profile.Mirror;
        var winner = profile.Winner;

        if (mirror && SyncProfile.MirrorSource(mode, winner, left, right) is { } mirrorSource
            && (!Directory.Exists(mirrorSource) || !Directory.EnumerateFileSystemEntries(mirrorSource).Any()))
        {
            row.Error = "Зеркало отменено: источник пуст.";
            row.Status = OverviewRunStatus.Error;
            return;
        }

        row.Comparison = null;
        row.Status = OverviewRunStatus.Syncing;
        var stopwatch = Stopwatch.StartNew();

        await RunRowAsync(row, token, "Пропущено: часть файлов могла быть перенесена", async rowToken =>
        {
            var request = BuildCompareRequest(profile);
            var recycleOverwritten = _settings.GetBool(SettingsKeys.SyncRecycleOverwritten, AppDefaults.SyncRecycleOverwrittenDefault);
            var verify = _settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);

            var run = await Task.Run(() =>
                {
                    var result = _compare.Execute(request, rowToken);
                    var applied = _sync.Execute(new(result, SyncConflictPolicy.SkipUnresolved, SyncDeleteUi.Silent, verify, recycleOverwritten), rowToken);
                    return (Report: applied, Unreadable: result.IncompleteDirectories(), Links: result.SkippedLinks());
                },
                rowToken);

            var report = run.Report;
            var verifyState = SyncPlanNarrative.ResolveVerify(verify, report);

            if (run.Unreadable.Count > 0)
            {
                _logger.CompareIncomplete(run.Unreadable.Count, run.Unreadable[0]);
            }

            if (run.Links.Count > 0)
            {
                _logger.CompareLinksSkipped(run.Links.Count, run.Links[0]);
            }

            stopwatch.Stop();
            row.ApplySyncReport(report, verifyState);
            row.ElapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            row.Error = null;
            SyncLog.AppendSafe(SyncLogOrigin.Overview, profile.Name, report, verifyState, _logger);
        });
    }

    private static CompareDirectoriesRequest BuildCompareRequest(SyncProfile profile)
    {
        return new(profile.Left, profile.Right, profile.Exclusions, HeadlessSync.MapMode(profile.Mode), profile.Winner, profile.Mirror);
    }

    private void MarkSkipped(OverviewRowViewModel row, string? note = null)
    {
        row.Comparison = null;
        row.Error = note;
        row.Status = OverviewRunStatus.Skipped;
        _logger.OverviewRowSkipped(row.Name);
    }

    private async Task<bool> ConfirmSyncAsync(string title, IReadOnlyList<ConfirmLine> lines, PlannedActions? planned, string? warning = null)
    {
        var destructive = planned is null || planned.Deletes + planned.DirDeletes > 0;

        var confirm = new ConfirmDialogViewModel(
            title,
            PackIconLucideKind.ArrowRightLeft,
            lines,
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Синхронизировать", destructive ? ConfirmChoiceKind.Destructive : ConfirmChoiceKind.Primary),
            ])
        {
            Summary = planned is null ? null : SyncPlanNarrative.DescribePlanVolume(planned),
            Warning = warning,
        };

        return await _dialogs.ShowAsync(confirm);
    }

    private List<OverviewRowViewModel> BatchTargets()
    {
        return [.. _rows.Rows.Where(static row => row.IncludeInBatch)];
    }

    private void NotifyResult(string caption, int ok, int failed)
    {
        var (message, severity) = OverviewNarrative.DescribeOutcome(caption, ok, failed);
        _notifier.Notify(message, severity);
    }

    private bool CanCompareAll()
    {
        return !IsBusy && _rows.Rows.Any(static row => row.IncludeInBatch);
    }

    private bool CanSyncRow(OverviewRowViewModel? row)
    {
        return !IsBusy && row is not null;
    }

    private bool CanSyncAll()
    {
        return !IsBusy && _rows.Rows.Any(static row => row.IncludeInBatch);
    }
}
