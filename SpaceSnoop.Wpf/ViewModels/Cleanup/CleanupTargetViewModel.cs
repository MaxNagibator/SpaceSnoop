using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupTargetViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(ShowShare))]
    private long _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesText))]
    [NotifyPropertyChangedFor(nameof(HasContent))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    private int _files;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnreadableText))]
    [NotifyPropertyChangedFor(nameof(HasUnreadable))]
    private int _unreadable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    [NotifyPropertyChangedFor(nameof(HasAvailabilityIssue))]
    [NotifyPropertyChangedFor(nameof(IsUnsupported))]
    [NotifyPropertyChangedFor(nameof(ShowSystemTool))]
    [NotifyPropertyChangedFor(nameof(ShowShare))]
    [NotifyPropertyChangedFor(nameof(IsAvailabilityError))]
    [NotifyPropertyChangedFor(nameof(IsAvailabilityWarning))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    private CleanupAvailability _availability;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumbers))]
    [NotifyPropertyChangedFor(nameof(ShowShare))]
    [NotifyPropertyChangedFor(nameof(ShowPending))]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    private bool _isMeasuring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumbers))]
    [NotifyPropertyChangedFor(nameof(ShowShare))]
    [NotifyPropertyChangedFor(nameof(ShowPending))]
    private bool _isMeasured;

    [ObservableProperty]
    private double _share;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AvailabilityText))]
    private string? _failureMessage;

    public CleanupTarget Model { get; }

    public PackIconLucideKind IconKind { get; }

    public string Name => Model.Name;

    public string Description => Model.Kind == CleanupTargetKind.RecycleBin
        ? "Удалённые файлы всех томов · Очищается целиком, фильтр возраста не применяется"
        : Model.Description;

    public bool UsesSystemTool => !Model.Supported;
    public bool ShowSystemTool => UsesSystemTool && Availability != CleanupAvailability.Missing;

    public bool IsAutomatic => Model.Supported;

    public bool HasPath => Model.Kind == CleanupTargetKind.Directory;

    public bool IsAvailable => Availability == CleanupAvailability.Available;

    public bool HasContent => Files > 0;

    public bool CanClean => IsAvailable && HasContent && SizeBytes > 0;

    public bool ShowNumbers => IsMeasured && !IsMeasuring;
    public bool ShowShare => ShowNumbers && IsAutomatic && IsAvailable && SizeBytes > 0;

    public bool ShowPending => !ShowNumbers;

    public string PendingText => IsMeasuring ? "замеряю…" : "не замерено";

    public bool HasUnreadable => Unreadable > 0;

    public string UnreadableText => $"не прочитано каталогов: {Unreadable:N0} – этих данных в замере нет";

    public bool IsUnsupported => Availability == CleanupAvailability.Unsupported;

    public bool HasAvailabilityIssue => Availability is not (CleanupAvailability.None or CleanupAvailability.Available);
    public bool IsAvailabilityError => Availability == CleanupAvailability.Failed;
    public bool IsAvailabilityWarning => Availability is CleanupAvailability.NeedsAdmin or CleanupAvailability.Unsafe;

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public string FilesText => Model.Kind == CleanupTargetKind.RecycleBin
        ? $"{Files:N0} {Plural.Word(Files, "объект", "объекта", "объектов")}"
        : $"{Files:N0} {Plural.Word(Files, "файл", "файла", "файлов")}";

    public string AvailabilityText => FormatAvailabilityText();

    public CleanupTargetViewModel(CleanupTarget target)
    {
        Model = target;
        _path = target.Path;
        IconKind = IconFor(target.Id);
    }

    public void Apply(CleanupMeasurement measurement, string? failureMessage = null)
    {
        if (!string.IsNullOrEmpty(measurement.ResolvedPath))
        {
            Path = measurement.ResolvedPath;
        }

        FailureMessage = string.IsNullOrWhiteSpace(failureMessage) ? null : failureMessage.Trim();
        Availability = measurement.Availability;
        SizeBytes = measurement.Bytes;
        Files = measurement.Files;
        Unreadable = measurement.Unreadable.Count;
        IsMeasured = true;

        if (!IsAvailable)
        {
            IsSelected = false;
        }
    }

    private string FormatAvailabilityText()
    {
        if (!HasAvailabilityIssue)
        {
            return string.Empty;
        }

        var label = CleanupText.Availability(Availability);
        return Availability == CleanupAvailability.Failed && !string.IsNullOrWhiteSpace(FailureMessage)
            ? $"{label}: {FailureMessage}"
            : label;
    }

    private static PackIconLucideKind IconFor(string id)
    {
        return id switch
        {
            "TempFiles" => PackIconLucideKind.FileClock,
            "SystemTemp" => PackIconLucideKind.Cog,
            "WindowsUpdate" => PackIconLucideKind.Download,
            "DeliveryOptimization" => PackIconLucideKind.Share2,
            "Prefetch" => PackIconLucideKind.Zap,
            "Thumbnails" => PackIconLucideKind.Image,
            "DirectXShaderCache" => PackIconLucideKind.Sparkles,
            "GpuShaderCache" => PackIconLucideKind.Cpu,
            "RecycleBin" => PackIconLucideKind.Trash2,
            "ErrorReports" => PackIconLucideKind.Bug,
            "WindowsErrorReporting" => PackIconLucideKind.FileText,
            "LiveKernelReports" => PackIconLucideKind.ShieldAlert,
            _ => PackIconLucideKind.HardDrive,
        };
    }
}
