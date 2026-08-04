using KeepShell.ViewModels;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class ArchiveProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly ArchiveRequest _request;
    private readonly ArchiveService _service;
    private readonly ILogger _logger;
    private readonly int _total;

    private bool _packed;
    private int _verifyTotal;
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    private string _countLabel = "Упаковано файлов";

    public ArchiveProgressDialogViewModel(ArchiveRequest request, ArchiveService service, ILogger logger)
    {
        _request = request;
        _service = service;
        _logger = logger;
        _total = request.Files.Count;

        SourceName = Path.GetFileName(request.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SourceSize = $"{SizeFormatter.Format(request.TotalBytes)} · {_total:N0} файлов";
        TargetName = Path.GetFileName(request.TargetPath);
        FateText = request.DeleteOriginal
            ? "Архив будет прочитан целиком с проверкой контрольных сумм, и только потом оригинал отправится в корзину."
            : "Оригинал останется на месте.";
    }

    public override string Title => "Упаковка в архив";

    public string ActionText => "Упаковать";

    public string SourceName { get; }

    public string SourceSize { get; }

    public string TargetName { get; }

    public string FateText { get; }

    public bool OriginalDeleted { get; private set; }

    public string? CreatedArchivePath { get; private set; }

    public bool IsIndeterminate => _total == 0;

    protected override string RunningStatus => "Упаковка…";

    protected override bool CloseResult => OriginalDeleted;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var packProgress = new Progress<OperationProgress>(update => OnTick(update, false));
        var verifyProgress = new Progress<OperationProgress>(update => OnTick(update, true));

        await Task.Run(() => ExecuteZip(packProgress, verifyProgress, token), token);
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

        return OriginalDeleted
            ? $"Готово. Архив {_resultSummary}. Оригинал → в корзину."
            : $"Готово. Архив {_resultSummary}.";
    }

    private void ExecuteZip(IProgress<OperationProgress> packProgress, IProgress<OperationProgress> verifyProgress, CancellationToken token)
    {
        var stats = _service.ZipFiles(_request.SourcePath, _request.Files, _request.TargetPath, _request.Level, packProgress, token);

        _packed = true;
        _verifyTotal = stats.Count;

        var (ok, detail) = _service.VerifyZip(_request.TargetPath, stats, _request.DeleteOriginal, verifyProgress, token);

        if (!ok)
        {
            _logger.ArchiveVerifyFailed(_request.TargetPath, detail);
            _service.SafeDelete(_request.TargetPath);
            throw new InvalidOperationException($"Архив не прошёл проверку ({detail}); оригинал не тронут.");
        }

        token.ThrowIfCancellationRequested();

        var compressed = new FileInfo(_request.TargetPath).Length;
        var ratio = stats.Bytes > 0 ? (1 - (double)compressed / stats.Bytes) * 100 : 0;
        _resultSummary = $"{SizeFormatter.Format(compressed)} (было {SizeFormatter.Format(stats.Bytes)}, −{ratio:F0} %)";
        CreatedArchivePath = _request.TargetPath;

        if (_request.DeleteOriginal)
        {
            _service.DeleteDirectoryToRecycleBin(_request.SourcePath, _request.Interactive);
            OriginalDeleted = true;
        }
    }

    private void OnTick(OperationProgress update, bool verifying)
    {
        if (IsFinished)
        {
            return;
        }

        var total = verifying ? _verifyTotal : _total;

        StatusText = verifying ? "Проверка архива…" : "Упаковка…";
        CountLabel = verifying ? "Проверено файлов" : "Упаковано файлов";
        CurrentPath = update.Current;
        CountText = total > 0 ? $"{update.Completed} / {total}" : update.Completed.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)update.Completed / total, 0d, 1d) : 0d;
    }
}
