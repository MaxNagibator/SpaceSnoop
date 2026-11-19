using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public readonly record struct CleanupRequest(
    IReadOnlyList<CleanupTarget> Targets,
    long EstimatedBytes,
    int EstimatedFiles,
    CancellationToken External = default);

public sealed partial class CleanupProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly CleanupRequest _request;
    private readonly CleanupService _service;
    private readonly ILogger _logger;
    private readonly OperationProgressState _progress = new();
    private readonly UiProgressPoller _progressPoller;
    private readonly PerformanceOperations _operations;
    private readonly List<string> _reportedErrors = [];
    private PerformanceOperation? _reported;
    private long _started;
    private DateTime _startedAtUtc;

    private bool _cancelMissedIndivisible;

    [ObservableProperty]
    private string _targetName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelStep))]
    private bool _isStepIndivisible;

    [ObservableProperty]
    private bool _isDetailsExpanded;

    [ObservableProperty]
    private bool _isFailureExpanded;

    public override string Title => "Очистка диска";

    public string ActionText => "Очистить";

    public string PlanText { get; }

    public string FateText => "Файлы удаляются безвозвратно, мимо корзины – перенос временных файлов в корзину не освободил бы места.";

    public long FreedBytes { get; private set; }

    public int Deleted { get; private set; }

    public int Skipped { get; private set; }

    public string FirstError { get; private set; } = string.Empty;

    public string ResultText => Failure is not null
        ? StatusText
        : Summarize(Deleted, FreedBytes, Cancelled, _cancelMissedIndivisible, Skipped, FirstError, false);

    public string ResultHeading => Failure is not null
        ? "Не удалось завершить очистку"
        : CleanupOutcomeText.For(new CleanupReport(FreedBytes, Deleted, Skipped, _reportedErrors, Cancelled));

    public string FreedText => SizeFormatter.Format(FreedBytes);

    public string DeletedText => Deleted.ToString("N0");

    public string SkippedText => Skipped.ToString("N0");

    public bool HasSkipped => Skipped > 0;

    public bool ShowIndivisibleResult => IsFinished && _cancelMissedIndivisible;

    public string FailureText => Failure?.Message ?? string.Empty;

    public IReadOnlyList<CleanupIssue> Issues { get; private set; } = [];

    public bool HasErrorDetails => IsFinished && _reportedErrors.Count > 0;

    public string ErrorDetailsText => string.Join(Environment.NewLine, _reportedErrors);

    public string ErrorDetailsCaption => FormatErrorDetails(_reportedErrors.Count, Skipped);

    public bool WasCancelled => Cancelled;

    public bool HasFailure => Failure is not null;

    public bool IsIndeterminate => _request.EstimatedFiles == 0;

    public bool CanCancelStep => !IsStepIndivisible;

    public string IndivisibleText => "Корзина Windows очищается одним системным вызовом – этот шаг не прерывается.";

    protected override string RunningStatus => "Очистка…";

    protected override bool CloseResult => FreedBytes > 0;

    protected override bool HasFailedItems => Skipped > 0 || FirstError.Length > 0;

    public CleanupProgressDialogViewModel(CleanupRequest request, CleanupService service, IUiDispatcher uiDispatcher, ILogger logger, PerformanceOperations operations)
    {
        _request = request;
        _operations = operations;
        _service = service;
        _logger = logger;
        _progressPoller = new(uiDispatcher, OnProgressTick);

        TargetName = request.Targets.Count == 1
            ? request.Targets[0].Name
            : $"Корзин выбрано: {request.Targets.Count}";

        PlanText = $"≈ {SizeFormatter.Format(request.EstimatedBytes)} · ≈ {request.EstimatedFiles:N0} файлов";
    }

    internal static string FormatErrorDetails(int shown, int skipped)
    {
        var text = $"В списке {shown:N0} {Plural.Word(shown, "причина", "причины", "причин")}.";
        var omitted = Math.Max(0, skipped - shown);
        if (omitted > 0)
        {
            text += $" Без подробностей: {omitted:N0} {Plural.Word(omitted, "пропуск", "пропуска", "пропусков")}.";
        }

        return text;
    }

    internal static string Summarize(int deleted, long freed, bool cancelled, bool binClearedDespiteCancel, int skipped, string firstError, bool includeFirstError = true)
    {
        IReadOnlyList<string> errors = firstError.Length > 0 ? [firstError] : [];
        var outcome = CleanupOutcomeText.For(new CleanupReport(freed, deleted, skipped, errors, cancelled));
        var text = $"{outcome}: удалено {deleted:N0}, освобождено {SizeFormatter.Format(freed)}";
        if (skipped > 0)
        {
            text += $". Пропущено {skipped:N0}";
        }

        if (includeFirstError && firstError.Length > 0)
        {
            text += skipped > 0 ? $" – {firstError}" : $". {firstError}";
        }
        else
        {
            text += ".";
        }

        return binClearedDespiteCancel
            ? $"{text.TrimEnd('.')}. Корзину Windows отмена не остановила: этот шаг не прерывается."
            : text;
    }

    internal static bool ClearedDespiteCancel(CleanupTarget target, CleanupReport report, bool cancelRequested)
    {
        return target.IsIndivisible && cancelRequested && !report.Cancelled && report.Deleted > 0;
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _request.External);

        _progress.Reset();
        _progressPoller.Start();

        try
        {
            foreach (var target in _request.Targets)
            {
                linked.Token.ThrowIfCancellationRequested();

                IsStepIndivisible = target.IsIndivisible;
                ReportOperation($"Очистка: {target.Name}", Deleted, FreedBytes);

                var progress = new OffsetProgress(_progress, Deleted, FreedBytes);
                var report = await Task.Run(() => _service.Clean(target, progress, linked.Token), linked.Token);

                IsStepIndivisible = false;
                Tally(target, report, linked.Token.IsCancellationRequested);

                if (report.Cancelled)
                {
                    throw new OperationCanceledException(linked.Token);
                }
            }
        }
        finally
        {
            IsStepIndivisible = false;
            await _progressPoller.StopAsync();
        }
    }

    protected override void OnStarting()
    {
        _started = Stopwatch.GetTimestamp();
        _startedAtUtc = DateTime.UtcNow;
        ReportOperation("Подготовка очистки", 0, 0);
        _logger.CleanupRunStarted(_request.Targets.Count, _request.EstimatedBytes);
    }

    protected override void OnFinished()
    {
        Issues = _reportedErrors.Select(CleanupIssue.From).ToArray();
        OnPropertyChanged(nameof(ResultText));
        OnPropertyChanged(nameof(ResultHeading));
        OnPropertyChanged(nameof(FreedText));
        OnPropertyChanged(nameof(DeletedText));
        OnPropertyChanged(nameof(SkippedText));
        OnPropertyChanged(nameof(HasSkipped));
        OnPropertyChanged(nameof(ShowIndivisibleResult));
        OnPropertyChanged(nameof(HasFailure));
        OnPropertyChanged(nameof(FailureText));
        OnPropertyChanged(nameof(Issues));
        OnPropertyChanged(nameof(HasErrorDetails));
        OnPropertyChanged(nameof(ErrorDetailsText));
        OnPropertyChanged(nameof(ErrorDetailsCaption));
        _operations.ReportCleanup(new(_startedAtUtc, DateTime.UtcNow, Deleted, Skipped, FreedBytes, Cancelled, Failure?.Message, _reportedErrors.ToArray()));
        _operations.Release(_reported);
        _reported = null;
        if (Cancelled)
        {
            _logger.CleanupRunCancelled(Deleted, FreedBytes);
            return;
        }

        if (Failure is { } failure)
        {
            _logger.CleanupRunFailed(failure);
            return;
        }

        _logger.CleanupRunFinished(Deleted, FreedBytes, Skipped);
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        return Summarize(Deleted, FreedBytes, Cancelled, _cancelMissedIndivisible, Skipped, FirstError);
    }

    private void Tally(CleanupTarget target, CleanupReport report, bool cancelRequested)
    {
        _cancelMissedIndivisible |= ClearedDespiteCancel(target, report, cancelRequested);

        FreedBytes += report.FreedBytes;
        Deleted += report.Deleted;
        Skipped += report.Skipped;
        var available = Math.Max(0, AppDefaults.CleanupReportedErrorsLimit - _reportedErrors.Count);
        _reportedErrors.AddRange(report.Errors.Take(available));

        if (FirstError.Length == 0 && report.Errors.Count > 0)
        {
            FirstError = report.Errors[0];
        }
    }

    private void OnProgressTick()
    {
        Apply(_progress.CreateSnapshot());
    }

    private void Apply(OperationProgress update)
    {
        if (IsFinished)
        {
            return;
        }

        var done = update.Completed;
        var total = _request.EstimatedFiles;

        CurrentPath = update.Current;
        CountText = total > 0 ? $"{done:N0} / {total:N0}" : done.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)done / total, 0d, 1d) : 0d;
        ReportOperation(_reported?.Name ?? "Очистка диска", done, update.Bytes);
    }

    private void ReportOperation(string name, int completed, long bytes)
    {
        var operation = new PerformanceOperation(name, completed, bytes, Stopwatch.GetElapsedTime(_started), LogicalBytes: true);
        if (_operations.TryReport(operation, _reported))
        {
            _reported = operation;
        }
    }

    private sealed class OffsetProgress(OperationProgressState state, int completed, long bytes) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            state.Report(new(completed + value.Completed, value.Current, bytes + value.Bytes));
        }
    }
}
