namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceChartViewModel : ObservableObject, ILogsPanel
{
    private readonly PerformanceMonitor _monitor;
    private readonly ISettingsStore _settings;
    private readonly IApplicationLifetime _lifetime;
    private readonly IUiTimer _timer;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private PerformanceChartData _data = PerformanceChartData.Empty;

    [ObservableProperty]
    private string _verdictText = string.Empty;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _windowText = string.Empty;

    [ObservableProperty]
    private string? _operationsText;

    [ObservableProperty]
    private double _chartHeight = AppDefaults.PerformanceChartPanelHeight;

    [ObservableProperty]
    private PerformanceChartWindow _window;

    public PerformanceChartViewModel(PerformanceMonitor monitor, ISettingsStore settings, IUiDispatcher uiDispatcher, IApplicationLifetime lifetime)
    {
        _monitor = monitor;
        _settings = settings;
        _lifetime = lifetime;

        _timer = uiDispatcher.CreateTimer(TimeSpan.FromMilliseconds(AppDefaults.PerformanceChartRefreshMs), OnTick);

        IsExpanded = _settings.GetBool(SettingsKeys.PerformanceChart, AppDefaults.PerformanceChartExpandedDefault);
        _window = RestoreWindow(settings);
    }

    public event EventHandler? Refreshed;

    public string Title => "График производительности";

    public static string ChartDescription =>
        "Верхнее поле – задержка UI-потока в миллисекундах: шкала идёт от нуля до круглого числа над пиком окна, "
        + $"поперечный пунктир – порог просадки {AppDefaults.PerformanceHitchMs} мс, кружком помечен каждый замер выше порога. "
        + "Нижнее поле – занятая управляемая память; её шкала подписана по краям делений, поэтому видно и величину, и размах. "
        + "Ось внизу отсчитывает время назад от «сейчас», разрыв линии означает пропущенные замеры. "
        + "Лента под осью – промежутки, когда шла операция: сканирование, сравнение, синхронизация. "
        + "Наведение показывает значения выбранного замера.";

    public void SetActive(bool active)
    {
        if (active)
        {
            Window = RestoreWindow(_settings);
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

    partial void OnWindowChanged(PerformanceChartWindow value)
    {
        _settings.SetEnum(SettingsKeys.PerformanceChartWindow, value);
        Refresh();
    }

    private static PerformanceChartWindow RestoreWindow(ISettingsStore settings)
    {
        var window = settings.GetEnum(SettingsKeys.PerformanceChartWindow, AppDefaults.PerformanceChartWindowDefault);

        return window == PerformanceChartWindow.None ? AppDefaults.PerformanceChartWindowDefault : window;
    }

    private void OnTick()
    {
        if (_lifetime.IsMainWindowMinimized)
        {
            return;
        }

        Refresh();
    }

    public void Refresh()
    {
        _monitor.Start();

        var data = PerformanceChartLayout.Build(_monitor.CaptureHistory(TimeSpan.FromSeconds((int)Window), AppDefaults.PerformanceHistoryPointsMax));

        Data = data;
        VerdictText = PerformanceFormat.ChartVerdict(data);
        MemoryText = PerformanceFormat.ChartMemory(data);
        WindowText = PerformanceFormat.ChartWindow(data);
        OperationsText = DescribeOperations(data);

        Refreshed?.Invoke(this, EventArgs.Empty);
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
