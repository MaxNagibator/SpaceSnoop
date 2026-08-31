using KeepShell.ViewModels;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    internal const int ProgressPollIntervalMs = 120;

    private const double AutomationChunkSeconds = 12.4;

    internal static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(ProgressPollIntervalMs);

    private readonly bool _permanent;
    private readonly long _totalBytes;
    private readonly DeleteOperationDiagnostics _diagnostics;
    private readonly IUiTimer _progressTimer;
    private readonly ILogger _logger;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    private CancellationToken _token;
    private DeleteProgressState? _progress;
    private long _freedBytes;
    private int _deleted;
    private int _failed;
    private int _processed;

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

    // TODO: наблюдаемого CancelRequested в пакете KeepShell ещё нет, поэтому запрошенная отмена
    // считается по токену на тике опроса – после бампа пакета убрать CancelPending и читать базовое свойство
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CancelCaption))]
    private bool _cancelPending;

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
        CancelHint = permanent
            ? "Отмена сработает после текущего объекта – удаление каталога со всем содержимым прервать нельзя."
            : $"Отмена сработает в конце текущей пачки – это {Plural.Format(ChunkSize, "объект", "объекта", "объектов")}, обычно доли секунды.";
        WidestSizeText = Items.Select(static row => row.SizeText).MaxBy(static text => text.Length) ?? string.Empty;
        _freedText = SizeFormatter.Format(0);
    }

    public ObservableCollection<DeleteRowViewModel> Items { get; }

    public string WidestSizeText { get; }

    public IReadOnlyList<SpaceBase> DeletedItems { get; private set; } = [];

    public override string Title => "Удаление";

    public string ActionVerb { get; }

    public string IntroText { get; }

    public string CancelHint { get; }

    public string CancelCaption => CancelPending ? "Отмена запрошена…" : "Отменить удаление";

    public bool IsChunkStalled => IsChunkRunning && ChunkElapsedText is not null;

    protected override string RunningStatus => "Удаление…";

    protected override bool CloseResult => _deleted > 0;

    protected override bool HasFailedItems => _failed > 0;

    private int ChunkSize => Math.Clamp(_permanent ? 1 : AppDefaults.DeleteRecycleChunkSize, 1, Math.Max(Items.Count, 1));

    protected override async Task ExecuteAsync(CancellationToken token)
    {
        var progress = new DeleteProgressState(Items.Count);
        _progress = progress;
        _token = token;
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
            ClearPulse();
        }
    }

    protected override void OnStarting()
    {
        _diagnostics.Start();
        _progressTimer.Start();
        _logger.DeletionStarted(_runId, Items.Count, SizeFormatter.Format(_totalBytes), _permanent);
    }

    protected override void OnFinished()
    {
        _progressTimer.Stop();
        OnProgressTick();

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

        if (Items.Count > 0)
        {
            var started = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * AutomationChunkSeconds);
            ApplyChunk(new(true, 1, 1, 0, Math.Min(Items.Count, ChunkSize) - 1, started));
        }
    }

    internal void ShowFinishedForAutomation()
    {
        ShowRunningForAutomation(Items.Count);
        ClearPulse();

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
        CancelPending = false;
        ClearPulse();
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
            : $"идёт {PerformanceFormat.Duration(elapsed)}";
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

    private void OnProgressTick()
    {
        if (_token.IsCancellationRequested)
        {
            CancelPending = true;
        }

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
