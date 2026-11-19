using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SpaceSnoop.Wpf.Diagnostics;

internal sealed class BackgroundScanProbe : IDisposable
{
    public const string OperationName = "Сканирование (агент)";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs);

    private readonly PerformanceOperations _operations;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly long? _totalBytes;
    private readonly int _parallelism;
    private readonly Timer _timer;

    private PerformanceOperation? _reported;
    private bool _stopped;
    private bool _disposed;

    public ScanProgress Progress { get; } = new();

    public BackgroundScanProbe(PerformanceOperations operations, long? totalBytes, int parallelism)
    {
        _operations = operations;
        _totalBytes = totalBytes;
        _parallelism = Math.Max(1, parallelism);
        _timer = new(_ => Publish(), null, ReportInterval, ReportInterval);
        Publish();
    }

    public PerformanceOperation Finish(bool releaseMeasurement = true, ScanNotes? notes = null)
    {
        Stop(releaseMeasurement);

        var parallelism = notes is { Parallelism: > 0 } applied ? applied.Parallelism : _parallelism;
        var run = Describe(Progress.CreateSnapshot(), _stopwatch.Elapsed, null, parallelism, notes);
        _operations.ReportRun(run);

        return run;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            Stop();
            _timer.Dispose();
            _disposed = true;
        }
    }

    public void ReportPhase(string name)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var phase = Describe(Progress.CreateSnapshot(), _stopwatch.Elapsed, null, _parallelism) with
            {
                Name = name,
            };

            if (_operations.TryReport(phase, _reported))
            {
                _reported = phase;
            }
        }
    }

    public Task CompleteMeasurementAsync()
    {
        lock (_gate)
        {
            Stop(releaseMeasurement: false);
            var reported = _reported;
            _reported = null;
            return _operations.ReleaseAfterRenderAsync(reported);
        }
    }

    internal static PerformanceOperation Describe(
        ScanProgressSnapshot snapshot,
        TimeSpan elapsed,
        long? totalBytes,
        int parallelism,
        ScanNotes? notes = null)
    {
        return new(
            OperationName,
            snapshot.FilesScanned,
            snapshot.BytesScanned,
            elapsed,
            TotalBytes: totalBytes,
            Basis: totalBytes.HasValue ? EtaBasis.Bytes : EtaBasis.None,
            Traversal: new(snapshot.DirectoriesScanned, snapshot.DirectoriesFailed, Math.Max(1, parallelism)),
            LogicalBytes: true,
            Notes: notes);
    }

    internal bool Publish()
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return false;
            }

            var operation = Describe(Progress.CreateSnapshot(), _stopwatch.Elapsed, _totalBytes, _parallelism);

            if (!_operations.TryReport(operation, _reported))
            {
                return false;
            }

            _reported = operation;

            return true;
        }
    }

    private void Stop(bool releaseMeasurement = true)
    {
        lock (_gate)
        {
            _stopped = true;
            _stopwatch.Stop();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (releaseMeasurement)
            {
                _operations.Release(_reported);
                _reported = null;
            }
        }
    }
}
