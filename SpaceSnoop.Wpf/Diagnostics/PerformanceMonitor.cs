using System.Diagnostics;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceMonitor(ILogger<PerformanceMonitor> logger) : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs);

    private readonly PerformanceSamples _delays = new(AppDefaults.PerformanceWindowSamples);

    private readonly int[] _baseCollections = new int[3];

    private DispatcherTimer? _timer;

    private long _lastTick;

    private long _lastHitchLog;

    private PerformanceOperation? _operation;

    private PerformanceSnapshot _snapshot = PerformanceSnapshot.Empty;

    private volatile bool _running;

    public event EventHandler? Updated;

    public PerformanceSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        for (var generation = 0; generation < _baseCollections.Length; generation++)
        {
            _baseCollections[generation] = GC.CollectionCount(generation);
        }

        _delays.Clear();
        _lastTick = Stopwatch.GetTimestamp();
        _lastHitchLog = 0;
        _running = true;

        Publish(0, Volatile.Read(ref _operation));

        _timer ??= CreateTimer();
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();
    }

    public void ReportOperation(PerformanceOperation? operation)
    {
        Volatile.Write(ref _operation, operation);
    }

    public void Dispose()
    {
        _running = false;

        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = SampleInterval,
        };

        timer.Tick += OnTick;

        return timer;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();
        var sinceLastTick = Stopwatch.GetElapsedTime(_lastTick, now);
        _lastTick = now;

        var delay = Math.Max(0, sinceLastTick.TotalMilliseconds - SampleInterval.TotalMilliseconds);
        _delays.Add(delay);

        var operation = Volatile.Read(ref _operation);

        Publish(delay, operation);
        LogHitch(delay, operation, now);

        Updated?.Invoke(this, EventArgs.Empty);
    }

    private void Publish(double delay, PerformanceOperation? operation)
    {
        Volatile.Write(ref _snapshot, new(delay,
            _delays.Peak(),
            _delays.Average(),
            GC.GetTotalMemory(false),
            Environment.WorkingSet,
            GC.CollectionCount(0) - _baseCollections[0],
            GC.CollectionCount(1) - _baseCollections[1],
            GC.CollectionCount(2) - _baseCollections[2],
            operation));
    }

    private void LogHitch(double delay, PerformanceOperation? operation, long now)
    {
        if (delay < AppDefaults.PerformanceHitchMs)
        {
            return;
        }

        if (_lastHitchLog != 0 && Stopwatch.GetElapsedTime(_lastHitchLog, now).TotalSeconds < AppDefaults.PerformanceHitchLogIntervalSeconds)
        {
            return;
        }

        _lastHitchLog = now;
        logger.PerformanceHitch((long)delay, operation?.Name ?? "нет операции");
    }
}
