namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanSummaryViewModel : ObservableObject
{
    private const string NoValue = "–";

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _resultPath = string.Empty;

    [ObservableProperty]
    private string _resultSizeText = NoValue;

    [ObservableProperty]
    private string _resultFileCountText = NoValue;

    [ObservableProperty]
    private string _resultDirCountText = NoValue;

    [ObservableProperty]
    private string _resultElapsedText = NoValue;

    [ObservableProperty]
    private string _resultRateText = NoValue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVolumeNote))]
    [NotifyPropertyChangedFor(nameof(VolumeNoteHint))]
    private string _volumeNote = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLinkNote))]
    [NotifyPropertyChangedFor(nameof(LinkNoteHint))]
    private string _linkNote = string.Empty;

    private DriveCapacity? _drive;
    private long _extraNameBytes;

    public bool HasVolumeNote => VolumeNote.Length > 0;

    public string VolumeNoteHint => HasVolumeNote ? ScanVolumeNote.Explain(VolumeNote) : string.Empty;

    public bool HasLinkNote => LinkNote.Length > 0;

    public string LinkNoteHint => HasLinkNote ? ScanLinkNote.Explain(LinkNote) : string.Empty;

    public PerformanceOperation Apply(
        DirectorySpace result,
        TimeSpan elapsed,
        PerformanceTraversal? traversal = null,
        DriveCapacity? drive = null,
        long extraNameBytes = 0)
    {
        ResultPath = result.AbsolutePath;
        _drive = drive;
        _extraNameBytes = extraNameBytes;
        Refresh(result);
        ResultElapsedText = PerformanceFormat.Elapsed(elapsed);

        var run = new PerformanceOperation("Сканирование", result.TotalFileCount, result.TotalSize, elapsed, Traversal: traversal, LogicalBytes: true);
        ResultRateText = PerformanceFormat.Rate(run) ?? NoValue;

        return run;
    }

    public void Refresh(DirectorySpace result)
    {
        ResultSizeText = result.TotalSizeText;
        ResultFileCountText = result.TotalFileCount.ToString("N0");
        ResultDirCountText = result.TotalDirectoryCount.ToString("N0");
        VolumeNote = ScanVolumeNote.Describe(result.AbsolutePath, result.TotalSize, _drive) ?? string.Empty;
        LinkNote = ScanLinkNote.Describe(_extraNameBytes) ?? string.Empty;
    }
}
