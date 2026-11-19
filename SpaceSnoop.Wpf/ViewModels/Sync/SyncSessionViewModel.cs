using KeepShell.Services;
using System.Diagnostics;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncSessionViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly ToastNotifier _notifier;
    private readonly PerformanceOperations _performance;
    private readonly Action<string> _reportSummary;
    private readonly OperationProgressState _progress = new();
    private readonly UiProgressPoller _progressPoller;

    private readonly Stopwatch _clock = new();

    private CancellationTokenSource? _cts;
    private bool _isIndeterminate;
    private double _progressValue;
    private double _progressMax = 1;

    private RunShape _shape;
    private PerformanceOperation? _measured;
    private PerformanceOperation? _reported;

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

    internal bool LastOperationCancelled { get; private set; }

    public SyncSessionViewModel(
        IDialogService dialogs,
        ILogger logger,
        ToastNotifier notifier,
        PerformanceOperations performance,
        IUiDispatcher uiDispatcher,
        Action<string> reportSummary)
    {
        _dialogs = dialogs;
        _logger = logger;
        _notifier = notifier;
        _performance = performance;
        _reportSummary = reportSummary;
        _progressPoller = new(uiDispatcher, OnProgressTick);
    }

    internal static string DescribeRate(string name, SyncReport report, TimeSpan elapsed)
    {
        var finished = new PerformanceOperation(
            name,
            report.SuccessCount + report.Errors.Count,
            report.CopiedBytes,
            elapsed);

        return PerformanceText.Rate(finished) is { } rate ? $" Скорость: {rate}." : string.Empty;
    }

    internal void ShowBusyForAutomation(string caption, OperationProgress update, TimeSpan elapsed, int total = 0, long totalBytes = 0)
    {
        IsBusy = true;

        var shape = new RunShape(caption, caption.TrimEnd(' ', ':'), total, totalBytes);

        if (shape.Determinate)
        {
            ProgressMax = total;
            IsIndeterminate = false;
        }
        else
        {
            IsIndeterminate = true;
        }

        Advance(shape, update, elapsed);
    }

    internal void ClearBusyForAutomation()
    {
        IsBusy = false;
        IsIndeterminate = false;
        ProgressValue = 0;
        StatusCaption = null;
        ProgressDetail = null;
        ProgressRateText = string.Empty;
        ProgressRemainingText = string.Empty;
        HasProgressRate = false;
    }

    internal async Task<T?> RunAsync<T>(
        string caption,
        Func<CancellationToken, IProgress<OperationProgress>, T> work,
        int total = 0,
        CancellationToken external = default,
        long totalBytes = 0,
        bool selfThrottled = false,
        Func<Exception, string>? formatError = null,
        Action<string>? onError = null)
        where T : class
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var token = _cts.Token;
        LastOperationCancelled = false;

        ProgressRateText = string.Empty;
        ProgressRemainingText = string.Empty;
        HasProgressRate = false;

        IsBusy = true;

        _shape = new(caption, caption.TrimEnd(' ', ':'), total, totalBytes);
        var operation = _shape.Operation;

        if (_shape.Determinate)
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

        _clock.Restart();
        _measured = null;
        _reported = null;
        _progress.Reset();

        IProgress<OperationProgress> progress = _progress;

        if (selfThrottled)
        {
            progress = new Progress<OperationProgress>(Observe);
        }
        else
        {
            _progressPoller.Start();
        }

        try
        {
            T result;

            try
            {
                result = await Task.Run(() => work(token, progress), token);
            }
            finally
            {
                await _progressPoller.StopAsync();
            }

            LastOperationCancelled = token.IsCancellationRequested;
            _performance.ReportRun(Finished(operation, _measured, _clock.Elapsed));

            return result;
        }
        catch (OperationCanceledException)
        {
            LastOperationCancelled = true;
            _logger.SyncOperationCancelled(operation);
            Report("Операция отменена.");
            return null;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            var message = formatError?.Invoke(cause) ?? cause.Message;
            _logger.SyncOperationFailed(cause, operation);
            _notifier.Notify($"Ошибка: {operation}", StatusSeverity.Error);
            if (onError is null)
            {
                _dialogs.Error("Ошибка", message);
            }
            else
            {
                onError(message);
            }

            Report($"Ошибка: {message}");
            return null;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            ProgressValue = 0;
            _performance.Release(_reported);
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnProgressTick()
    {
        Observe(_progress.CreateSnapshot());
    }

    private void Observe(OperationProgress update)
    {
        var current = Advance(_shape, update, _clock.Elapsed);

        _measured = current;

        if (_performance.TryReport(current, _reported))
        {
            _reported = current;
        }
    }

    private PerformanceOperation Advance(in RunShape shape, OperationProgress update, TimeSpan elapsed)
    {
        var tail = string.IsNullOrEmpty(update.Current) ? string.Empty : $" · {update.Current}";
        string head;

        if (shape.Determinate)
        {
            ProgressValue = update.Completed;
            var percent = update.Completed * 100 / shape.Total;
            head = $"{shape.Caption} {update.Completed} / {shape.Total} ({percent} %)";
        }
        else
        {
            head = $"{shape.Caption} {update.Completed}";
        }

        if (shape.HasBytes)
        {
            head += $" · {SizeFormatter.Format(update.Bytes)} из {SizeFormatter.Format(shape.TotalBytes)}";
        }

        StatusCaption = head;
        ProgressDetail = head + tail;

        var current = new PerformanceOperation(
            shape.Operation,
            update.Completed,
            update.Bytes,
            elapsed,
            shape.Determinate ? shape.Total : null,
            shape.HasBytes ? shape.TotalBytes : null,
            shape.HasBytes ? EtaBasis.Bytes : EtaBasis.Items);

        ProgressRateText = PerformanceText.Rate(current) ?? string.Empty;
        ProgressRemainingText = PerformanceText.Remaining(current) ?? string.Empty;
        HasProgressRate = ProgressRateText.Length > 0 || ProgressRemainingText.Length > 0;

        return current;
    }

    private static PerformanceOperation Finished(string name, PerformanceOperation? measured, TimeSpan elapsed)
    {
        return measured is null
            ? new(name, 0, 0, elapsed)
            : measured with
            {
                Elapsed = elapsed,
                TotalItems = null,
                TotalBytes = null,
                Basis = EtaBasis.None,
            };
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

    private readonly record struct RunShape(string Caption, string Operation, int Total, long TotalBytes)
    {
        public bool Determinate => Total > 0;

        public bool HasBytes => TotalBytes > 0;
    }
}
