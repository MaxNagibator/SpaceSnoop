using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewRowViewModel : ObservableObject
{
    private readonly Action<SyncProfile> _openInSync;

    private FreshnessSummary _freshness;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusIconKind), nameof(HasCounts), nameof(ShowNewerBadge))]
    private OverviewRunStatus _status;

    [ObservableProperty]
    private int _leftOnlyCount;

    [ObservableProperty]
    private int _rightOnlyCount;

    [ObservableProperty]
    private int _modifiedCount;

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private int _identicalCount;

    [ObservableProperty]
    private int _dirDiffCount;

    [ObservableProperty]
    private long _elapsedMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _error;

    public OverviewRowViewModel(SyncProfile profile, Action<SyncProfile> openInSync)
    {
        Profile = profile;
        _openInSync = openInSync;
    }

    public SyncProfile Profile { get; }

    public string Name => Profile.Name;

    public string Left => Profile.Left;

    public string Right => Profile.Right;

    public int DiffCount => LeftOnlyCount + RightOnlyCount + ModifiedCount + ConflictCount;

    public bool HasCounts => Status == OverviewRunStatus.Compared;

    public bool ShowNewerBadge => Status == OverviewRunStatus.Compared && _freshness.Verdict != NewerSide.None;

    public PackIconLucideKind NewerBadgeIconKind => _freshness.Verdict switch
    {
        NewerSide.Left => PackIconLucideKind.ArrowLeft,
        NewerSide.Right => PackIconLucideKind.ArrowRight,
        _ => PackIconLucideKind.ArrowRightLeft,
    };

    public bool NewerIsLeft => _freshness.Verdict == NewerSide.Left;

    public bool NewerIsRight => _freshness.Verdict == NewerSide.Right;

    public string NewerBadgeText => _freshness.Verdict switch
    {
        NewerSide.Left => "СЛЕВА",
        NewerSide.Right => "СПРАВА",
        NewerSide.Tie => "ПОРОВНУ",
        _ => string.Empty,
    };

    public string NewerBadgeTooltip =>
        $"Свежее по изменённым файлам: слева {_freshness.LeftNewer:N0}, справа {_freshness.RightNewer:N0}.{Environment.NewLine}"
        + $"Новейший файл слева: {FormatStamp(_freshness.LeftMax)}, справа: {FormatStamp(_freshness.RightMax)}.{Environment.NewLine}"
        + $"Только слева: {_freshness.LeftOnly:N0}, только справа: {_freshness.RightOnly:N0}.";

    public string BreakdownText =>
        $"слева {LeftOnlyCount} · справа {RightOnlyCount} · изм {ModifiedCount}"
        + (ConflictCount > 0 ? $" · конфл {ConflictCount}" : string.Empty)
        + (DirDiffCount > 0 ? $" · кат {DirDiffCount}" : string.Empty);

    public string StatusText => Status switch
    {
        OverviewRunStatus.Comparing => "Сравнение…",
        OverviewRunStatus.Compared => DiffCount == 0 ? "Идентичны" : $"Различий: {DiffCount}",
        OverviewRunStatus.Unavailable => "Каталог недоступен",
        OverviewRunStatus.Overlap => "Пути пересекаются или вложены",
        OverviewRunStatus.Error => Error ?? "Ошибка",
        _ => "Не сравнивалось",
    };

    public PackIconLucideKind StatusIconKind => Status switch
    {
        OverviewRunStatus.Comparing => PackIconLucideKind.Loader,
        OverviewRunStatus.Compared => DiffCount == 0 ? PackIconLucideKind.Check : PackIconLucideKind.GitCompareArrows,
        OverviewRunStatus.Unavailable => PackIconLucideKind.FolderX,
        OverviewRunStatus.Overlap => PackIconLucideKind.TriangleAlert,
        OverviewRunStatus.Error => PackIconLucideKind.CircleX,
        _ => PackIconLucideKind.Minus,
    };

    public void ApplyStatistics(IReadOnlyDictionary<ComparisonStatus, int> files, IReadOnlyDictionary<ComparisonStatus, int> directories)
    {
        LeftOnlyCount = files.GetValueOrDefault(ComparisonStatus.LeftOnly);
        RightOnlyCount = files.GetValueOrDefault(ComparisonStatus.RightOnly);
        ModifiedCount = files.GetValueOrDefault(ComparisonStatus.Modified);
        ConflictCount = files.GetValueOrDefault(ComparisonStatus.Conflict);
        IdenticalCount = files.GetValueOrDefault(ComparisonStatus.Identical);
        DirDiffCount = directories.GetValueOrDefault(ComparisonStatus.LeftOnly)
                       + directories.GetValueOrDefault(ComparisonStatus.RightOnly)
                       + directories.GetValueOrDefault(ComparisonStatus.Modified);

        OnPropertyChanged(nameof(DiffCount));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusIconKind));
        OnPropertyChanged(nameof(BreakdownText));
    }

    internal void ApplyFreshness(FreshnessSummary freshness)
    {
        _freshness = freshness;

        OnPropertyChanged(nameof(ShowNewerBadge));
        OnPropertyChanged(nameof(NewerBadgeIconKind));
        OnPropertyChanged(nameof(NewerBadgeText));
        OnPropertyChanged(nameof(NewerBadgeTooltip));
        OnPropertyChanged(nameof(NewerIsLeft));
        OnPropertyChanged(nameof(NewerIsRight));
    }

    private static string FormatStamp(DateTime? value)
    {
        return value is { } stamp ? stamp.ToString("yyyy-MM-dd HH:mm") : "–";
    }

    [RelayCommand]
    private void OpenInSync()
    {
        _openInSync(Profile);
    }
}
