using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceViewModel : ObservableObject, IPageHeader, IPageRefresh
{
    private readonly PerformanceMonitor _monitor;

    private readonly ToastNotifier _notifier;

    private readonly ILogger<PerformanceViewModel> _logger;

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
    private string _collectionsText = string.Empty;

    [ObservableProperty]
    private string _startupText = string.Empty;

    [ObservableProperty]
    private string _windowText = string.Empty;

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
        ILogger<PerformanceViewModel> logger)
    {
        _monitor = monitor;
        _notifier = notifier;
        _logger = logger;

        Chart = chart;
        Chart.ChartHeight = AppDefaults.PerformanceChartPageHeight;
        Preferences = preferences;

        _monitor.Updated += OnMonitorUpdated;
        Apply(_monitor.Snapshot);
    }

    public PerformanceChartViewModel Chart { get; }

    public ShellPreferences Preferences { get; }

    public string PageTitle => "Производительность";

    public string? PageDescription =>
        $"Задержка UI-потока, память и сборки мусора. График держит последние {AppDefaults.PerformanceHistorySecondsMax / 60} мин, просадкой считается задержка от {AppDefaults.PerformanceHitchMs} мс.";

    public string? RefreshTooltip => "Перечитать замеры прямо сейчас";

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

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
    private void Refresh()
    {
        _monitor.Start();
        Apply(_monitor.Snapshot);
        Chart.Refresh();
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
        try
        {
            Clipboard.SetText(PerformanceReport.Build(_monitor.Snapshot, AppInfo.Version));
            _notifier.Notify("Сводка скопирована в буфер обмена");
        }
        catch (Exception exception)
        {
            _logger.PerformanceSummaryCopyFailed(exception);
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

    private void Apply(PerformanceSnapshot snapshot)
    {
        DelayText = $"{Math.Round(snapshot.UiDelayMs):N0} мс";
        DelayHint = $"пик {Math.Round(snapshot.UiPeakMs):N0} мс · среднее {Math.Round(snapshot.UiAverageMs):N0} мс";
        IsHitch = snapshot.UiPeakMs >= AppDefaults.PerformanceHitchMs;

        MemoryText = SizeFormatter.Format(snapshot.ManagedBytes);
        MemoryHint = $"процесс {SizeFormatter.Format(snapshot.WorkingSetBytes)}";

        CollectionsText = $"{snapshot.Gen0Collections} / {snapshot.Gen1Collections} / {snapshot.Gen2Collections}";
        StartupText = $"{snapshot.StartupSeconds:N2} с";
        WindowText = $"{Plural.Format(snapshot.SampleCount, "замер", "замера", "замеров")} за {snapshot.ObservedSpanSeconds:N1} с";

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
