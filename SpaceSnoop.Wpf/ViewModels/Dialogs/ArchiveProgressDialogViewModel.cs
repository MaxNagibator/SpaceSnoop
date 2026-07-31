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
    private bool _failed;
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
            ? "После проверки архива оригинал отправится в корзину."
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
        _cts?.Cancel();

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
        StatusText = $"{ActionText}…";

        _logger.ArchiveStarted(_request.SourcePath);

        var progress = new Progress<OperationProgress>(OnTick);

        try
        {
            await Task.Run(() => ExecuteZip(progress, token), token);
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

    private void ExecuteZip(IProgress<OperationProgress> progress, CancellationToken token)
    {
        var stats = _service.ZipFiles(_request.SourcePath, _request.Files, _request.TargetPath, _request.Level, progress, token);

        var (ok, detail) = _service.VerifyZip(_request.TargetPath, stats);

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

    private void OnTick(OperationProgress update)
    {
        CurrentPath = update.Current;
        CountText = _total > 0 ? $"{update.Completed} / {_total}" : update.Completed.ToString("N0");
        ProgressValue = _total > 0 ? Math.Clamp((double)update.Completed / _total, 0d, 1d) : 0d;
    }

    private string BuildSummary()
    {
        if (_cancelled)
        {
            return "Отменено.";
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
