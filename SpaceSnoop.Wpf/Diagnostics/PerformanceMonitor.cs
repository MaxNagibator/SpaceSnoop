using System.Diagnostics;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceMonitor(ILogger<PerformanceMonitor> logger) : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs);

    private readonly PerformanceSamples _delays = new(AppDefaults.PerformanceWindowSamples);

    private readonly PerformanceHistoryBuffer _history = new(AppDefaults.PerformanceHistorySamples);

    private readonly Lock _lock = new();

    private readonly int[] _baseCollections = new int[3];

    private DispatcherTimer? _timer;

    private long _lastTick;

    private long _lastHitchLog;

    private PerformanceOperation? _operation;

    private TimeSpan _startup;

    private PerformanceSnapshot _snapshot = PerformanceSnapshot.Empty;

    private volatile bool _running;

    private volatile bool _disposed;

    public event EventHandler? Updated;

    public PerformanceSnapshot Snapshot => Volatile.Read(ref _snapshot);

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running || _disposed)
        {
            return;
        }

        for (var generation = 0; generation < _baseCollections.Length; generation++)
        {
            _baseCollections[generation] = GC.CollectionCount(generation);
        }

        var operation = Volatile.Read(ref _operation);

        lock (_lock)
        {
            _delays.Clear();
            _history.Clear();

            _lastTick = Stopwatch.GetTimestamp();
            _lastHitchLog = 0;
            _running = true;

            Publish(Sample(_lastTick, 0, operation), operation);
        }

        _timer ??= CreateTimer();
        _timer.Start();
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();

        Volatile.Write(ref _operation, null);
        Volatile.Write(ref _snapshot, PerformanceSnapshot.Empty with { StartupSeconds = _startup.TotalSeconds });
    }

    public void ReportStartup(TimeSpan elapsed)
    {
        _startup = elapsed;
    }

    public void ReportOperation(PerformanceOperation? operation)
    {
        Volatile.Write(ref _operation, operation);
    }

    public PerformanceHistory CaptureHistory(TimeSpan since, int maxPoints)
    {
        lock (_lock)
        {
            return _history.Capture(Stopwatch.GetTimestamp(), DateTime.UtcNow, since, maxPoints);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _running = false;
        Updated = null;

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
        var operation = Volatile.Read(ref _operation);
        double delay;

        lock (_lock)
        {
            delay = Math.Max(0, Stopwatch.GetElapsedTime(_lastTick, now).TotalMilliseconds - SampleInterval.TotalMilliseconds);
            _lastTick = now;
            _delays.Add(delay);

            var sample = Sample(now, delay, operation);
            _history.Add(sample);
            Publish(sample, operation);
        }

        LogHitch(delay, operation, now);
        Notify();
    }

    private void Notify()
    {
        try
        {
            Updated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            logger.PerformanceListenerFailed(exception);
        }
    }

    private PerformanceSample Sample(long now, double delay, PerformanceOperation? operation)
    {
        return new(now,
            delay,
            GC.GetTotalMemory(false),
            Environment.WorkingSet,
            GC.CollectionCount(0) - _baseCollections[0],
            GC.CollectionCount(1) - _baseCollections[1],
            GC.CollectionCount(2) - _baseCollections[2],
            operation?.Name);
    }

    private void Publish(PerformanceSample sample, PerformanceOperation? operation)
    {
        Volatile.Write(ref _snapshot, new(DateTime.UtcNow,
            sample.UiDelayMs,
            _delays.Peak(),
            _delays.Average(),
            _delays.Count,
            _delays.SpanSeconds(SampleInterval.TotalMilliseconds),
            sample.ManagedBytes,
            sample.WorkingSetBytes,
            sample.Gen0Collections,
            sample.Gen1Collections,
            sample.Gen2Collections,
            _startup.TotalSeconds,
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
