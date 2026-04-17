using KeepShell.Services.Modal;
using Microsoft.VisualBasic.FileIO;
using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class DeleteProgressDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly bool _permanent;
    private readonly long _totalBytes;
    private readonly ILogger _logger;

    private CancellationTokenSource? _cts;
    private long _freedBytes;
    private int _deleted;
    private int _failed;
    private bool _cancelled;

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
    private string _countText;

    [ObservableProperty]
    private string _freedText;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasErrors;

    public DeleteProgressDialogViewModel(IReadOnlyList<SpaceBase> items, bool permanent, ILogger logger)
    {
        _permanent = permanent;
        _logger = logger;
        Items = new(items.Select(static item => new DeleteRowViewModel(item)));
        _totalBytes = items.Sum(static item => item.TotalSize);

        ActionVerb = permanent ? "удалены безвозвратно" : "перемещены в корзину";
        IntroText = $"Будут {ActionVerb}: {Items.Count} элемент(ов) · {SizeFormatter.Format(_totalBytes)}";
        _countText = $"0 / {Items.Count}";
        _freedText = SizeFormatter.Format(0);
    }

    public event EventHandler<bool>? RequestClose;

    public ObservableCollection<DeleteRowViewModel> Items { get; }

    public IReadOnlyList<SpaceBase> DeletedItems { get; private set; } = [];

    public string Title => "Удаление";

    public string ActionVerb { get; }

    public string IntroText { get; }

    public bool IsIdle => !IsRunning && !IsFinished;

    public void RequestStop()
    {
        _cts?.Cancel();
    }

    private bool CanStart()
    {
        return IsIdle && Items.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        _cts = new();
        var token = _cts.Token;
        IsRunning = true;
        StatusText = "Удаление…";

        _logger.DeletionStarted(Items.Count, SizeFormatter.Format(_totalBytes), _permanent);

        var progress = new Progress<DeleteTick>(OnTick);
        List<SpaceBase>? deletedItems = null;

        try
        {
            deletedItems = await Task.Run(() => RunDeletion(progress, token), token);
        }
        catch (OperationCanceledException)
        {
            _cancelled = true;
        }
        catch (Exception exception)
        {
            _logger.DeleteItemFailed(exception, CurrentPath);
            StatusText = $"Ошибка: {exception.Message}";
        }
        finally
        {
            IsRunning = false;
            IsFinished = true;
            HasErrors = _failed > 0;
            StatusText = BuildSummary();
            DeletedItems = deletedItems ?? [];

            _logger.DeletionFinished(_cancelled, _deleted, _failed, SizeFormatter.Format(_freedBytes));

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
        RequestClose?.Invoke(this, _deleted > 0);
    }

    private void OnTick(DeleteTick tick)
    {
        var row = Items[tick.Index];
        row.State = tick.State;

        if (tick.Error is not null)
        {
            row.Error = tick.Error;
        }

        CurrentPath = row.Path;
        _freedBytes = tick.Freed;
        _deleted = tick.Completed;
        _failed = tick.Failed;
        CountText = $"{tick.Completed} / {Items.Count}";
        FreedText = SizeFormatter.Format(tick.Freed);
        ProgressValue = _totalBytes > 0 ? Math.Clamp((double)tick.Freed / _totalBytes, 0d, 1d) : 0d;
    }

    private List<SpaceBase> RunDeletion(IProgress<DeleteTick> progress, CancellationToken token)
    {
        var logPath = Path.Combine(AppContext.BaseDirectory, AppInfo.DeletionLogFileName);
        long freed = 0;
        var completed = 0;
        var failed = 0;
        var deletedItems = new List<SpaceBase>(Items.Count);

        using var writer = new StreamWriter(logPath, true);
        var buffer = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Удаление {Items.Count} элемент(ов) ({(_permanent ? "безвозвратно" : "в корзину")})";
        writer.WriteLine(buffer);

        for (var i = 0; i < Items.Count; i++)
        {
            token.ThrowIfCancellationRequested();

            var space = Items[i].Space;
            var path = space.AbsolutePath;
            progress.Report(new(i, DeleteRowState.Deleting, null, freed, completed, failed));

            try
            {
                bool removed;

                if (Directory.Exists(path))
                {
                    if (_permanent)
                    {
                        Directory.Delete(path, true);
                    }
                    else
                    {
                        FileSystem.DeleteDirectory(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }

                    removed = true;
                }
                else if (File.Exists(path))
                {
                    if (_permanent)
                    {
                        File.Delete(path);
                    }
                    else
                    {
                        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    }

                    removed = true;
                }
                else
                {
                    removed = false;
                }

                if (removed)
                {
                    writer.WriteLine(path);
                    completed++;
                    freed += space.TotalSize;
                    deletedItems.Add(space);
                    progress.Report(new(i, DeleteRowState.Done, null, freed, completed, failed));
                }
                else
                {
                    writer.WriteLine($"ПРОПУЩЕНО (не найден) {path}");
                    _logger.DeleteItemMissing(path);
                    failed++;
                    progress.Report(new(i, DeleteRowState.Failed, "Путь не найден", freed, completed, failed));
                }
            }
            catch (Exception exception)
            {
                writer.WriteLine($"ОШИБКА {path}: {exception.Message}");
                _logger.DeleteItemFailed(exception, path);
                failed++;
                progress.Report(new(i, DeleteRowState.Failed, exception.Message, freed, completed, failed));
            }
        }

        return deletedItems;
    }

    private string BuildSummary()
    {
        var verb = _permanent ? "Удалено безвозвратно" : "Перемещено в корзину";
        var head = _cancelled ? "Отменено." : "Готово.";
        var errors = _failed > 0 ? $" Ошибок: {_failed}." : string.Empty;
        return $"{head} {verb}: {_deleted} из {Items.Count}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}";
    }

    private readonly record struct DeleteTick(
        int Index,
        DeleteRowState State,
        string? Error,
        long Freed,
        int Completed,
        int Failed);
}
