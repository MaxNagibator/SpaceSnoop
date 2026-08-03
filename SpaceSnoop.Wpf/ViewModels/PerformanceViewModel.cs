using System.Collections.Immutable;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceViewModel : ObservableObject, IPageHeader
{
    private readonly PerformanceMonitor _monitor;

    private readonly PerformanceRunTracker _runs;

    private readonly ToastNotifier _notifier;

    private readonly IClipboardService _clipboard;

    private bool _active;

    [ObservableProperty]
    private string _delayText = string.Empty;

    [ObservableProperty]
    private string _delayHint = string.Empty;

    [ObservableProperty]
    private bool _isHitch;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _memoryHint = string.Empty;

    [ObservableProperty]
    private string _memoryPeakText = string.Empty;

    [ObservableProperty]
    private string _collectionsText = string.Empty;

    [ObservableProperty]
    private string _collectionsWindowText = string.Empty;

    [ObservableProperty]
    private string _startupText = string.Empty;

    [ObservableProperty]
    private string _startupHint = string.Empty;

    [ObservableProperty]
    private string _renderText = string.Empty;

    [ObservableProperty]
    private string _renderHint = string.Empty;

    [ObservableProperty]
    private bool _isRenderSlow;

    [ObservableProperty]
    private string _windowText = string.Empty;

    [ObservableProperty]
    private string? _staleText;

    [ObservableProperty]
    private string _operationCaption = string.Empty;

    [ObservableProperty]
    private string _operationText = string.Empty;

    [ObservableProperty]
    private string _operationVolume = string.Empty;

    [ObservableProperty]
    private string _operationRate = string.Empty;

    [ObservableProperty]
    private string? _operationTraversal;

    [ObservableProperty]
    private string? _operationTraversalDetail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHitches))]
    private ImmutableArray<PerformanceHitchText> _hitches = [];

    [ObservableProperty]
    private string _hitchesCaption = string.Empty;

    public PerformanceViewModel(
        PerformanceMonitor monitor,
        PerformanceRunTracker runs,
        PerformanceChartViewModel chart,
        ShellPreferences preferences,
        ToastNotifier notifier,
        IClipboardService clipboard)
    {
        _monitor = monitor;
        _runs = runs;
        _notifier = notifier;
        _clipboard = clipboard;

        Chart = chart;
        Chart.ChartHeight = AppDefaults.PerformanceChartPageHeight;
        Chart.Refreshed += OnChartRefreshed;
        Preferences = preferences;

        _monitor.Updated += OnMonitorUpdated;
        _runs.Changed += OnRunsChanged;
        Apply(_monitor.Snapshot);
        ApplyHitches();
    }

    public PerformanceChartViewModel Chart { get; }

    public ShellPreferences Preferences { get; }

    public bool HasHitches => Hitches.Length > 0;

    public string HitchesHint => PerformanceFormat.HitchesHint;

    public string PageTitle => "Производительность";

    public string? PageDescription =>
        $"Задержка UI-потока, память и сборки мусора. Замеры живут {AppDefaults.PerformanceHistorySecondsMax / 60} мин, окно графика выбирается полосой, просадка – от {AppDefaults.PerformanceHitchMs} мс.";

    public void SetActive(bool active)
    {
        _active = active;
        Chart.SetActive(active);

        if (!active)
        {
            return;
        }

        _monitor.Start();
        Apply(_monitor.Snapshot);
    }

    [RelayCommand]
    private void Reset()
    {
        _monitor.Reset();
        _runs.Clear();
        Apply(_monitor.Snapshot);
        Chart.Refresh();
        _notifier.Notify("Замеры производительности сброшены");
    }

    [RelayCommand]
    private void CopySummary()
    {
        if (_clipboard.TrySetText(PerformanceReport.Build(_monitor.Snapshot, AppInfo.Version, _runs.Last)))
        {
            _notifier.Notify("Сводка скопирована в буфер обмена");
        }
        else
        {
            _notifier.Notify("Не удалось скопировать сводку", StatusSeverity.Warning);
        }
    }

    private void OnMonitorUpdated(object? sender, EventArgs e)
    {
        if (_active)
        {
            Apply(_monitor.Snapshot);
        }
    }

    private void OnChartRefreshed(object? sender, EventArgs e)
    {
        StaleText = PerformanceFormat.StaleWarning(_monitor.Snapshot, DateTime.UtcNow);
        ApplyHitches();
    }

    private void ApplyHitches()
    {
        var hitches = _monitor.CaptureHitches(AppDefaults.PerformanceHitchMs, AppDefaults.PerformanceHitchRowsMax);
        var rows = ImmutableArray.CreateRange(hitches.Rows, PerformanceFormat.HitchText);

        if (!Hitches.AsSpan().SequenceEqual(rows.AsSpan()))
        {
            Hitches = rows;
        }

        HitchesCaption = PerformanceFormat.HitchesCaption(hitches);
    }

    private void OnRunsChanged(object? sender, EventArgs e)
    {
        ApplyOperation(_monitor.Snapshot.Operation);
    }

    private void Apply(PerformanceSnapshot snapshot)
    {
        DelayText = PerformanceFormat.TileDelay(snapshot);
        DelayHint = PerformanceFormat.TileDelayHint(snapshot);
        IsHitch = snapshot.UiPeakMs >= AppDefaults.PerformanceHitchMs;

        MemoryText = PerformanceFormat.TileMemory(snapshot);
        MemoryHint = PerformanceFormat.TileMemoryHint(snapshot);
        MemoryPeakText = PerformanceFormat.TileMemoryPeak(snapshot);

        CollectionsText = PerformanceFormat.TileCollections(snapshot);
        CollectionsWindowText = PerformanceFormat.TileCollectionsWindow(snapshot);
        StartupText = PerformanceFormat.TileStartup(snapshot);
        StartupHint = PerformanceFormat.TileStartupHint(snapshot);

        RenderText = snapshot.RenderCount == 0 ? "нет кадров" : $"{snapshot.RenderLastMs:N1} мс";

        RenderHint = snapshot.RenderCount == 0
            ? "карту диска с начала сбора ни разу не рисовали"
            : $"пик {snapshot.RenderPeakMs:N1} мс · среднее {snapshot.RenderAverageMs:N1} мс · {Plural.Format(snapshot.RenderCount, "кадр", "кадра", "кадров")}";

        IsRenderSlow = snapshot.RenderPeakMs >= AppDefaults.PerformanceRenderSlowMs;
        WindowText = PerformanceFormat.TileWindow(snapshot);
        StaleText = PerformanceFormat.StaleWarning(snapshot, DateTime.UtcNow);

        ApplyOperation(snapshot.Operation);
    }

    private void ApplyOperation(PerformanceOperation? current)
    {
        var tile = PerformanceFormat.TileOperation(current, _runs.Last);

        OperationCaption = tile.Caption;
        OperationText = tile.Value;
        OperationVolume = tile.Volume;
        OperationRate = tile.Rate;
        OperationTraversal = tile.Traversal;
        OperationTraversalDetail = tile.TraversalDetail;
    }
}
