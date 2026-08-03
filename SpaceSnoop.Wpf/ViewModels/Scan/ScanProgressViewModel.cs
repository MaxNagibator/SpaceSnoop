using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanProgressViewModel : ObservableObject
{
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(120);

    private readonly PerformanceMonitor _performance;
    private readonly IUiTimer _progressTimer;

    private ScanProgress? _progress;
    private Stopwatch? _scanStopwatch;
    private double? _progressFraction;
    private long? _estimatedTotalBytes;
    private int _parallelism = 1;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanCurrentPath = string.Empty;

    [ObservableProperty]
    private string _scanDirCountText = "0";

    [ObservableProperty]
    private string _scanFileCountText = "0";

    [ObservableProperty]
    private string _scanBytesText = "0 байт";

    [ObservableProperty]
    private string _scanElapsedText = "0,0 с";

    [ObservableProperty]
    private string _scanThroughputText = "–";

    [ObservableProperty]
    private string _scanRemainingText = string.Empty;

    [ObservableProperty]
    private bool _scanHasRemaining;

    [ObservableProperty]
    private string _scanTopLevelText = string.Empty;

    [ObservableProperty]
    private bool _scanHasBranches;

    [ObservableProperty]
    private bool _scanHasDeterminateProgress;

    [ObservableProperty]
    private string _scanPercentText = string.Empty;

    public ScanProgressViewModel(PerformanceMonitor performance, IUiDispatcher uiDispatcher)
    {
        _performance = performance;

        _progressTimer = uiDispatcher.CreateTimer(ProgressPollInterval, OnProgressTick);
    }

    public PerformanceTraversal? Traversal { get; private set; }

    public bool IsIndeterminate => !_progressFraction.HasValue;

    public double ProgressValue => _progressFraction ?? 0;

    public double ProgressMax => 1;

    internal static long? EstimateTotalBytes(DirectoryInfo directory)
    {
        try
        {
            var full = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = directory.Root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var drive = new DriveInfo(directory.Root.FullName);

            if (!drive.IsReady)
            {
                return null;
            }

            var used = drive.TotalSize - drive.TotalFreeSpace;
            return used > 0 ? used : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal ScanProgress Begin(DirectoryInfo directory, string path, int parallelism)
    {
        _estimatedTotalBytes = EstimateTotalBytes(directory);
        _parallelism = Math.Max(1, parallelism);
        _progress = new();
        _scanStopwatch = Stopwatch.StartNew();
        Traversal = null;

        ResetLiveProgress(path);
        _progressTimer.Start();

        return _progress;
    }

    internal TimeSpan Finish()
    {
        _progressTimer.Stop();
        _scanStopwatch?.Stop();
        _performance.ReportOperation(null);

        if (_progress is { } progress)
        {
            Traversal = Describe(progress.CreateSnapshot());
        }

        _progress = null;
        _progressFraction = null;
        _estimatedTotalBytes = null;

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));

        return _scanStopwatch?.Elapsed ?? TimeSpan.Zero;
    }

    private PerformanceTraversal Describe(ScanProgressSnapshot snapshot)
    {
        return new(snapshot.DirectoriesScanned, snapshot.DirectoriesFailed, _parallelism);
    }

    private void OnProgressTick()
    {
        UpdateLiveProgress();
    }

    private void ResetLiveProgress(string path)
    {
        _progressFraction = null;
        ScanCurrentPath = path;
        ScanDirCountText = "0";
        ScanFileCountText = "0";
        ScanBytesText = SizeFormatter.Format(0);
        ScanElapsedText = PerformanceFormat.Elapsed(TimeSpan.Zero);
        ScanThroughputText = "–";
        ScanRemainingText = string.Empty;
        ScanHasRemaining = false;
        ScanTopLevelText = string.Empty;
        ScanPercentText = string.Empty;
        ScanHasBranches = false;
        ScanHasDeterminateProgress = false;

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
    }

    private void UpdateLiveProgress()
    {
        if (_progress is null)
        {
            return;
        }

        var snapshot = _progress.CreateSnapshot();
        var elapsed = _scanStopwatch?.Elapsed ?? TimeSpan.Zero;

        ScanCurrentPath = string.IsNullOrEmpty(snapshot.CurrentPath) ? ScanCurrentPath : snapshot.CurrentPath;
        ScanDirCountText = snapshot.DirectoriesScanned.ToString("N0");
        ScanFileCountText = snapshot.FilesScanned.ToString("N0");
        ScanBytesText = SizeFormatter.Format(snapshot.BytesScanned);
        ScanElapsedText = PerformanceFormat.Elapsed(elapsed);

        ScanHasBranches = snapshot.TopLevelTotal > 0;
        ScanTopLevelText = ScanHasBranches
            ? $"{snapshot.TopLevelCompleted:N0} / {snapshot.TopLevelTotal:N0}"
            : string.Empty;

        double? fraction = _estimatedTotalBytes is > 0
            ? Math.Clamp((double)snapshot.BytesScanned / _estimatedTotalBytes.Value, 0d, 1d)
            : null;

        _progressFraction = fraction;
        ScanHasDeterminateProgress = fraction.HasValue;
        ScanPercentText = fraction.HasValue ? $"{fraction.Value * 100:F0} %" : string.Empty;

        var operation = new PerformanceOperation("Сканирование",
            snapshot.FilesScanned,
            snapshot.BytesScanned,
            elapsed,
            TotalBytes: _estimatedTotalBytes,
            Basis: EtaBasis.Bytes,
            Traversal: Describe(snapshot));

        ScanThroughputText = PerformanceFormat.Rate(operation) ?? "–";
        ScanRemainingText = PerformanceFormat.Remaining(operation) ?? string.Empty;
        ScanHasRemaining = ScanRemainingText.Length > 0;

        _performance.ReportOperation(operation);

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
    }
}
