using KeepShell.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly bool _basePermanent;
    private readonly IReadOnlySet<SpaceBase>? _withoutRecycleBin;
    private readonly PerformanceMonitor _monitor;
    private readonly PerformanceOperations _operations;
    private readonly ShellPreferences _preferences;
    private readonly UiProgressPoller _progressPoller;
    private readonly ILogger _logger;
    private readonly Action<SpaceBase>? _unmark;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    private bool _permanent;
    private bool _forcedByNetwork;

    private long _totalBytes;
    private DeleteOperationDiagnostics _diagnostics;
    private DeleteProgressState? _progress;
    private long _freedBytes;
    private int _deleted;
    private int _failed;
    private int _processed;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExcludeCommand))]
    private DeleteRowViewModel? _selectedItem;

    [ObservableProperty]
    private string _freedText;

    [ObservableProperty]
    private string? _diagnosticsText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChunkStalled))]
    private bool _isChunkRunning;

    [ObservableProperty]
    private string? _batchCaption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChunkStalled))]
    private string? _chunkElapsedText;

    [ObservableProperty]
    private int _followIndex = -1;

    public ObservableCollection<DeleteRowViewModel> Items { get; }

    public string WidestSizeText { get; }

    public IReadOnlyList<SpaceBase> DeletedItems { get; private set; } = [];

    public override string Title => "Удаление";

    public string ActionVerb => _permanent ? "удалены безвозвратно" : "перемещены в корзину";

    public bool ForcedByNetwork
    {
        get => _forcedByNetwork;
        private set => SetProperty(ref _forcedByNetwork, value);
    }

    public string StartButtonText => _permanent ? "Удалить безвозвратно" : "Удалить";

    public string IntroText => $"Будут {ActionVerb}: {Plural.Format(Items.Count, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_totalBytes)}";

    public string CancelHint => _permanent
        ? "Остановка сработает после текущего объекта – удаление каталога со всем содержимым прервать нельзя."
        : $"Остановка сработает в конце текущей пачки – это {Plural.Format(ChunkSize, "объект", "объекта", "объектов")}, обычно доли секунды.";

    public string CancelCaption => CancelRequested ? "Остановка запрошена…" : "Остановить удаление";

    public bool IsChunkStalled => IsChunkRunning && ChunkElapsedText is not null;

    protected override string RunningStatus => "Удаление…";

    protected override bool CloseResult => _deleted > 0;

    protected override bool HasFailedItems => _failed > 0;

    private int ChunkSize => Math.Clamp(_permanent ? 1 : AppDefaults.DeleteRecycleChunkSize, 1, Math.Max(Items.Count, 1));

    public DeleteProgressDialogViewModel(
        IReadOnlyList<SpaceBase> items,
        bool permanent,
        PerformanceMonitor monitor,
        PerformanceOperations operations,
        ShellPreferences preferences,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Action<SpaceBase>? unmark = null,
        IReadOnlySet<SpaceBase>? withoutRecycleBin = null)
    {
        _basePermanent = permanent;
        _withoutRecycleBin = withoutRecycleBin;
        var forcedByNetwork = withoutRecycleBin is { Count: > 0 };
        var effectivePermanent = permanent || forcedByNetwork;
        _permanent = effectivePermanent;
        _forcedByNetwork = forcedByNetwork;
        _monitor = monitor;
        _operations = operations;
        _preferences = preferences;
        _progressPoller = new(uiDispatcher, OnProgressTick);
        _logger = logger;
        _unmark = unmark;
        Items = new(items.Select(static item => new DeleteRowViewModel(item)));
        SelectedItem = Items.FirstOrDefault();
        _totalBytes = items.Sum(static item => item.TotalSize);
        _diagnostics = new(effectivePermanent, Items.Count, _totalBytes, monitor, operations, preferences);

        CountText = $"0 / {Items.Count}";

        WidestSizeText = Items.Select(static row => row.SizeText).MaxBy(static text => text.Length) ?? string.Empty;
        _freedText = SizeFormatter.Format(0);
    }

    internal static string? DescribeBatch(int firstIndex, int lastIndex, int total)
    {
        return firstIndex == lastIndex ? null : $"объекты {firstIndex + 1}–{lastIndex + 1} из {total}";
    }

    internal static string? DescribeElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(1))
        {
            return null;
        }

        return elapsed.TotalSeconds < 60
            ? $"идёт {(int)elapsed.TotalSeconds} с"
            : $"идёт {PerformanceText.Duration(elapsed)}";
    }

    internal static int FirstUnfinished(IReadOnlyList<DeleteRowViewModel> rows, int firstIndex, int lastIndex)
    {
        var end = Math.Min(lastIndex, rows.Count - 1);

        for (var index = Math.Max(firstIndex, 0); index <= end; index++)
        {
            if (rows[index].State is not (DeleteRowState.Done or DeleteRowState.Failed))
            {
                return index;
            }
        }

        return end;
    }

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
            await _progressPoller.StopAsync();
            _progress = null;
            ClearPulse();
        }
    }

    protected override void OnStarting()
    {
        ExcludeCommand.NotifyCanExecuteChanged();
        SelectPreviousCommand.NotifyCanExecuteChanged();
        SelectNextCommand.NotifyCanExecuteChanged();
        _diagnostics = new(_permanent, Items.Count, _totalBytes, _monitor, _operations, _preferences);
        _diagnostics.Start();
        _progressPoller.Start();
        _logger.DeletionStarted(_runId, Items.Count, SizeFormatter.Format(_totalBytes), _permanent);
    }

    protected override void OnFinished()
    {
        _progressPoller.Stop();

        _diagnostics.Finish(_processed, _freedBytes, !Cancelled && Failure is null);
        DiagnosticsText = null;

        if (Failure is { } failure)
        {
            _logger.DeletionFailed(failure, _runId, _processed, Items.Count);
        }

        _logger.DeletionFinished(_runId, Cancelled, _deleted, _failed, SizeFormatter.Format(_freedBytes));
    }

    protected override bool CanStart()
    {
        return base.CanStart() && Items.Count > 0;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CancelRequested))
        {
            OnPropertyChanged(nameof(CancelCaption));
        }
    }

    protected override string BuildSummary()
    {
        if (Failure is { } failure)
        {
            return $"Ошибка: {failure.Message}";
        }

        var verb = _permanent ? "Удалено безвозвратно" : "Перемещено в корзину";
        var head = Cancelled ? "Отменено." : "Готово.";
        if (!Cancelled && _failed > 0)
        {
            head = "Завершено с ошибками.";
        }

        var errors = _failed > 0 ? $" Ошибок: {_failed}." : string.Empty;
        var partial = _permanent && Items.Any(static row => row.IsDirectory && row.State == DeleteRowState.Failed)
            ? " Каталоги с ошибкой могли быть удалены частично. Повторите сканирование оставшихся данных."
            : string.Empty;

        return $"{head} {verb}: {_deleted} из {Items.Count}. Освобождено: {SizeFormatter.Format(_freedBytes)}.{errors}{partial}";
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
        ApplyChunk(snapshot.Chunk);

        DiagnosticsText = _diagnostics.Report(_processed, _freedBytes);
    }

    private static string DescribeRange(in DeleteChunkInfo chunk)
    {
        return chunk.FirstIndex == chunk.LastIndex
            ? $"{chunk.FirstIndex + 1}"
            : $"{chunk.FirstIndex + 1}–{chunk.LastIndex + 1}";
    }

    private void ApplyChunk(in DeleteChunkProgress chunk)
    {
        if (!chunk.Running)
        {
            ClearPulse();
            return;
        }

        BatchCaption = DescribeBatch(chunk.FirstIndex, chunk.LastIndex, Items.Count);
        ChunkElapsedText = DescribeElapsed(Stopwatch.GetElapsedTime(chunk.StartedTimestamp));
        FollowIndex = FirstUnfinished(Items, chunk.FirstIndex, chunk.LastIndex);
        IsChunkRunning = true;
    }

    private void ClearPulse()
    {
        IsChunkRunning = false;
        BatchCaption = null;
        ChunkElapsedText = null;
        FollowIndex = -1;
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

            DeleteBatch.Run(
                paths,
                ChunkSize,
                _diagnostics.BuildCallbacks(),
                new(OnStarting, OnResult, OnChunkStarting, OnChunkFinished),
                token);
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

        void OnChunkStarting(DeleteChunkInfo chunk)
        {
            progress.ReportChunkStarted(chunk);
            _logger.DeleteChunkStarted(_runId, chunk.Ordinal, chunk.Total, DescribeRange(chunk));
        }

        void OnChunkFinished(DeleteChunkOutcome outcome)
        {
            progress.ReportChunkFinished();

            var chunk = outcome.Chunk;
            var range = DescribeRange(chunk);

            _logger.DeleteChunkFinished(_runId, chunk.Ordinal, chunk.Total, range, outcome.ElapsedMs, outcome.RetriedOneByOne);

            if (outcome.ElapsedMs >= AppDefaults.DeleteChunkWarnMs)
            {
                _logger.DeleteChunkSlow(_runId, chunk.Ordinal, chunk.Total, range, outcome.ElapsedMs, AppDefaults.DeleteChunkWarnMs);
            }
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
