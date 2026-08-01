using System.Windows.Threading;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceChartViewModel : ObservableObject, ILogsPanel
{
    private readonly PerformanceMonitor _monitor;
    private readonly ISettingsStore _settings;
    private readonly DispatcherTimer _timer;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private PerformanceChartData _data = PerformanceChartData.Empty;

    [ObservableProperty]
    private string _delayText = string.Empty;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _windowText = string.Empty;

    [ObservableProperty]
    private string? _operationsText;

    public PerformanceChartViewModel(PerformanceMonitor monitor, ISettingsStore settings)
    {
        _monitor = monitor;
        _settings = settings;

        _timer = new(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceChartRefreshMs),
        };

        _timer.Tick += OnTick;

        IsExpanded = _settings.GetBool(SettingsKeys.PerformanceChart, AppDefaults.PerformanceChartExpandedDefault);
    }

    public string Title => "График производительности";

    public void SetActive(bool active)
    {
        if (active)
        {
            Refresh();
            _timer.Start();

            return;
        }

        _timer.Stop();
    }

    partial void OnIsExpandedChanged(bool value)
    {
        _settings.SetBool(SettingsKeys.PerformanceChart, value);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        _monitor.Start();

        var data = PerformanceChartLayout.Build(_monitor.CaptureHistory(TimeSpan.Zero, AppDefaults.PerformanceHistorySamples));

        Data = data;
        DelayText = PerformanceFormat.ChartDelay(data);
        MemoryText = PerformanceFormat.ChartMemory(data);
        WindowText = PerformanceFormat.ChartWindow(data);
        OperationsText = DescribeOperations(data);
    }

    private static string? DescribeOperations(PerformanceChartData data)
    {
        var names = data.Bands
            .Select(static band => band.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return names.Count > 0 ? $"В окне работали: {string.Join(", ", names)}" : null;
    }
}
