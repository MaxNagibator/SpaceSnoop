using System.Diagnostics;
using Timer = System.Threading.Timer;

namespace SpaceSnoop.Wpf.Diagnostics;

internal sealed class BackgroundScanProbe : IDisposable
{
    public const string OperationName = "Сканирование (агент)";

    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(AppDefaults.PerformanceSampleIntervalMs);

    private readonly PerformanceMonitor _performance;
    private readonly PerformanceRunTracker _runs;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly Lock _gate = new();
    private readonly long? _totalBytes;
    private readonly int _parallelism;
    private readonly Timer _timer;

    private PerformanceOperation? _reported;
    private bool _stopped;

    public BackgroundScanProbe(PerformanceMonitor performance, PerformanceRunTracker runs, long? totalBytes, int parallelism)
    {
        _performance = performance;
        _runs = runs;
        _totalBytes = totalBytes;
        _parallelism = Math.Max(1, parallelism);
        _timer = new(_ => Publish(), null, ReportInterval, ReportInterval);
    }

    public ScanProgress Progress { get; } = new();

    internal static PerformanceOperation Describe(ScanProgressSnapshot snapshot, TimeSpan elapsed, long? totalBytes, int parallelism)
    {
        return new(OperationName,
            snapshot.FilesScanned,
            snapshot.BytesScanned,
            elapsed,
            TotalBytes: totalBytes,
            Basis: totalBytes.HasValue ? EtaBasis.Bytes : EtaBasis.None,
            Traversal: new(snapshot.DirectoriesScanned, snapshot.DirectoriesFailed, Math.Max(1, parallelism)));
    }

    public PerformanceOperation Finish()
    {
        Stop();

        var run = Describe(Progress.CreateSnapshot(), _stopwatch.Elapsed, null, _parallelism);
        _runs.Report(run);

        return run;
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
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

            if (!_performance.TryReportOperation(operation, _reported))
            {
                return false;
            }

            _reported = operation;

            return true;
        }
    }

    private void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
            _stopwatch.Stop();
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _performance.ClearOperation(_reported);
            _reported = null;
        }
    }
}
