using KeepShell.Services.Modal;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class ArchiveProgressDialogViewModel : ObservableObject, IDialogViewModel, ICancelableDialog
{
    private readonly ArchiveRequest _request;
    private readonly ArchiveService _service;
    private readonly ILogger _logger;
    private readonly int _total;

    private CancellationTokenSource? _cts;
    private bool _cancelled;
    private bool _packed;
    private bool _failed;
    private int _verifyTotal;
    private string _error = string.Empty;
    private string _resultSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isFinished;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _currentPath = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    [ObservableProperty]
    private string _countLabel = "Упаковано файлов";

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasErrors;

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

    public event EventHandler<bool>? RequestClose;

    public string Title => "Упаковка в архив";

    public string ActionText => "Упаковать";

    public string SourceName { get; }

    public string SourceSize { get; }

    public string TargetName { get; }

    public string FateText { get; }

    public bool OriginalDeleted { get; private set; }

    public string? CreatedArchivePath { get; private set; }

    public bool IsIdle => !IsRunning && !IsFinished;

    public bool IsIndeterminate => _total == 0;

    public bool CanCancel => !IsRunning;

    public void RequestStop()
    {
        _cts?.Cancel();
    }

    public async Task StopAsync()
    {
        if (_cts is { } cts)
        {
            await cts.CancelAsync();
        }

        if (StartCommand.ExecutionTask is not { } run)
        {
            return;
        }

        try
        {
            await run;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool CanStart()
    {
        return IsIdle;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _cts = new();
        var token = _cts.Token;
        IsRunning = true;
        StatusText = "Упаковка…";

        _logger.ArchiveStarted(_request.SourcePath);

        var packProgress = new Progress<OperationProgress>(update => OnTick(update, false));
        var verifyProgress = new Progress<OperationProgress>(update => OnTick(update, true));

        try
        {
            await Task.Run(() => ExecuteZip(packProgress, verifyProgress, token), token);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
            _logger.ArchiveCancelled(_request.SourcePath);
        }
        catch (Exception exception)
        {
            _failed = true;
            _error = exception.Message;
            _logger.ArchiveFailed(exception, _request.SourcePath);
        }
        finally
        {
            IsRunning = false;
            IsFinished = true;
            HasErrors = _failed;
            StatusText = BuildSummary();

            if (!_failed && !_cancelled)
            {
                _logger.ArchiveFinished(_request.SourcePath, _resultSummary);
            }

            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanCancelRun()
    {
        return IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun()
    {
        StatusText = "Отмена…";
        _cts?.Cancel();
    }

    private bool CanClose()
    {
        return !IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanClose))]
    private void Close()
    {
        RequestClose?.Invoke(this, OriginalDeleted);
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
        var total = verifying ? _verifyTotal : _total;

        StatusText = verifying ? "Проверка архива…" : "Упаковка…";
        CountLabel = verifying ? "Проверено файлов" : "Упаковано файлов";
        CurrentPath = update.Current;
        CountText = total > 0 ? $"{update.Completed} / {total}" : update.Completed.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)update.Completed / total, 0d, 1d) : 0d;
    }

    private string BuildSummary()
    {
        if (_cancelled)
        {
            return _packed
                ? $"Отменено на проверке: архив {TargetName} создан, но не проверен. Оригинал не тронут."
                : "Отменено.";
        }

        if (_failed)
        {
            return $"Ошибка: {_error}";
        }

        return OriginalDeleted
            ? $"Готово. Архив {_resultSummary}. Оригинал → в корзину."
            : $"Готово. Архив {_resultSummary}.";
    }
}
