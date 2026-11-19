namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanSummaryViewModel : ObservableObject
{
    private const string NoValue = "–";

    private readonly bool _isElevated;
    private readonly Func<Task>? _restartAsAdmin;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnCaption))]
    private string _resultPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReturnCaption))]
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDropNote))]
    [NotifyPropertyChangedFor(nameof(DropNoteHint))]
    private string _dropNote = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnreadNote))]
    [NotifyPropertyChangedFor(nameof(CanRestartAsAdmin))]
    private string _unreadNote = string.Empty;

    private DriveCapacity? _drive;
    private ScanNotes _notes;
    private long _unreadDirectories;

    public string ReturnCaption => ResultPath.Length > 0
        ? $"К результатам: {DriveItem.ShortName(ResultPath)} · {ResultSizeText}"
        : "К результатам";

    public bool HasUnreadNote => UnreadNote.Length > 0;

    public string UnreadNoteHint => ScanUnreadNote.Explain(_isElevated);

    public bool CanRestartAsAdmin => HasUnreadNote && !_isElevated && _restartAsAdmin is not null;

    public string RestartAsAdminHint => ScanUnreadNote.RestartHint;

    public bool HasVolumeNote => VolumeNote.Length > 0;

    public string VolumeNoteHint => HasVolumeNote ? ScanVolumeNote.Explain(VolumeNote) : string.Empty;

    public bool HasLinkNote => LinkNote.Length > 0;

    public string LinkNoteHint => HasLinkNote ? ScanLinkNote.Explain(LinkNote) : string.Empty;

    public bool HasDropNote => DropNote.Length > 0;

    public string DropNoteHint => HasDropNote
        ? ScanDropNote.Explain(_notes.DroppedObjects, _notes.DroppedBytes, _notes.UnknownSizeFiles, _notes.PartialRecords)
        : string.Empty;

    public ScanSummaryViewModel(bool isElevated = false, Func<Task>? restartAsAdmin = null)
    {
        _isElevated = isElevated;
        _restartAsAdmin = restartAsAdmin;
    }

    public PerformanceOperation Apply(
        DirectorySpace result,
        TimeSpan elapsed,
        PerformanceTraversal? traversal = null,
        DriveCapacity? drive = null,
        ScanNotes notes = default)
    {
        ResultPath = result.AbsolutePath;
        _drive = drive;
        _notes = notes;
        _unreadDirectories = traversal?.FailedDirectories ?? 0;
        Refresh(result);
        ResultElapsedText = PerformanceText.Elapsed(elapsed);

        if (traversal is not null && notes.Parallelism > 0)
        {
            traversal = traversal with
            {
                Parallelism = notes.Parallelism,
            };
        }

        ScanNotes? operationNotes = notes.Parallelism > 0 ? notes : null;
        var run = new PerformanceOperation(
            "Сканирование",
            result.TotalFileCount,
            result.TotalSize,
            elapsed,
            Traversal: traversal,
            LogicalBytes: true,
            Notes: operationNotes);

        ResultRateText = PerformanceText.Rate(run) ?? NoValue;

        return run;
    }

    public void Refresh(DirectorySpace result)
    {
        ResultSizeText = result.TotalSizeText;
        ResultFileCountText = result.TotalFileCount.ToString("N0");
        ResultDirCountText = result.TotalDirectoryCount.ToString("N0");
        VolumeNote = ScanVolumeNote.Describe(result.AbsolutePath, result.TotalSize, _drive) ?? string.Empty;
        LinkNote = ScanLinkNote.Describe(_notes.ExtraNameBytes) ?? string.Empty;
        DropNote = ScanDropNote.Describe(_notes.DroppedObjects, _notes.UnknownSizeFiles, _notes.PartialRecords) ?? string.Empty;
        UnreadNote = ScanUnreadNote.Describe(_unreadDirectories) ?? string.Empty;
    }

    [RelayCommand]
    private Task RestartAsAdmin()
    {
        return _restartAsAdmin?.Invoke() ?? Task.CompletedTask;
    }
}
