using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceMonitor(ILogger<PerformanceMonitor> logger) : IDisposable
{
    private static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs);

    private readonly PerformanceSamples _delays = new(AppDefaults.PerformanceWindowSamples);

    private readonly PerformanceFrames _frames = new(AppDefaults.PerformanceFrameSlowMs, AppDefaults.PerformanceFrameGapMs);

    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    private readonly PerformanceHistoryBuffer _history = new(AppDefaults.PerformanceHistorySamples);

    private readonly Lock _lock = new();

    private readonly int[] _baseCollections = new int[3];

    private DispatcherTimer? _timer;

    private long _lastTick;

    private long _lastHitchLog;

    private int _frameLeases;

    private volatile bool _frameHooked;

    private long _workingSetPeak;

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

        Rebase(markRunning: true);

        _timer ??= CreateTimer();
        _timer.Start();

        SyncFrameProbe();
    }

    public void Reset()
    {
        if (_disposed)
        {
            return;
        }

        if (!_running)
        {
            Start();
            return;
        }

        Rebase(markRunning: false);
        Notify();
    }

    private void Rebase(bool markRunning)
    {
        for (var generation = 0; generation < _baseCollections.Length; generation++)
        {
            _baseCollections[generation] = GC.CollectionCount(generation);
        }

        var operation = Volatile.Read(ref _operation);

        lock (_lock)
        {
            _delays.Clear();
            _history.Clear();
            _frames.Clear();
            _workingSetPeak = 0;

            _lastTick = Stopwatch.GetTimestamp();
            _lastHitchLog = 0;

            if (markRunning)
            {
                _running = true;
            }

            Publish(Sample(_lastTick, 0, operation), operation);
        }
    }

    public void Stop()
    {
        _running = false;
        _timer?.Stop();

        Volatile.Write(ref _operation, null);
        Volatile.Write(ref _snapshot, PerformanceSnapshot.Empty with { StartupSeconds = _startup.TotalSeconds });

        SyncFrameProbe();
    }

    public void ReportStartup(TimeSpan elapsed)
    {
        _startup = elapsed;
    }

    public bool TryReportOperation(PerformanceOperation operation, PerformanceOperation? own)
    {
        var current = Volatile.Read(ref _operation);

        if (current is not null && !ReferenceEquals(current, own))
        {
            return false;
        }

        if (!ReferenceEquals(Interlocked.CompareExchange(ref _operation, operation, current), current))
        {
            return false;
        }

        SyncFrameProbe();

        return true;
    }

    public void ClearOperation(PerformanceOperation? own)
    {
        if (own is null || !ReferenceEquals(Interlocked.CompareExchange(ref _operation, null, own), own))
        {
            return;
        }

        SyncFrameProbe();
    }

    public IDisposable WatchFrames()
    {
        Interlocked.Increment(ref _frameLeases);
        SyncFrameProbe();

        return new FrameLease(this);
    }

    public PerformanceHistory CaptureHistory(TimeSpan since, int maxPoints)
    {
        lock (_lock)
        {
            return _history.Capture(Stopwatch.GetTimestamp(), DateTime.UtcNow, since, maxPoints);
        }
    }

    public PerformanceHitches CaptureHitches(double thresholdMs, int maxRows)
    {
        lock (_lock)
        {
            return _history.Hitches(Stopwatch.GetTimestamp(), DateTime.UtcNow, thresholdMs, maxRows);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _running = false;
        Updated = null;

        SyncFrameProbe();

        if (_timer is null)
        {
            return;
        }

        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void ReleaseFrames()
    {
        Interlocked.Decrement(ref _frameLeases);
        SyncFrameProbe();
    }

    private bool FrameProbeWanted()
    {
        return _running && !_disposed && (Volatile.Read(ref _frameLeases) > 0 || Volatile.Read(ref _operation) is not null);
    }

    private void SyncFrameProbe()
    {
        if (FrameProbeWanted() == _frameHooked)
        {
            return;
        }

        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(SyncFrameProbe);
            return;
        }

        var wanted = FrameProbeWanted();

        if (wanted == _frameHooked)
        {
            return;
        }

        _frameHooked = wanted;

        if (wanted)
        {
            CompositionTarget.Rendering += OnFrame;
            return;
        }

        CompositionTarget.Rendering -= OnFrame;

        lock (_lock)
        {
            _frames.Pause();
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = Stopwatch.GetTimestamp();

        lock (_lock)
        {
            _frames.Mark(now);
        }
    }

    private DispatcherTimer CreateTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
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
        _workingSetPeak = Math.Max(_workingSetPeak, sample.WorkingSetBytes);

        Volatile.Write(ref _snapshot, new(DateTime.UtcNow,
            sample.UiDelayMs,
            _delays.Peak(),
            _delays.Average(),
            _delays.Count,
            _delays.SpanSeconds(SampleInterval.TotalMilliseconds),
            sample.ManagedBytes,
            sample.WorkingSetBytes,
            _workingSetPeak,
            sample.Gen0Collections,
            sample.Gen1Collections,
            sample.Gen2Collections,
            _history.Stats(),
            _startup.TotalSeconds,
            _frames.LastMs,
            _frames.PeakMs,
            _frames.AverageMs,
            _frames.Count,
            _frames.SlowCount,
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

    private sealed class FrameLease(PerformanceMonitor monitor) : IDisposable
    {
        private PerformanceMonitor? _monitor = monitor;

        public void Dispose()
        {
            Interlocked.Exchange(ref _monitor, null)?.ReleaseFrames();
        }
    }
}
