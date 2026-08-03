namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceViewModel : ObservableObject, IPageHeader
{
    private readonly PerformanceMonitor _monitor;

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
    private string? _operationText;

    [ObservableProperty]
    private string? _operationHint;

    [ObservableProperty]
    private bool _hasOperation;

    public PerformanceViewModel(
        PerformanceMonitor monitor,
        PerformanceChartViewModel chart,
        ShellPreferences preferences,
        ToastNotifier notifier,
        IClipboardService clipboard)
    {
        _monitor = monitor;
        _notifier = notifier;
        _clipboard = clipboard;

        Chart = chart;
        Chart.ChartHeight = AppDefaults.PerformanceChartPageHeight;
        Chart.Refreshed += OnChartRefreshed;
        Preferences = preferences;

        _monitor.Updated += OnMonitorUpdated;
        Apply(_monitor.Snapshot);
    }

    public PerformanceChartViewModel Chart { get; }

    public ShellPreferences Preferences { get; }

    public string PageTitle => "Производительность";

    public string? PageDescription =>
        $"Задержка UI-потока, память и сборки мусора. График держит последние {AppDefaults.PerformanceHistorySecondsMax / 60} мин, просадкой считается задержка от {AppDefaults.PerformanceHitchMs} мс.";

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
        Apply(_monitor.Snapshot);
        Chart.Refresh();
        _notifier.Notify("Замеры производительности сброшены");
    }

    [RelayCommand]
    private void CopySummary()
    {
        if (_clipboard.TrySetText(PerformanceReport.Build(_monitor.Snapshot, AppInfo.Version)))
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

        OperationText = snapshot.Operation?.Name;
        OperationHint = DescribeOperation(snapshot.Operation);
        HasOperation = snapshot.Operation is not null;
    }

    private static string? DescribeOperation(PerformanceOperation? operation)
    {
        if (operation is null)
        {
            return null;
        }

        var parts = new List<string>(2);

        if (PerformanceFormat.Rate(operation) is { } rate)
        {
            parts.Add(rate);
        }

        if (PerformanceFormat.Remaining(operation) is { } remaining)
        {
            parts.Add($"осталось {remaining}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }
}
