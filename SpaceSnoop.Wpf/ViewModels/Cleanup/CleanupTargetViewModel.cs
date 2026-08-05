using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupTargetViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
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
    [NotifyPropertyChangedFor(nameof(CanClean))]
    private CleanupAvailability _availability;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumbers))]
    [NotifyPropertyChangedFor(nameof(ShowPending))]
    [NotifyPropertyChangedFor(nameof(PendingText))]
    private bool _isMeasuring;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNumbers))]
    [NotifyPropertyChangedFor(nameof(ShowPending))]
    private bool _isMeasured;

    [ObservableProperty]
    private double _share;

    [ObservableProperty]
    private bool _isSelected;

    public CleanupTargetViewModel(CleanupTarget target)
    {
        Model = target;
        IconKind = IconFor(target.Id);
    }

    public CleanupTarget Model { get; }

    public PackIconLucideKind IconKind { get; }

    public string Name => Model.Name;

    public string Description => Model.Description;

    public string Path => Model.Path;

    public bool HasPath => Model.Kind == CleanupTargetKind.Directory;

    public bool IsAvailable => Availability == CleanupAvailability.Available;

    public bool HasContent => Files > 0;

    public bool CanClean => IsAvailable && HasContent;

    public bool ShowNumbers => IsMeasured && !IsMeasuring;

    public bool ShowPending => !ShowNumbers;

    public string PendingText => IsMeasuring ? "замеряю…" : "не замерено";

    public bool HasUnreadable => Unreadable > 0;

    public string UnreadableText => $"не прочитано каталогов: {Unreadable:N0} – этих данных в замере нет";

    public bool HasAvailabilityIssue => Availability is not (CleanupAvailability.None or CleanupAvailability.Available);

    public string SizeText => SizeFormatter.Format(SizeBytes);

    public string FilesText => Model.Kind == CleanupTargetKind.RecycleBin
        ? $"{Files:N0} объектов"
        : $"{Files:N0} файлов";

    public string AvailabilityText => HasAvailabilityIssue ? CleanupText.Availability(Availability) : string.Empty;

    public void Apply(CleanupMeasurement measurement)
    {
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

    private static PackIconLucideKind IconFor(string id)
    {
        return id switch
        {
            "TempFiles" => PackIconLucideKind.FileClock,
            "SystemTemp" => PackIconLucideKind.Cog,
            "WindowsUpdate" => PackIconLucideKind.Download,
            "Prefetch" => PackIconLucideKind.Zap,
            "Thumbnails" => PackIconLucideKind.Image,
            "RecycleBin" => PackIconLucideKind.Trash2,
            "ErrorReports" => PackIconLucideKind.Bug,
            _ => PackIconLucideKind.HardDrive,
        };
    }
}
