using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class ArchiveProgressDialogViewModel : OperationDialogViewModelBase
{
    private readonly ArchiveRequest _request;
    private readonly ArchiveService _service;
    private readonly ILogger _logger;
    private readonly Action<string, bool> _deleteDirectory;
    private readonly Func<string, ArchiveFileMetadata> _readArchiveMetadata;
    private readonly OperationProgressState _packProgress = new();
    private readonly OperationProgressState _verifyProgress = new();
    private readonly OperationProgressState _coverageProgress = new();
    private readonly UiProgressPoller _progressPoller;

    internal static Func<string, bool> SupportsRecycleBin { get; set; } = RecycleBin.SupportsRecycleBin;

    private volatile ArchivePhase _phase;
    private volatile CoverageTotals _coverageTotals = CoverageTotals.Empty;
    private bool _packed;
    private bool _deletingOriginal;
    private int _packTotal;
    private int _verifyTotal;
    private int _phaseTotal;
    private string _resultSummary = string.Empty;
    private string? _coverageIssue;
    private string? _unreadableIssue;

    [ObservableProperty]
    private string _countLabel = "Упаковано";

    [ObservableProperty]
    private string _sourceSize;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FateText))]
    private bool _confirmPermanentOriginalDelete;

    public override string Title => "Упаковка в архив";

    public string ActionText => "Упаковать";

    public string SourceName { get; }

    public string TargetName { get; }

    public bool NeedsPermanentOriginalConfirm { get; }

    public string FateText => DescribeFate();

    private bool DeleteOriginalEffective => _request.DeleteOriginal && (!NeedsPermanentOriginalConfirm || ConfirmPermanentOriginalDelete);

    public bool OriginalDeleted { get; private set; }

    public string? CreatedArchivePath { get; private set; }

    internal ArchiveFileMetadata? CreatedArchive { get; private set; }

    public bool IsIndeterminate => _phaseTotal == 0;

    protected override string RunningStatus => "Обход каталога…";

    protected override bool CloseResult => OriginalDeleted;

    protected override bool HasFailedItems => _coverageIssue is not null || _unreadableIssue is not null;

    public ArchiveProgressDialogViewModel(ArchiveRequest request, ArchiveService service, IUiDispatcher uiDispatcher, ILogger logger)
        : this(request, service, uiDispatcher, logger, service.DeleteDirectoryToRecycleBin)
    {
    }

    internal ArchiveProgressDialogViewModel(
        ArchiveRequest request,
        ArchiveService service,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Action<string, bool> deleteDirectory,
        bool? needsPermanentConfirm = null,
        Func<string, ArchiveFileMetadata>? readArchiveMetadata = null)
    {
        _request = request;
        _service = service;
        _logger = logger;
        _deleteDirectory = deleteDirectory;
        _readArchiveMetadata = readArchiveMetadata ?? ReadArchiveMetadata;
        _progressPoller = new(uiDispatcher, OnProgressTick);

        SourceName = Path.GetFileName(request.SourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        SourceSize = $"≈ {SizeFormatter.Format(request.TotalBytes)} · ≈ {request.EstimatedFiles:N0} {Plural.Word(request.EstimatedFiles, "файл", "файла", "файлов")}";
        TargetName = Path.GetFileName(request.TargetPath);
        NeedsPermanentOriginalConfirm = request.DeleteOriginal && request.Interactive
            && (needsPermanentConfirm ?? RequiresPermanentConfirmation(request.SourcePath));
    }

    internal static ArchiveFileMetadata ReadArchiveMetadata(string path)
    {
        FileInfo info = new(path);
        var length = info.Length;
        return new(info.Name, length, info.CreationTime, info.LastAccessTime);
    }

    internal static bool RequiresPermanentConfirmation(string sourcePath) => !SupportsRecycleBin(sourcePath);

    internal void ShowRunningForAutomation(int packed, int total, long bytes, string current)
    {
        IsRunning = true;
        StatusText = RunningStatus;
        _packTotal = total;

        OnStage(new(ArchiveStage.Packing, total, total, bytes));
        OnTick(new(packed, current), false);
    }

    internal void ShowFinishedForAutomation(int total, long bytes, long compressed)
    {
        _packed = true;
        _packTotal = total;
        _resultSummary = Summarize(compressed, bytes);
        OriginalDeleted = DeleteOriginalEffective;

        CountText = $"{total} / {total}";
        ProgressValue = 1d;
        CurrentPath = string.Empty;
        SetPhaseTotal(total);

        IsRunning = false;
        IsFinished = true;
        HasErrors = HasFailedItems;
        StatusText = BuildSummary();
    }

    internal void ClearForAutomation()
    {
        IsRunning = false;
        IsFinished = false;
        OriginalDeleted = false;
        ConfirmPermanentOriginalDelete = false;
        CreatedArchivePath = null;
        CreatedArchive = null;
        _packed = false;
        _deletingOriginal = false;
        _phase = ArchivePhase.None;
        _coverageTotals = CoverageTotals.Empty;
        _coverageProgress.Reset();
        _resultSummary = string.Empty;
        SetPhaseTotal(0);
    }

    private void OnStage(ArchiveStageUpdate update)
    {
        if (IsFinished)
        {
            return;
        }

        switch (update.Stage)
        {
            case ArchiveStage.Coverage when update.Files == 0 && update.Bytes == 0:
                if (_coverageTotals == CoverageTotals.Empty)
                {
                    SetPhaseTotal(0);
                    CurrentPath = string.Empty;
                    CountText = string.Empty;
                    ProgressValue = 0d;
                    CountLabel = "Сверено";
                    StatusText = "Сверка архива с каталогом…";
                }

                return;

            case ArchiveStage.Coverage:
                OnCoverageTick(_coverageProgress.CreateSnapshot(), update.Files, update.Bytes);
                return;

            case ArchiveStage.Deleting:
                SetPhaseTotal(0);
                CurrentPath = string.Empty;
                CountText = string.Empty;
                ProgressValue = 0d;
                CountLabel = "Сверено";
                StatusText = "Перемещение в корзину…";
                return;

            case ArchiveStage.Finished:
                if (update.Files > 0 || update.Bytes > 0)
                {
                    OnCoverageTick(_coverageProgress.CreateSnapshot(), update.Files, update.Bytes);
                }
                else
                {
                    SetPhaseTotal(update.Entries);
                    CurrentPath = string.Empty;
                    CountText = "0 / 0";
                    ProgressValue = 1d;
                }

                return;

            default:
                _packTotal = update.Entries;
                SourceSize = $"{SizeFormatter.Format(update.Bytes)} · {update.Files:N0} {Plural.Word(update.Files, "файл", "файла", "файлов")}";
                SetPhaseTotal(update.Entries);
                return;
        }
    }

    private void OnProgressTick()
    {
        switch (_phase)
        {
            case ArchivePhase.Packing:
                OnTick(_packProgress.CreateSnapshot(), false);
                break;

            case ArchivePhase.Verifying:
                OnTick(_verifyProgress.CreateSnapshot(), true);
                break;

            case ArchivePhase.Coverage:
                var totals = _coverageTotals;

                if (totals.Files > 0 || totals.Bytes > 0)
                {
                    OnCoverageTick(_coverageProgress.CreateSnapshot(), totals.Files, totals.Bytes);
                }

                break;

            default:
                return;
        }
    }

    private void OnCoverageTick(OperationProgress update, int totalFiles, long totalBytes)
    {
        if (IsFinished)
        {
            return;
        }

        SetPhaseTotal(totalFiles > 0 ? totalFiles : 1);
        CountLabel = "Сверено";
        CurrentPath = update.Current;
        CountText = totalBytes > 0
            ? $"{update.Completed} / {totalFiles} · {SizeFormatter.Format(update.Bytes)} / {SizeFormatter.Format(totalBytes)}"
            : $"{update.Completed} / {totalFiles}";
        ProgressValue = CoverageFraction(update, totalFiles, totalBytes);
        StatusText = "Сверка архива с каталогом…";
    }

    private static double CoverageFraction(OperationProgress update, int totalFiles, long totalBytes)
    {
        if (totalBytes > 0)
        {
            return Math.Clamp((double)update.Bytes / totalBytes, 0d, 1d);
        }

        return totalFiles > 0 ? Math.Clamp((double)update.Completed / totalFiles, 0d, 1d) : 0d;
    }

    private void OnTick(OperationProgress update, bool verifying)
    {
        if (IsFinished)
        {
            return;
        }

        var total = verifying ? _verifyTotal : _packTotal;

        StatusText = verifying ? "Проверка архива…" : "Упаковка…";
        CountLabel = verifying ? "Проверено" : "Упаковано";
        CurrentPath = update.Current;
        CountText = total > 0 ? $"{update.Completed} / {total}" : update.Completed.ToString("N0");
        ProgressValue = total > 0 ? Math.Clamp((double)update.Completed / total, 0d, 1d) : 0d;
        SetPhaseTotal(total);
    }

    private void SetPhaseTotal(int total)
    {
        if (_phaseTotal == total)
        {
            return;
        }

        _phaseTotal = total;
        OnPropertyChanged(nameof(IsIndeterminate));
    }
    private sealed record CoverageTotals(int Files, long Bytes)
    {
        internal static readonly CoverageTotals Empty = new(0, 0);
    }
}

public enum ArchiveStage
{
    None = 0,
    Packing = 1,
    Coverage = 2,
    Deleting = 3,
    Finished = 4,
}

internal enum ArchivePhase
{
    None = 0,
    Packing = 1,
    Verifying = 2,
    Coverage = 3,
}

public readonly record struct ArchiveStageUpdate(ArchiveStage Stage, int Files, int Entries, long Bytes);

internal readonly record struct ArchiveFileMetadata(string Name, long Length, DateTime CreationTime, DateTime LastAccessTime);
