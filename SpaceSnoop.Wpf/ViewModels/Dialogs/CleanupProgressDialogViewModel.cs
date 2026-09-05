using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public readonly record struct CleanupRequest(
    IReadOnlyList<CleanupTarget> Targets,
    long EstimatedBytes,
    int EstimatedFiles,
    CancellationToken External = default);

public sealed partial class CleanupProgressDialogViewModel : OperationDialogViewModelBase
{
    internal const int ProgressPollIntervalMs = 120;

    internal static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(ProgressPollIntervalMs);

    private readonly CleanupRequest _request;
    private readonly CleanupService _service;
    private readonly ILogger _logger;
    private readonly OperationProgressState _progress = new();
    private readonly IUiTimer _progressTimer;

    private long _freed;
    private int _deleted;
    private int _skipped;
    private string _firstError = string.Empty;

    [ObservableProperty]
    private string _targetName = string.Empty;

    public CleanupProgressDialogViewModel(CleanupRequest request, CleanupService service, IUiDispatcher uiDispatcher, ILogger logger)
    {
        _request = request;
        _service = service;
        _logger = logger;
        _progressTimer = uiDispatcher.CreateTimer(ProgressPollInterval, OnProgressTick);

        TargetName = request.Targets.Count == 1
            ? request.Targets[0].Name
            : $"Корзин выбрано: {request.Targets.Count}";

        PlanText = $"≈ {SizeFormatter.Format(request.EstimatedBytes)} · ≈ {request.EstimatedFiles:N0} файлов";
    }

    public override string Title => "Очистка диска";

    public string ActionText => "Очистить";

    public string PlanText { get; }

    public string FateText => "Файлы удаляются безвозвратно, мимо корзины – перенос временных файлов в корзину не освободил бы места.";

    public long FreedBytes => _freed;

    public int Deleted => _deleted;

    public int Skipped => _skipped;

    public string FirstError => _firstError;

    public bool WasCancelled => Cancelled;

    public bool HasFailure => Failure is not null;

    public bool IsIndeterminate => _request.EstimatedFiles == 0;

    protected override string RunningStatus => "Очистка…";

    protected override bool CloseResult => _freed > 0;

    protected override bool HasFailedItems => _skipped > 0 || _firstError.Length > 0;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _request.External);

        _progress.Reset();
        _progressTimer.Start();

        try
        {
            await Task.Run(() => Execute(linked.Token), linked.Token);
        }
        finally
        {
            _progressTimer.Stop();
            Apply(_progress.CreateSnapshot());
        }
    }

    protected override void OnStarting()
    {
        _logger.CleanupRunStarted(_request.Targets.Count, _request.EstimatedBytes);
    }

    protected override void OnFinished()
    {
        if (Cancelled)
        {
            _logger.CleanupRunCancelled(_deleted, _freed);
            return;
        }

        if (Failure is { } failure)
        {
            _logger.CleanupRunFailed(failure);
            return;
        }

        _logger.CleanupRunFinished(_deleted, _freed, _skipped);
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        var done = $"удалено {_deleted:N0}, освобождено {SizeFormatter.Format(_freed)}";

        if (Cancelled)
        {
            return $"Отменено: {done}.";
        }

        if (_skipped > 0)
        {
            return $"Готово: {done}. Пропущено {_skipped:N0} – {_firstError}";
        }

        if (_firstError.Length > 0)
        {
            return $"Готово: {done}. {_firstError}";
        }

        return $"Готово: {done}.";
    }

    private void Execute(CancellationToken token)
    {
        foreach (var target in _request.Targets)
        {
            token.ThrowIfCancellationRequested();

            var report = _service.Clean(target, new OffsetProgress(_progress, _deleted, _freed), token);

            _freed += report.FreedBytes;
            _deleted += report.Deleted;
            _skipped += report.Skipped;

            if (_firstError.Length == 0 && report.Errors.Count > 0)
            {
                _firstError = report.Errors[0];
            }

            if (report.Cancelled)
            {
                throw new OperationCanceledException(token);
            }
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
    }

    private sealed class OffsetProgress(OperationProgressState state, int completed, long bytes) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            state.Report(new(completed + value.Completed, value.Current, bytes + value.Bytes));
        }
    }
}
