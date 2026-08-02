using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewBatchViewModel : ObservableObject
{
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
        IDialogService dialogs,
        ToastNotifier notifier,
        ILogger logger,
        CompareDirectoriesUseCase compare,
        ExecuteSyncUseCase sync,
        OverviewRowsViewModel rows)
    {
        _dialogs = dialogs;
        _notifier = notifier;
        _logger = logger;
        _compare = compare;
        _sync = sync;
        _rows = rows;
    }

    [RelayCommand(CanExecute = nameof(CanCompareAll))]
    private async Task CompareAll()
    {
        _cts = new();
        var token = _cts.Token;

        var targets = BatchTargets();

        IsBusy = true;
        IsBatchRunning = true;
        IsIndeterminate = false;
        ProgressMax = targets.Count;
        ProgressValue = 0;

        var total = targets.Count;
        var compared = 0;
        var failed = 0;
        var skipped = _rows.Rows.Count - total;
        var stopwatch = Stopwatch.StartNew();

        _logger.OverviewCompareStarted(total);

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var row = targets[i];
                ProgressValue = i;
                StatusCaption = $"Пара {i + 1} из {total} · {row.Name}";

                var preflight = OverviewPipeline.Classify(row.Profile);

                if (preflight is not null)
                {
                    row.Error = null;
                    row.Status = preflight.Value;
                    skipped++;
                    continue;
                }

                row.Comparison = null;
                row.Status = OverviewRunStatus.Comparing;
                var rowStopwatch = Stopwatch.StartNew();
                var rowCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                _rowCts = rowCts;

                try
                {
                    var request = BuildCompareRequest(row.Profile);
                    var result = await Task.Run(() => _compare.Execute(request, rowCts.Token), rowCts.Token);

                    rowStopwatch.Stop();
                    row.ApplyStatistics(result.GetStatistics(), result.GetDirectoryStatistics());
                    row.ApplyFreshness(SyncFreshness.Compute(result.Root));
                    row.ElapsedMs = (long)rowStopwatch.Elapsed.TotalMilliseconds;
                    row.Error = null;
                    row.Comparison = result;
                    row.Status = OverviewRunStatus.Compared;
                    compared++;
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    MarkSkipped(row);
                    skipped++;
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
                    failed++;
                }
                finally
                {
                    _rowCts = null;
                    rowCts.Dispose();
                }
            }

            ProgressValue = total;
            stopwatch.Stop();
            StatusCaption = $"Сравнено пар: {compared}, ошибок: {failed}, пропущено: {skipped}";
            _logger.OverviewCompareFinished(compared, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, compared, failed);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Сравнение отменено.";
            _logger.OverviewCompareCancelled();
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

    [RelayCommand(CanExecute = nameof(CanSyncRow))]
    private async Task SyncRow(OverviewRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var planned = row.Comparison?.CountPlannedActions();

        var lines = new List<ConfirmLine>
        {
            new ConfirmTextLine(row.Name, ConfirmTextTone.Strong),
            new ConfirmTextLine($"{row.Left} → {row.Right}", ConfirmTextTone.Muted),
            new ConfirmGapLine(),
        };

        if (planned is null)
        {
            lines.Add(new ConfirmTextLine("Файлы будут скопированы по направлению профиля, удаления – в корзину."));
        }
        else
        {
            lines.AddRange(SyncPlanNarrative.BuildPlanLines(planned, null, []));
        }

        if (!await ConfirmSyncAsync("Синхронизация профиля", lines, planned))
        {
            return;
        }

        _cts = new();
        var token = _cts.Token;

        IsBusy = true;
        IsIndeterminate = true;
        ProgressMax = 0;
        ProgressValue = 0;
        StatusCaption = $"Синхронизация · {row.Name}";

        var stopwatch = Stopwatch.StartNew();
        _logger.OverviewSyncStarted(1);

        try
        {
            await SyncRowCore(row, token);
            stopwatch.Stop();
            var (synced, failed, skipped) = OverviewNarrative.Tally(row);
            StatusCaption = row.StatusText;
            _logger.OverviewSyncFinished(synced, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(row.StatusText, synced, failed);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Синхронизация отменена.";
            _logger.OverviewSyncCancelled();
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            _rows.RefreshView();
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncAll))]
    private async Task SyncAll()
    {
        var targets = BatchTargets();
        var excluded = _rows.Rows.Count - targets.Count;

        var planned = OverviewNarrative.SumPlans(targets);

        var lines = new List<ConfirmLine>
        {
            new ConfirmMetricLine("Профилей в пакете", $"{targets.Count:N0}", string.Empty),
        };

        if (excluded > 0)
        {
            lines.Add(new ConfirmMetricLine("Исключено", $"{excluded:N0}", string.Empty, ConfirmMetricTone.Sub));
        }

        lines.Add(new ConfirmGapLine());

        if (planned is null)
        {
            lines.Add(new ConfirmTextLine("Файлы будут скопированы по направлению каждого профиля, удаления – в корзину."));
        }
        else
        {
            lines.AddRange(SyncPlanNarrative.BuildPlanLines(planned, null, []));
        }

        if (!await ConfirmSyncAsync("Синхронизация всех профилей", lines, planned))
        {
            return;
        }

        _cts = new();
        var token = _cts.Token;

        IsBusy = true;
        IsBatchRunning = true;
        IsIndeterminate = false;
        ProgressMax = targets.Count;
        ProgressValue = 0;

        var total = targets.Count;
        var synced = 0;
        var failed = 0;
        var skipped = excluded;
        var stopwatch = Stopwatch.StartNew();

        _logger.OverviewSyncStarted(total);

        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var row = targets[i];
                ProgressValue = i;
                StatusCaption = $"Пара {i + 1} из {total} · {row.Name}";

                await SyncRowCore(row, token);

                var (rowSynced, rowFailed, rowSkipped) = OverviewNarrative.Tally(row);
                synced += rowSynced;
                failed += rowFailed;
                skipped += rowSkipped;
            }

            ProgressValue = total;
            stopwatch.Stop();
            StatusCaption = $"Синхронизировано профилей: {synced}, c ошибками: {failed}, пропущено: {skipped}";
            _logger.OverviewSyncFinished(synced, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, synced, failed);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Синхронизация отменена.";
            _logger.OverviewSyncCancelled();
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
        var rowCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        _rowCts = rowCts;

        try
        {
            var request = BuildCompareRequest(profile);

            var report = await Task.Run(() =>
                {
                    var result = _compare.Execute(request, rowCts.Token);
                    return _sync.Execute(new(result, SyncConflictPolicy.SkipUnresolved, SyncDeleteUi.Silent), rowCts.Token);
                },
                rowCts.Token);

            stopwatch.Stop();
            row.ApplySyncReport(report);
            row.ElapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            row.Error = null;
            SyncLog.AppendSafe($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Обзор [{profile.Name}]: {report.SuccessCount} успешно, {report.Errors.Count} ошибок", report, _logger);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            MarkSkipped(row, "Пропущено: часть файлов могла быть перенесена");
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

    private async Task<bool> ConfirmSyncAsync(string title, IReadOnlyList<ConfirmLine> lines, PlannedActions? planned)
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
