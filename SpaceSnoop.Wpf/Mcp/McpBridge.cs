namespace SpaceSnoop.Wpf.Mcp;

public sealed class McpBridge
{
    private readonly McpNavigator _navigator = new();
    private readonly McpStateReader _state;

    public McpBridge(
        ISettingsStore settings,
        ISyncAutomation sync,
        IScanAutomation scan,
        McpPreferences preferences,
        ScanPreferences scanPreferences,
        DiskSpaceCalculator calculator,
        DockerService docker,
        CleanupService cleanup,
        ICleanupAutomation cleanupAutomation,
        ToastNotifier notifier,
        PerformanceMonitor performance,
        PerformanceRunTracker runs,
        CompareDirectoriesUseCase compare,
        ILogger<McpBridge> logger)
    {
        _state = new(scan, sync, _navigator);

        Scan = new(scan, scanPreferences, calculator, preferences, notifier, _navigator, _state, performance, runs, logger);
        Sync = new(sync, compare, preferences, notifier, _navigator, _state, logger);
        Insight = new(settings, preferences, docker, performance, _navigator, _state, logger);
        Cleanup = new(settings, cleanup, static age => CleanupCatalog.BuildDefault(age), preferences, cleanupAutomation, _navigator, notifier, logger);
        Capture = new(_navigator, logger);
    }

    public event Action<string>? NavigationDeferred
    {
        add => _navigator.NavigationDeferred += value;
        remove => _navigator.NavigationDeferred -= value;
    }

    public bool DeferNavigation
    {
        get => _navigator.DeferNavigation;
        set => _navigator.DeferNavigation = value;
    }

    internal McpScanTools Scan { get; }

    internal McpSyncTools Sync { get; }

    internal McpInsightTools Insight { get; }

    internal McpCleanupTools Cleanup { get; }

    internal McpCaptureTools Capture { get; }

    public void Attach(ShellViewModel shell)
    {
        _navigator.Attach(shell);
    }

    public string DescribeContext()
    {
        return _state.DescribeContext();
    }
}
