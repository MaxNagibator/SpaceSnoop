using KeepShell.Services.Platform;
using KeepShell.ViewModels;
using SpaceSnoop.Core.Duplicates;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public readonly record struct DuplicateRequest(
    DirectorySpace Root,
    DuplicateOptions Options,
    CancellationToken External = default);

public sealed partial class DuplicateProgressDialogViewModel : OperationDialogViewModelBase
{
    internal const int ProgressPollIntervalMs = 120;

    internal static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(ProgressPollIntervalMs);

    private readonly DuplicateRequest _request;
    private readonly DuplicateFinder _finder;
    private readonly ILogger _logger;
    private readonly OperationProgressState _progress = new();
    private readonly IUiTimer _progressTimer;

    public DuplicateProgressDialogViewModel(DuplicateRequest request, DuplicateFinder finder, IUiDispatcher uiDispatcher, ILogger logger)
    {
        _request = request;
        _finder = finder;
        _logger = logger;
        _progressTimer = uiDispatcher.CreateTimer(ProgressPollInterval, OnProgressTick);

        RootPath = request.Root.AbsolutePath;
        ThresholdText = $"от {SizeFormatter.Format(request.Options.MinSize)}";
        PlanText = $"{RootPath} · {ThresholdText}";
    }

    public override string Title => "Поиск дубликатов";

    public string ActionText => "Искать";

    public string RootPath { get; }

    public string ThresholdText { get; }

    public string PlanText { get; }

    public string FateText => "Файлы только читаются: поиск ничего не удаляет и ничего не помечает.";

    public DuplicateReport? Report { get; private set; }

    public bool IsIndeterminate => true;

    protected override string RunningStatus => "Сличение файлов…";

    protected override bool CloseResult => Report is { Groups.Count: > 0 };

    protected override bool HasFailedItems => Report is { Errors.Count: > 0 };

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _request.External);

        _progress.Reset();
        _progressTimer.Start();

        try
        {
            Report = await _finder.FindAsync(_request.Root, _request.Options, _progress, linked.Token);
        }
        finally
        {
            _progressTimer.Stop();
            Apply(_progress.CreateSnapshot());
        }
    }

    protected override void OnStarting()
    {
        _logger.DuplicateSearchStarted(_request.Root.AbsolutePath, _request.Options.MinSize);
    }

    protected override void OnFinished()
    {
        if (Cancelled)
        {
            _logger.DuplicateSearchCancelled();
            return;
        }

        if (Failure is { } failure)
        {
            _logger.DuplicateSearchFailed(failure);
            return;
        }

        if (Report is { } report)
        {
            _logger.DuplicateSearchFinished(report.Groups.Count, report.ReclaimableBytes, report.Examined);
        }
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        if (Cancelled)
        {
            return "Отменено: результат неполон.";
        }

        if (Report is not { } report)
        {
            return "Готово.";
        }

        if (report.Groups.Count == 0)
        {
            return $"Дубликатов не найдено, проверено файлов: {report.Examined:N0}.";
        }

        var summary = $"Групп: {report.Groups.Count:N0}, вернёт {SizeFormatter.Format(report.ReclaimableBytes)}";

        return report.Errors.Count > 0
            ? $"Готово: {summary}. Не прочитано файлов: {report.Errors.Count:N0}."
            : $"Готово: {summary}.";
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

        CurrentPath = update.Current;
        CountText = $"{update.Completed:N0} · {SizeFormatter.Format(update.Bytes)}";
    }
}
