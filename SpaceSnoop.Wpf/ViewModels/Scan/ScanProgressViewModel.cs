using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanProgressViewModel : ObservableObject
{
    internal const string MeasuringText = "считаем…";

    private readonly PerformanceOperations _operations;
    private readonly IUiTimer _progressTimer;
    private readonly Lock _estimateGate = new();

    private ScanProgress? _progress;
    private PerformanceOperation? _reported;
    private long? _scanStarted;
    private TimeSpan _scanElapsed;
    private double? _progressFraction;
    private long? _estimatedTotalBytes;
    private int _parallelism = 1;
    private int _runId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanCurrentPath = string.Empty;

    [ObservableProperty]
    private string _scanStageText = string.Empty;

    [ObservableProperty]
    private bool _scanShowsStage;

    [ObservableProperty]
    private string _scanDirCountText = "0";

    [ObservableProperty]
    private string _scanFileCountText = "0";

    [ObservableProperty]
    private string _scanBytesText = MeasuringText;

    [ObservableProperty]
    private string _scanElapsedText = PerformanceText.Elapsed(TimeSpan.Zero);

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

    public PerformanceTraversal? Traversal { get; private set; }

    public bool IsIndeterminate => (IsScanning || _progress is not null) && !_progressFraction.HasValue;

    public double ProgressValue => _progressFraction ?? 0;

    public double ProgressMax => 1;

    internal static Func<string, long?> VolumeUsedReader { get; set; } = ReadVolumeUsedBytes;

    internal Task PendingEstimate { get; private set; } = Task.CompletedTask;

    public ScanProgressViewModel(PerformanceOperations operations, IUiDispatcher uiDispatcher)
    {
        _operations = operations;

        _progressTimer = uiDispatcher.CreateTimer(UiProgressPoller.DefaultInterval, OnProgressTick);
    }

    internal static string BytesText(long files, long bytes)
    {
        return files == 0 && bytes == 0 ? MeasuringText : SizeFormatter.Format(bytes);
    }

    internal static long? EstimateTotalBytes(DirectoryInfo directory)
    {
        return TryGetVolumeRoot(directory, out var root) ? VolumeUsedReader(root) : null;
    }

    internal ScanProgress Begin(DirectoryInfo directory, string path, int parallelism, long? estimatedTotalBytes = null)
    {
        int runId;

        lock (_estimateGate)
        {
            runId = ++_runId;
            _estimatedTotalBytes = estimatedTotalBytes;
        }

        _parallelism = Math.Max(1, parallelism);
        _progress = new();
        _scanStarted = Stopwatch.GetTimestamp();
        _scanElapsed = TimeSpan.Zero;
        Traversal = null;

        ResetLiveProgress(path);
        _progressTimer.Start();
        UpdateLiveProgress();

        if (estimatedTotalBytes is null && TryGetVolumeRoot(directory, out var volumeRoot))
        {
            var reader = VolumeUsedReader;
            PendingEstimate = Task.Run(() => ProbeTotalBytes(runId, volumeRoot, reader));
        }
        else
        {
            PendingEstimate = Task.CompletedTask;
        }

        return _progress;
    }

    internal void ShiftStartForAutomation(TimeSpan elapsed)
    {
        if (_scanStarted is { } started)
        {
            _scanStarted = started - (long)(elapsed.TotalSeconds * Stopwatch.Frequency);
        }
    }

    internal TimeSpan Finish(bool releaseMeasurement = true)
    {
        _progressTimer.Stop();
        _scanElapsed = Elapsed();
        _scanStarted = null;
        if (releaseMeasurement)
        {
            _operations.Release(_reported);
            _reported = null;
        }

        if (_progress is { } progress)
        {
            Traversal = Describe(progress.CreateSnapshot());
        }

        _progress = null;
        _progressFraction = null;

        lock (_estimateGate)
        {
            _runId++;
            _estimatedTotalBytes = null;
        }

        PendingEstimate = Task.CompletedTask;

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));

        return _scanElapsed;
    }

    internal void ReportPhase(string name)
    {
        var phase = _reported is { } reported
            ? reported with
            {
                Name = name,
            }
            : new(name, 0, 0, TimeSpan.Zero);

        if (_operations.TryReport(phase, _reported))
        {
            _reported = phase;
        }
    }

    internal Task CompleteMeasurementAsync()
    {
        var reported = _reported;
        _reported = null;
        return _operations.ReleaseAfterRenderAsync(reported);
    }

    private static bool TryGetVolumeRoot(DirectoryInfo directory, out string volumeRoot)
    {
        volumeRoot = string.Empty;

        try
        {
            var full = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = directory.Root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase)
                || root.Length != 2
                || !char.IsAsciiLetter(root[0])
                || root[1] != ':')
            {
                return false;
            }

            volumeRoot = directory.Root.FullName;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static long? ReadVolumeUsedBytes(string volumeRoot)
    {
        try
        {
            var drive = new DriveInfo(volumeRoot);

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

    private void ProbeTotalBytes(int runId, string volumeRoot, Func<string, long?> reader)
    {
        var used = reader(volumeRoot);

        lock (_estimateGate)
        {
            if (runId == _runId && _progress is not null)
            {
                _estimatedTotalBytes = used;
            }
        }
    }

    private TimeSpan Elapsed()
    {
        return _scanStarted is { } started ? Stopwatch.GetElapsedTime(started) : _scanElapsed;
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
        ScanStageText = string.Empty;
        ScanShowsStage = false;
        ScanDirCountText = "0";
        ScanFileCountText = "0";
        ScanBytesText = MeasuringText;
        ScanElapsedText = PerformanceText.Elapsed(TimeSpan.Zero);
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
        var elapsed = Elapsed();

        if (!string.IsNullOrEmpty(snapshot.CurrentPath))
        {
            if (snapshot.IsStage)
            {
                ScanStageText = snapshot.CurrentPath;
            }
            else
            {
                ScanCurrentPath = snapshot.CurrentPath;
            }
        }

        ScanShowsStage = snapshot.IsStage;

        ScanDirCountText = snapshot.DirectoriesScanned.ToString("N0");
        ScanFileCountText = snapshot.FilesScanned.ToString("N0");
        ScanBytesText = BytesText(snapshot.FilesScanned, snapshot.BytesScanned);
        ScanElapsedText = PerformanceText.Elapsed(elapsed);

        ScanHasBranches = snapshot.TopLevelTotal > 0;
        ScanTopLevelText = ScanHasBranches
            ? $"{snapshot.TopLevelCompleted:N0} из {snapshot.TopLevelTotal:N0}"
            : string.Empty;

        long? estimatedTotalBytes;

        lock (_estimateGate)
        {
            estimatedTotalBytes = _estimatedTotalBytes;
        }

        double? fraction = estimatedTotalBytes is > 0
            ? Math.Clamp((double)snapshot.BytesScanned / estimatedTotalBytes.Value, 0d, 1d)
            : null;

        var fractionChanged = !EqualityComparer<double?>.Default.Equals(_progressFraction, fraction);
        _progressFraction = fraction;
        ScanHasDeterminateProgress = fraction.HasValue;
        ScanPercentText = fraction.HasValue ? $"{fraction.Value * 100:F0} %" : string.Empty;

        var operation = new PerformanceOperation(
            "Сканирование",
            snapshot.FilesScanned,
            snapshot.BytesScanned,
            elapsed,
            TotalBytes: estimatedTotalBytes,
            Basis: EtaBasis.Bytes,
            Traversal: Describe(snapshot),
            LogicalBytes: true);

        ScanThroughputText = PerformanceText.Rate(operation) ?? "–";
        ScanRemainingText = PerformanceText.Remaining(operation) ?? string.Empty;
        ScanHasRemaining = ScanRemainingText.Length > 0;

        if (_operations.TryReport(operation, _reported))
        {
            _reported = operation;
        }

        if (fractionChanged)
        {
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(ProgressValue));
        }
    }
}
