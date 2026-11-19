using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class ArchiveProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly ArchiveRequest _request;
    private readonly ArchiveService _service;
    private readonly ILogger _logger;
    private readonly Action<string, bool> _deleteDirectory;
    private readonly OperationProgressState _packProgress = new();
    private readonly OperationProgressState _verifyProgress = new();
    private readonly OperationProgressState _coverageProgress = new();
    private readonly UiProgressPoller _progressPoller;

    private volatile ArchivePhase _phase;
    private volatile CoverageTotals _coverageTotals = CoverageTotals.Empty;
    private bool _packed;
    private int _packTotal;
    private int _verifyTotal;
    private int _phaseTotal;
    private string _resultSummary = string.Empty;
    private string? _coverageIssue;
    private string? _unreadableIssue;

    [ObservableProperty]
    private string _countLabel = "Упаковано";

    [ObservableProperty]
    private string _sourceSize;

    public override string Title => "Упаковка в архив";

    public string ActionText => "Упаковать";

    public string SourceName { get; }

    public string TargetName { get; }

    public string FateText { get; }

    public bool OriginalDeleted { get; private set; }

    public string? CreatedArchivePath { get; private set; }

    public bool IsIndeterminate => _phaseTotal == 0;

    protected override string RunningStatus => "Обход каталога…";

    protected override bool CloseResult => OriginalDeleted;

    protected override bool HasFailedItems => _coverageIssue is not null || _unreadableIssue is not null;

    public ArchiveProgressDialogViewModel(ArchiveRequest request, ArchiveService service, IUiDispatcher uiDispatcher, ILogger logger)
        : this(request, service, uiDispatcher, logger, service.DeleteDirectoryToRecycleBin)
    {
    }

    internal ArchiveProgressDialogViewModel(
        ArchiveRequest request,
        ArchiveService service,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Action<string, bool> deleteDirectory)
    {
        _request = request;
        _service = service;
        _logger = logger;
        _deleteDirectory = deleteDirectory;
        _progressPoller = new(uiDispatcher, OnProgressTick);

        SourceName = Path.GetFileName(request.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SourceSize = $"≈ {SizeFormatter.Format(request.TotalBytes)} · ≈ {request.EstimatedFiles:N0} {Plural.Word(request.EstimatedFiles, "файл", "файла", "файлов")}";
        TargetName = Path.GetFileName(request.TargetPath);
        FateText = request.DeleteOriginal
            ? "Архив будет прочитан целиком с проверкой контрольных сумм, и только потом оригинал отправится в корзину."
            : "Оригинал останется на месте.";
    }

    internal void ShowRunningForAutomation(int packed, int total, long bytes, string current)
    {
        IsRunning = true;
        StatusText = RunningStatus;
        _packTotal = total;

        OnStage(new(ArchiveStage.Packing, total, total, bytes));
        OnTick(new(packed, current), false);
    }

    internal void ShowFinishedForAutomation(int total, long bytes, long compressed)
    {
        _packed = true;
        _packTotal = total;
        _resultSummary = Summarize(compressed, bytes);
        OriginalDeleted = _request.DeleteOriginal;

        CountText = $"{total} / {total}";
        ProgressValue = 1d;
        CurrentPath = string.Empty;
        SetPhaseTotal(total);

        IsRunning = false;
        IsFinished = true;
        HasErrors = HasFailedItems;
        StatusText = BuildSummary();
    }

    internal void ClearForAutomation()
    {
        IsRunning = false;
        IsFinished = false;
        OriginalDeleted = false;
        CreatedArchivePath = null;
        _packed = false;
        _phase = ArchivePhase.None;
        _coverageTotals = CoverageTotals.Empty;
        _coverageProgress.Reset();
        _resultSummary = string.Empty;
        SetPhaseTotal(0);
    }

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var stage = new Progress<ArchiveStageUpdate>(OnStage);

        _packProgress.Reset();
        _verifyProgress.Reset();
        _coverageProgress.Reset();

        await using (_progressPoller.StartScope())
        {
            await Task.Run(() => ExecuteZip(stage, token), token);
        }
    }

    protected override void OnStarting()
    {
        _logger.ArchiveStarted(_request.SourcePath);
    }

    protected override void OnFinished()
    {
        if (Cancelled)
        {
            _logger.ArchiveCancelled(_request.SourcePath);
            return;
        }

        if (Failure is { } failure)
        {
            _logger.ArchiveFailed(failure, _request.SourcePath);
            return;
        }

        _logger.ArchiveFinished(_request.SourcePath, _resultSummary);
    }

    protected override string BuildSummary()
    {
        if (Cancelled)
        {
            return _packed
                ? $"Отменено на проверке: архив {TargetName} создан, но не проверен. Оригинал не тронут."
                : "Отменено.";
        }

        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        if (_coverageIssue is { } issue)
        {
            return $"Архив {_resultSummary} создан, но каталог покрыт не полностью: {issue}. Оригинал оставлен на месте.";
        }

        if (_unreadableIssue is { } skipped)
        {
            return $"Архив {_resultSummary}. Оригинал на месте: {skipped} – этих данных в архиве нет.";
        }

        return OriginalDeleted
            ? $"Готово. Архив {_resultSummary}. Оригинал → в корзину."
            : $"Готово. Архив {_resultSummary}.";
    }

    private static string Summarize(long compressed, long original)
    {
        var ratio = original > 0 ? (1 - ((double)compressed / original)) * 100 : 0;

        return $"{SizeFormatter.Format(compressed)} (было {SizeFormatter.Format(original)}, −{ratio:F0} %)";
    }

    private void ExecuteZip(IProgress<ArchiveStageUpdate> stage, CancellationToken token)
    {
        var content = _service.Collect(_request.SourcePath, token);
        _phase = ArchivePhase.Packing;
        stage.Report(new(ArchiveStage.Packing, content.Files.Count, content.Files.Count + content.EmptyDirectories.Count, content.Bytes));

        if (content.Unreadable.Count > 0)
        {
            _unreadableIssue = content.Unreadable.Count > 1
                ? $"не прочитано каталогов: {content.Unreadable.Count}, первый «{content.Unreadable[0]}»"
                : $"не прочитан каталог «{content.Unreadable[0]}»";
        }

        var stats = _service.ZipFiles(_request.SourcePath, content, _request.TargetPath, _request.Level, _packProgress, token);

        _packed = true;
        _verifyTotal = stats.Count;

        if (_request.DeleteOriginal)
        {
            _phase = ArchivePhase.Verifying;
        }

        var (ok, detail) = _service.VerifyZip(_request.TargetPath, stats, _request.DeleteOriginal, _verifyProgress, token);

        if (!ok)
        {
            _logger.ArchiveVerifyFailed(_request.TargetPath, detail);
            _service.SafeDelete(_request.TargetPath);
            throw new InvalidOperationException($"Архив не прошёл проверку ({detail}); оригинал не тронут.");
        }

        token.ThrowIfCancellationRequested();

        if (_request.DeleteOriginal)
        {
            _coverageProgress.Reset();
            _coverageTotals = CoverageTotals.Empty;
            _phase = ArchivePhase.Coverage;
            stage.Report(new(ArchiveStage.Coverage, 0, 0, 0));

            var coverage = _service.VerifyCoverage(
                _request.SourcePath,
                _request.TargetPath,
                _coverageProgress,
                (files, bytes) =>
                {
                    _coverageTotals = new(files, bytes);
                    stage.Report(new(ArchiveStage.Coverage, files, files, bytes));
                },
                token);

            if (!coverage.Ok)
            {
                _coverageIssue = coverage.Detail;
                _logger.ArchiveVerifyFailed(_request.TargetPath, coverage.Detail);
            }
        }

        var compressed = new FileInfo(_request.TargetPath).Length;
        _resultSummary = Summarize(compressed, stats.Bytes);
        CreatedArchivePath = _request.TargetPath;

        if (!_request.DeleteOriginal || _coverageIssue is not null || _unreadableIssue is not null)
        {
            return;
        }

        token.ThrowIfCancellationRequested();
        _phase = ArchivePhase.None;
        stage.Report(new(ArchiveStage.Deleting, 0, 0, 0));
        _deleteDirectory(_request.SourcePath, _request.Interactive);
        OriginalDeleted = true;

        var totals = _coverageTotals;
        stage.Report(new(ArchiveStage.Finished, totals.Files, Math.Max(_verifyTotal, 1), totals.Bytes));
    }

    private void OnStage(ArchiveStageUpdate update)
    {
        if (IsFinished)
        {
            return;
        }

        switch (update.Stage)
        {
            case ArchiveStage.Coverage when update.Files == 0 && update.Bytes == 0:
                if (_coverageTotals == CoverageTotals.Empty)
                {
                    SetPhaseTotal(0);
                    CurrentPath = string.Empty;
                    CountText = string.Empty;
                    ProgressValue = 0d;
                    CountLabel = "Сверено";
                    StatusText = "Сверка архива с каталогом…";
                }

                return;

            case ArchiveStage.Coverage:
                OnCoverageTick(_coverageProgress.CreateSnapshot(), update.Files, update.Bytes);
                return;

            case ArchiveStage.Deleting:
                SetPhaseTotal(0);
                CurrentPath = string.Empty;
                CountText = string.Empty;
                ProgressValue = 0d;
                CountLabel = "Сверено";
                StatusText = "Перемещение в корзину…";
                return;

            case ArchiveStage.Finished:
                if (update.Files > 0 || update.Bytes > 0)
                {
                    OnCoverageTick(_coverageProgress.CreateSnapshot(), update.Files, update.Bytes);
                }
                else
                {
                    SetPhaseTotal(update.Entries);
                    CurrentPath = string.Empty;
                    CountText = "0 / 0";
                    ProgressValue = 1d;
                }

                return;

            default:
                _packTotal = update.Entries;
                SourceSize = $"{SizeFormatter.Format(update.Bytes)} · {update.Files:N0} {Plural.Word(update.Files, "файл", "файла", "файлов")}";
                SetPhaseTotal(update.Entries);
                return;
        }
    }

    private void OnProgressTick()
    {
        switch (_phase)
        {
            case ArchivePhase.Packing:
                OnTick(_packProgress.CreateSnapshot(), false);
                break;

            case ArchivePhase.Verifying:
                OnTick(_verifyProgress.CreateSnapshot(), true);
                break;

            case ArchivePhase.Coverage:
                var totals = _coverageTotals;

                if (totals.Files > 0 || totals.Bytes > 0)
                {
                    OnCoverageTick(_coverageProgress.CreateSnapshot(), totals.Files, totals.Bytes);
                }

                break;

            default:
                return;
        }
    }

    private void OnCoverageTick(OperationProgress update, int totalFiles, long totalBytes)
    {
        if (IsFinished)
        {
            return;
        }

        SetPhaseTotal(totalFiles > 0 ? totalFiles : 1);
        CountLabel = "Сверено";
        CurrentPath = update.Current;
        CountText = totalBytes > 0
            ? $"{update.Completed} / {totalFiles} · {SizeFormatter.Format(update.Bytes)} / {SizeFormatter.Format(totalBytes)}"
            : $"{update.Completed} / {totalFiles}";
        ProgressValue = CoverageFraction(update, totalFiles, totalBytes);
        StatusText = "Сверка архива с каталогом…";
    }

    private static double CoverageFraction(OperationProgress update, int totalFiles, long totalBytes)
    {
        if (totalBytes > 0)
        {
            return Math.Clamp((double)update.Bytes / totalBytes, 0d, 1d);
        }

        return totalFiles > 0 ? Math.Clamp((double)update.Completed / totalFiles, 0d, 1d) : 0d;
    }

    private void OnTick(OperationProgress update, bool verifying)
    {
        if (IsFinished)
        {
            return;
        }

        var total = verifying ? _verifyTotal : _packTotal;

        StatusText = verifying ? "Проверка архива…" : "Упаковка…";
        CountLabel = verifying ? "Проверено" : "Упаковано";
        CurrentPath = update.Current;
        CountText = total > 0 ? $"{update.Completed} / {total}" : update.Completed.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)update.Completed / total, 0d, 1d) : 0d;
        SetPhaseTotal(total);
    }

    private void SetPhaseTotal(int total)
    {
        if (_phaseTotal == total)
        {
            return;
        }

        _phaseTotal = total;
        OnPropertyChanged(nameof(IsIndeterminate));
    }
    private sealed record CoverageTotals(int Files, long Bytes)
    {
        internal static readonly CoverageTotals Empty = new(0, 0);
    }
}

public enum ArchiveStage
{
    None = 0,
    Packing = 1,
    Coverage = 2,
    Deleting = 3,
    Finished = 4,
}

internal enum ArchivePhase
{
    None = 0,
    Packing = 1,
    Verifying = 2,
    Coverage = 3,
}

public readonly record struct ArchiveStageUpdate(ArchiveStage Stage, int Files, int Entries, long Bytes);
