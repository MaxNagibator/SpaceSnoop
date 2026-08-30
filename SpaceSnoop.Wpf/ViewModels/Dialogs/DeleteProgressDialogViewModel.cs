using KeepShell.ViewModels;
using System.Collections.ObjectModel;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    internal const int ProgressPollIntervalMs = 120;

    internal static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(ProgressPollIntervalMs);

    private readonly bool _permanent;
    private readonly long _totalBytes;
    private readonly DeleteOperationDiagnostics _diagnostics;
    private readonly IUiTimer _progressTimer;
    private readonly ILogger _logger;

    private DeleteProgressState? _progress;
    private long _freedBytes;
    private int _deleted;
    private int _failed;
    private int _processed;

    [ObservableProperty]
    private string _freedText;

    [ObservableProperty]
    private string? _diagnosticsText;

    public DeleteProgressDialogViewModel(
        IReadOnlyList<SpaceBase> items,
        bool permanent,
        PerformanceMonitor performance,
        PerformanceRunTracker runs,
        ShellPreferences preferences,
        IUiDispatcher uiDispatcher,
        ILogger logger)
    {
        _permanent = permanent;
        _progressTimer = uiDispatcher.CreateTimer(ProgressPollInterval, OnProgressTick);
        _logger = logger;
        Items = new(items.Select(static item => new DeleteRowViewModel(item)));
        _totalBytes = items.Sum(static item => item.TotalSize);
        _diagnostics = new(permanent, Items.Count, _totalBytes, performance, runs, preferences);

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
        var progress = new DeleteProgressState(Items.Count);
        _progress = progress;
        var deletedItems = new List<SpaceBase>(Items.Count);
        DeletedItems = deletedItems;

        try
        {
            await Task.Run(() => RunDeletion(deletedItems, progress, token), token);
        }
        finally
        {
            _progressTimer.Stop();
            OnProgressTick();
            _progress = null;
        }
    }

    protected override void OnStarting()
    {
        _diagnostics.Start();
        _progressTimer.Start();
        _logger.DeletionStarted(Items.Count, SizeFormatter.Format(_totalBytes), _permanent);
    }

    protected override void OnFinished()
    {
        _progressTimer.Stop();
        OnProgressTick();

        _diagnostics.Finish(_processed, _freedBytes, !Cancelled && Failure is null);
        DiagnosticsText = null;

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
        DiagnosticsText = "отклик 6 мс · пик 18 мс · 118 файлов/с · осталось 31 с";

        for (var index = 0; index < Items.Count && index < done; index++)
        {
            _freedBytes += Items[index].Space.TotalSize;
            ApplyTick(new(index, DeleteRowState.Done, null, 0, 0, 0));
        }

        ApplyAggregate(_freedBytes, done, 0);

        if (done < Items.Count)
        {
            ApplyTick(new(done, DeleteRowState.Deleting, null, 0, 0, 0));
            CurrentPath = Items[done].Path;
        }
    }

    internal void ShowFinishedForAutomation()
    {
        ShowRunningForAutomation(Items.Count);

        IsRunning = false;
        IsFinished = true;
        HasErrors = HasFailedItems;
        StatusText = BuildSummary();
        DiagnosticsText = null;
    }

    internal void ClearForAutomation()
    {
        IsRunning = false;
        IsFinished = false;
        DiagnosticsText = null;
    }

    private void OnProgressTick()
    {
        if (_progress is not { } progress)
        {
            return;
        }

        var snapshot = progress.CreateSnapshot();

        foreach (var tick in snapshot.Updates)
        {
            ApplyTick(tick);
        }

        if ((uint)snapshot.CurrentIndex < (uint)Items.Count)
        {
            CurrentPath = Items[snapshot.CurrentIndex].Path;
        }

        ApplyAggregate(snapshot.Freed, snapshot.Completed, snapshot.Failed);

        DiagnosticsText = _diagnostics.Report(_processed, _freedBytes);
    }

    private void ApplyTick(DeleteTick tick)
    {
        var row = Items[tick.Index];
        row.State = tick.State;

        if (tick.Error is not null)
        {
            row.Error = tick.Error;
        }
    }

    private void ApplyAggregate(long freed, int completed, int failed)
    {
        _freedBytes = freed;
        _deleted = completed;
        _failed = failed;
        _processed = completed + failed;
        CountText = $"{completed} / {Items.Count}";
        FreedText = SizeFormatter.Format(freed);
        ProgressValue = Items.Count > 0 ? Math.Clamp((double)_processed / Items.Count, 0d, 1d) : 0d;
    }

    private void RunDeletion(List<SpaceBase> deletedItems, DeleteProgressState progress, CancellationToken token)
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

            DeleteBatch.Run(paths, ChunkSize, _diagnostics.BuildCallbacks(), OnStarting, OnResult, token);
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
}
