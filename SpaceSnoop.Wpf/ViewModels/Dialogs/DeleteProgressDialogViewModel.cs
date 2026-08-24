using KeepShell.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly bool _permanent;
    private readonly long _totalBytes;
    private readonly ILogger _logger;

    private long _freedBytes;
    private int _deleted;
    private int _failed;

    [ObservableProperty]
    private string _freedText;

    public DeleteProgressDialogViewModel(IReadOnlyList<SpaceBase> items, bool permanent, ILogger logger)
    {
        _permanent = permanent;
        _logger = logger;
        Items = new(items.Select(static item => new DeleteRowViewModel(item)));
        _totalBytes = items.Sum(static item => item.TotalSize);

        ActionVerb = permanent ? "удалены безвозвратно" : "перемещены в корзину";
        IntroText = $"Будут {ActionVerb}: {Plural.Format(Items.Count, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_totalBytes)}";
        CountText = $"0 / {Items.Count}";
        WidestSizeText = Items.Select(static row => row.SizeText).MaxBy(static text => text.Length) ?? string.Empty;
        _freedText = SizeFormatter.Format(0);
    }

    public ObservableCollection<DeleteRowViewModel> Items { get; }

    public string WidestSizeText { get; }

    public IReadOnlyList<SpaceBase> DeletedItems { get; private set; } = [];

    public override string Title => "Удаление";

    public string ActionVerb { get; }

    public string IntroText { get; }

    protected override string RunningStatus => "Удаление…";

    protected override bool CloseResult => _deleted > 0;

    protected override bool HasFailedItems => _failed > 0;

    private int ChunkSize => _permanent ? 1 : AppDefaults.DeleteRecycleChunkSize;

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var progress = new Progress<DeleteTick>(OnTick);
        var deletedItems = new List<SpaceBase>(Items.Count);
        DeletedItems = deletedItems;

        await Task.Run(() => RunDeletion(deletedItems, progress, token), token);
    }

    protected override void OnStarting()
    {
        _logger.DeletionStarted(Items.Count, SizeFormatter.Format(_totalBytes), _permanent);
    }

    protected override void OnFinished()
    {
        if (Failure is { } failure)
        {
            _logger.DeleteItemFailed(failure, CurrentPath);
        }

        _logger.DeletionFinished(Cancelled, _deleted, _failed, SizeFormatter.Format(_freedBytes));
    }

    protected override bool CanStart()
    {
        return base.CanStart() && Items.Count > 0;
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        var verb = _permanent ? "Удалено безвозвратно" : "Перемещено в корзину";
        var head = Cancelled ? "Отменено." : "Готово.";
        var errors = _failed > 0 ? $" Ошибок: {_failed}." : string.Empty;
        return $"{head} {verb}: {_deleted} из {Items.Count}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}";
    }

    internal void ShowRunningForAutomation(int done)
    {
        IsRunning = true;
        StatusText = RunningStatus;

        for (var index = 0; index < Items.Count && index < done; index++)
        {
            _freedBytes += Items[index].Space.TotalSize;
            OnTick(new(index, DeleteRowState.Done, null, _freedBytes, index + 1, 0));
        }

        if (done < Items.Count)
        {
            OnTick(new(done, DeleteRowState.Deleting, null, _freedBytes, done, 0));
        }
    }

    internal void ShowFinishedForAutomation()
    {
        ShowRunningForAutomation(Items.Count);

        IsRunning = false;
        IsFinished = true;
        HasErrors = HasFailedItems;
        StatusText = BuildSummary();
    }

    internal void ClearForAutomation()
    {
        IsRunning = false;
        IsFinished = false;
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

    private static bool PathExists(string path)
    {
        return Directory.Exists(path) || File.Exists(path);
    }

    private static void DeletePermanent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Путь не найден", path);
        }

        File.Delete(path);
    }

    private static void DeletePermanentChunk(IReadOnlyList<string> chunk)
    {
        foreach (var path in chunk)
        {
            DeletePermanent(path);
        }
    }

    private void RunDeletion(List<SpaceBase> deletedItems, IProgress<DeleteTick> progress, CancellationToken token)
    {
        var logPath = Path.Combine(AppStorage.DataDirectory, AppInfo.DeletionLogFileName);
        long freed = 0;
        var completed = 0;
        var failed = 0;
        var journalBroken = false;
        var paths = new string[Items.Count];

        for (var i = 0; i < paths.Length; i++)
        {
            paths[i] = Items[i].Path;
        }

        var writer = new StreamWriter(logPath, true);

        try
        {
            Journal($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Удаление {Items.Count} элемент(ов) ({(_permanent ? "безвозвратно" : "в корзину")})");

            DeleteBatch.Run(paths, ChunkSize, BuildCallbacks(), OnStarting, OnResult, token);
        }
        finally
        {
            try
            {
                writer.Dispose();
            }
            catch (Exception exception)
            {
                ReportJournalFailure(exception);
            }
        }

        void Journal(string line)
        {
            if (journalBroken)
            {
                return;
            }

            try
            {
                writer.WriteLine(line);
            }
            catch (Exception exception)
            {
                ReportJournalFailure(exception);
            }
        }

        void ReportJournalFailure(Exception exception)
        {
            if (journalBroken)
            {
                return;
            }

            journalBroken = true;

            try
            {
                _logger.DeletionLogWriteFailed(exception, logPath);
            }
            catch (Exception)
            {
            }
        }

        void OnStarting(int index)
        {
            progress.Report(new(index, DeleteRowState.Deleting, null, freed, completed, failed));
        }

        void OnResult(DeleteItemResult result)
        {
            var path = paths[result.Index];

            switch (result.Status)
            {
                case DeleteItemStatus.Removed:
                    Journal(path);
                    completed++;
                    freed += Items[result.Index].Space.TotalSize;
                    deletedItems.Add(Items[result.Index].Space);
                    progress.Report(new(result.Index, DeleteRowState.Done, null, freed, completed, failed));
                    break;

                case DeleteItemStatus.Missing:
                    Journal($"ПРОПУЩЕНО (не найден) {path}");
                    _logger.DeleteItemMissing(path);
                    failed++;
                    progress.Report(new(result.Index, DeleteRowState.Failed, "Путь не найден", freed, completed, failed));
                    break;

                case DeleteItemStatus.Failed when result.Failure is { } failure:
                    Journal($"ОШИБКА {path}: {failure.Message}");
                    _logger.DeleteItemFailed(failure, path);
                    failed++;
                    progress.Report(new(result.Index, DeleteRowState.Failed, failure.Message, freed, completed, failed));
                    break;

                default:
                    progress.Report(new(result.Index, DeleteRowState.Pending, null, freed, completed, failed));
                    break;
            }
        }
    }

    private DeleteBatchCallbacks BuildCallbacks()
    {
        return _permanent
            ? new(PathExists, DeletePermanentChunk, DeletePermanent)
            : new(PathExists, RecycleBin.DeleteSilent, RecycleBin.DeleteSilent);
    }

    private readonly record struct DeleteTick(
        int Index,
        DeleteRowState State,
        string? Error,
        long Freed,
        int Completed,
        int Failed);
}
