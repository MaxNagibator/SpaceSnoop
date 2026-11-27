using KeepShell.Diagnostics;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly bool _basePermanent;
    private readonly PerformanceMonitor _monitor;
    private readonly PerformanceOperations _operations;
    private readonly ShellPreferences _preferences;
    private readonly UiProgressPoller _progressPoller;
    private readonly ILogger _logger;
    private readonly Action<SpaceBase>? _unmark;
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    private bool _allPermanent;
    private bool _forcedByNetwork;
    private bool _isMixed;
    private int _networkCount;
    private int _recycleCount;
    private int _permanentCount;
    private long _recycleBytes;
    private long _permanentBytes;

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

    public string ActionVerb => IsMixed
        ? "перемещены в корзину или удалены безвозвратно"
        : _allPermanent
            ? "удалены безвозвратно"
            : "перемещены в корзину";

    public bool ForcedByNetwork
    {
        get => _forcedByNetwork;
        private set => SetProperty(ref _forcedByNetwork, value);
    }

    public bool IsMixed
    {
        get => _isMixed;
        private set => SetProperty(ref _isMixed, value);
    }

    public string NetworkWarningText => IsMixed
        ? "На сетевом хранилище нет корзины Windows – сетевые объекты будут удалены безвозвратно, локальные – в корзину"
        : "На сетевом хранилище нет корзины Windows – удаление необратимо";

    public string StartButtonText => IsMixed
        ? "Удалить (часть безвозвратно)"
        : _allPermanent
            ? "Удалить безвозвратно"
            : "Удалить";

    public string IntroText => IsMixed
        ? $"В корзину: {Plural.Format(_recycleCount, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_recycleBytes)}, безвозвратно: {Plural.Format(_permanentCount, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_permanentBytes)}"
        : $"Будут {ActionVerb}: {Plural.Format(Items.Count, "объект", "объекта", "объектов")} · {SizeFormatter.Format(_totalBytes)}";

    public string CancelHint => IsMixed
        ? $"Остановка сработает после текущей пачки в корзину (до {Plural.Format(RecycleChunkSize, "объект", "объекта", "объектов")}) или текущего сетевого объекта."
        : _allPermanent
            ? "Остановка сработает после текущего объекта – удаление каталога со всем содержимым прервать нельзя."
            : $"Остановка сработает в конце текущей пачки – это {Plural.Format(RecycleChunkSize, "объект", "объекта", "объектов")}, обычно доли секунды.";

    public string CancelCaption => CancelRequested ? "Остановка запрошена…" : "Остановить удаление";

    public bool IsChunkStalled => IsChunkRunning && ChunkElapsedText is not null;

    protected override string RunningStatus => "Удаление…";

    protected override bool CloseResult => _deleted > 0;

    protected override bool HasFailedItems => _failed > 0;

    private int RecycleChunkSize => Math.Clamp(AppDefaults.DeleteRecycleChunkSize, 1, Math.Max(_recycleCount, 1));

    private int FirstChunkSize => Items.Count > 0 && Items[0].IsPermanent ? 1 : RecycleChunkSize;

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
        _monitor = monitor;
        _operations = operations;
        _preferences = preferences;
        _progressPoller = new(uiDispatcher, OnProgressTick);
        _logger = logger;
        _unmark = unmark;

        var count = items.Count;
        var rows = new DeleteRowViewModel[count];
        var widestSizeText = string.Empty;

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var isWithoutRecycleBin = withoutRecycleBin?.Contains(item) ?? false;
            var isRowPermanent = permanent || isWithoutRecycleBin;
            var row = new DeleteRowViewModel(item, isRowPermanent, isWithoutRecycleBin);
            rows[i] = row;

            if (isWithoutRecycleBin)
            {
                _networkCount++;
            }

            if (isRowPermanent)
            {
                _permanentCount++;
                _permanentBytes += row.SizeBytes;
            }
            else
            {
                _recycleCount++;
                _recycleBytes += row.SizeBytes;
            }

            if (row.SizeText.Length > widestSizeText.Length)
            {
                widestSizeText = row.SizeText;
            }
        }

        _totalBytes = _recycleBytes + _permanentBytes;
        _forcedByNetwork = _networkCount > 0;
        _isMixed = !permanent && _recycleCount > 0 && _permanentCount > 0;
        _allPermanent = permanent || (count > 0 && _recycleCount == 0);

        Items = new(rows);
        SelectedItem = count > 0 ? Items[0] : null;
        _diagnostics = new(_allPermanent, count, _totalBytes, monitor, operations, preferences);

        CountText = $"0 / {count}";

        WidestSizeText = widestSizeText;
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
}
