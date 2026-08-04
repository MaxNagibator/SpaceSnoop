using KeepShell.Services;
using System.Diagnostics;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncSessionViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly ToastNotifier _notifier;
    private readonly PerformanceMonitor _performance;
    private readonly PerformanceRunTracker _runs;
    private readonly Action<string> _reportSummary;

    private CancellationTokenSource? _cts;
    private bool _isIndeterminate = true;
    private double _progressValue;
    private double _progressMax = 1;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusCaption;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private string _progressRateText = string.Empty;

    [ObservableProperty]
    private string _progressRemainingText = string.Empty;

    [ObservableProperty]
    private bool _hasProgressRate;

    public SyncSessionViewModel(
        IDialogService dialogs,
        ILogger logger,
        ToastNotifier notifier,
        PerformanceMonitor performance,
        PerformanceRunTracker runs,
        Action<string> reportSummary)
    {
        _dialogs = dialogs;
        _logger = logger;
        _notifier = notifier;
        _performance = performance;
        _runs = runs;
        _reportSummary = reportSummary;
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMax
    {
        get => _progressMax;
        private set => SetProperty(ref _progressMax, value);
    }

    public ICommand CancelCommand => CancelOperationCommand;

    internal static string DescribeRate(string name, SyncReport report, TimeSpan elapsed)
    {
        var finished = new PerformanceOperation(name,
            report.SuccessCount + report.Errors.Count,
            report.CopiedBytes,
            elapsed);

        return PerformanceFormat.Rate(finished) is { } rate ? $" Скорость: {rate}." : string.Empty;
    }

    internal async Task<T?> RunAsync<T>(
        string caption,
        Func<CancellationToken, IProgress<OperationProgress>, T> work,
        int total = 0,
        CancellationToken external = default,
        long totalBytes = 0)
        where T : class
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var token = _cts.Token;

        ProgressRateText = string.Empty;
        ProgressRemainingText = string.Empty;
        HasProgressRate = false;

        IsBusy = true;

        var operation = caption.TrimEnd(' ', ':');
        var determinate = total > 0;

        if (determinate)
        {
            ProgressMax = total;
            ProgressValue = 0;
            IsIndeterminate = false;
            StatusCaption = $"{caption} 0 / {total} (0 %)";
        }
        else
        {
            IsIndeterminate = true;
            StatusCaption = caption;
        }

        ProgressDetail = StatusCaption;

        var stopwatch = Stopwatch.StartNew();
        PerformanceOperation? measured = null;
        PerformanceOperation? reported = null;

        var progress = new Progress<OperationProgress>(update =>
        {
            var tail = string.IsNullOrEmpty(update.Current) ? string.Empty : $" · {update.Current}";

            string head;

            if (determinate)
            {
                ProgressValue = update.Completed;
                var percent = update.Completed * 100 / total;
                head = $"{caption} {update.Completed} / {total} ({percent} %)";
            }
            else
            {
                head = $"{caption} {update.Completed}";
            }

            if (totalBytes > 0)
            {
                head += $" · {SizeFormatter.Format(update.Bytes)} из {SizeFormatter.Format(totalBytes)}";
            }

            StatusCaption = head;
            ProgressDetail = head + tail;

            var current = new PerformanceOperation(operation,
                update.Completed,
                update.Bytes,
                stopwatch.Elapsed,
                determinate ? total : null,
                totalBytes > 0 ? totalBytes : null,
                totalBytes > 0 ? EtaBasis.Bytes : EtaBasis.Items);

            measured = current;

            ProgressRateText = PerformanceFormat.Rate(current) ?? string.Empty;
            ProgressRemainingText = PerformanceFormat.Remaining(current) ?? string.Empty;
            HasProgressRate = ProgressRateText.Length > 0 || ProgressRemainingText.Length > 0;

            if (_performance.TryReportOperation(current, reported))
            {
                reported = current;
            }
        });

        try
        {
            var result = await Task.Run(() => work(token, progress), token);
            _runs.Report(Finished(operation, measured, stopwatch.Elapsed));

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.SyncOperationCancelled(operation);
            Report("Операция отменена.");
            return null;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _logger.SyncOperationFailed(cause, operation);
            _notifier.Notify($"Ошибка: {operation}", StatusSeverity.Error);
            _dialogs.Error("Ошибка", cause.Message);
            Report($"Ошибка: {cause.Message}");
            return null;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = true;
            ProgressValue = 0;
            _performance.ClearOperation(reported);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static PerformanceOperation Finished(string name, PerformanceOperation? measured, TimeSpan elapsed)
    {
        return measured is null
            ? new(name, 0, 0, elapsed)
            : measured with { Elapsed = elapsed, TotalItems = null, TotalBytes = null, Basis = EtaBasis.None };
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
    }

    private void Report(string summary)
    {
        _reportSummary(summary);
        StatusCaption = summary;
    }
}
