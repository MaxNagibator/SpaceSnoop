using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewRowViewModel : ObservableObject
{
    private readonly Action<SyncProfile, ComparisonResult?> _openInSync;
    private readonly Action _persist;

    private FreshnessSummary _freshness;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusIconKind), nameof(HasCounts), nameof(ShowNewerBadge), nameof(SyncHadErrors), nameof(IsUnchanged), nameof(GroupOrder), nameof(GroupKey))]
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
    private int _syncCopied;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _syncDeleted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusIconKind), nameof(SyncHadErrors), nameof(IsUnchanged), nameof(GroupOrder), nameof(GroupKey))]
    private int _syncErrors;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusIconKind), nameof(SyncHadErrors), nameof(SyncNotConverged), nameof(IsUnchanged), nameof(GroupOrder), nameof(GroupKey))]
    private int _syncMismatches;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(StatusIconKind), nameof(SyncHadErrors), nameof(SyncNotConverged), nameof(IsUnchanged), nameof(GroupOrder), nameof(GroupKey))]
    private SyncVerifyState _syncVerify;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _error;

    public OverviewRowViewModel(SyncProfile profile, Action<SyncProfile, ComparisonResult?> openInSync, Action persist)
    {
        Profile = profile;
        _openInSync = openInSync;
        _persist = persist;
    }

    public SyncProfile Profile { get; }

    public ComparisonResult? Comparison { get; set; }

    public string Name => Profile.Name;

    public string Left => Profile.Left;

    public string Right => Profile.Right;

    public bool IncludeInBatch
    {
        get => !Profile.SkipInBatch;
        set
        {
            if (value == IncludeInBatch)
            {
                return;
            }

            Profile.SkipInBatch = !value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BatchTooltip));
            _persist();
        }
    }

    public string BatchTooltip => IncludeInBatch
        ? "Профиль участвует в «Сравнить все» и «Синхронизировать всё». Клик – исключить из пакета."
        : "Профиль исключён из пакетных операций. Клик – вернуть в пакет.";

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

    public int SelectedModeIndex
    {
        get => Math.Clamp(Profile.Mode, 0, SyncOptions.Modes.Count - 1);
        set
        {
            if (value < 0 || value == Profile.Mode)
            {
                return;
            }

            Profile.Mode = value;
            OnPropertyChanged();
            AdvanceDirectionChanged();
            _persist();
        }
    }

    public int SelectedWinnerIndex
    {
        get => SyncProfile.IndexOfWinner(Profile.Winner);
        set
        {
            var winner = SyncProfile.WinnerFromIndex(value);

            if (value < 0 || winner == Profile.Winner)
            {
                return;
            }

            Profile.Winner = winner;
            OnPropertyChanged();
            AdvanceWinnerChanged();
            _persist();
        }
    }

    public PackIconLucideKind DirectionIconKind => Profile.Mode switch
    {
        1 => PackIconLucideKind.ArrowLeft,
        2 => PackIconLucideKind.ArrowRightLeft,
        _ => PackIconLucideKind.ArrowRight,
    };

    public string DirectionArrow => Profile.Mode switch
    {
        1 => "←",
        2 => "↔",
        _ => "→",
    };

    public string DirectionTooltip => Profile.Mode switch
    {
        1 => "Направление: справа налево. Клик – сменить.",
        2 => "Направление: двустороннее. Клик – сменить.",
        _ => "Направление: слева направо. Клик – сменить.",
    };

    public bool WinnerApplicable => Profile.Mode == 2;

    public PackIconLucideKind WinnerIconKind => Profile.Winner switch
    {
        SyncWinner.Left => PackIconLucideKind.ArrowLeftToLine,
        SyncWinner.Right => PackIconLucideKind.ArrowRightToLine,
        _ => PackIconLucideKind.Clock,
    };

    public string WinnerTooltip => Profile.Winner switch
    {
        SyncWinner.Left => "Победитель: слева. Клик – сменить.",
        SyncWinner.Right => "Победитель: справа. Клик – сменить.",
        _ => "Победитель: новее по дате. Клик – сменить.",
    };

    public string WinnerText => !WinnerApplicable
        ? string.Empty
        : Profile.Winner switch
        {
            SyncWinner.Left => " · победитель слева",
            SyncWinner.Right => " · победитель справа",
            _ => " · побеждает свежее",
        };

    public int DiffCount => LeftOnlyCount + RightOnlyCount + ModifiedCount + ConflictCount;

    public bool IsUnchanged =>
        Status == OverviewRunStatus.Compared && DiffCount == 0
        || Status == OverviewRunStatus.Synced && SyncErrors == 0 && SyncCopied == 0 && SyncDeleted == 0 && !SyncNotConverged;

    public int GroupOrder => IsUnchanged ? 1 : 0;

    public string GroupKey => IsUnchanged ? "Без изменений" : "Профили";

    public DateTime? NewestModified => (_freshness.LeftMax, _freshness.RightMax) switch
    {
        ({ } left, { } right) => left > right ? left : right,
        ({ } left, null) => left,
        (null, { } right) => right,
        _ => null,
    };

    public int FreshnessOrder => _freshness.Verdict switch
    {
        NewerSide.Left => 0,
        NewerSide.Right => 1,
        NewerSide.Tie => 2,
        _ => 3,
    };

    public double FreshnessLead => _freshness.LeadSeconds;

    public bool HasCounts => Status == OverviewRunStatus.Compared;

    public bool SyncNotConverged => SyncMismatches > 0 || SyncVerify == SyncVerifyState.Interrupted;

    public bool SyncHadErrors => Status == OverviewRunStatus.Synced && (SyncErrors > 0 || SyncNotConverged);

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
        NewerSide.Left => "Слева",
        NewerSide.Right => "Справа",
        NewerSide.Tie => "Поровну",
        _ => string.Empty,
    };

    public string NewerBadgeTooltip =>
        $"Свежее по новейшему изменённому файлу: слева {FormatStamp(_freshness.LeftChangedMax)}, справа {FormatStamp(_freshness.RightChangedMax)}.{Environment.NewLine}"
        + $"Изменённых новее: слева {_freshness.LeftNewer:N0}, справа {_freshness.RightNewer:N0}.{Environment.NewLine}"
        + $"Только слева: {_freshness.LeftOnly:N0}, только справа: {_freshness.RightOnly:N0}.";

    public string BreakdownText =>
        $"слева {LeftOnlyCount} · справа {RightOnlyCount} · изм {ModifiedCount}"
        + (ConflictCount > 0 ? $" · конфл {ConflictCount}" : string.Empty)
        + (DirDiffCount > 0 ? $" · кат {DirDiffCount}" : string.Empty);

    public string StatusText => Status switch
    {
        OverviewRunStatus.Comparing => "Сравнение…",
        OverviewRunStatus.Compared => DiffCount == 0 ? "Идентичны" : $"Различий: {DiffCount}",
        OverviewRunStatus.Syncing => "Синхронизация…",
        OverviewRunStatus.Synced => SyncSummary(),
        OverviewRunStatus.Unavailable => "Каталог недоступен",
        OverviewRunStatus.Overlap => "Пути пересекаются или вложены",
        OverviewRunStatus.Skipped => Error ?? "Пропущено",
        OverviewRunStatus.Error => Error ?? "Ошибка",
        _ => "Не сравнивалось",
    };

    public PackIconLucideKind StatusIconKind => Status switch
    {
        OverviewRunStatus.Comparing => PackIconLucideKind.Loader,
        OverviewRunStatus.Compared => DiffCount == 0 ? PackIconLucideKind.Check : PackIconLucideKind.GitCompareArrows,
        OverviewRunStatus.Syncing => PackIconLucideKind.RefreshCw,
        OverviewRunStatus.Synced => SyncHadErrors ? PackIconLucideKind.TriangleAlert : PackIconLucideKind.FolderCheck,
        OverviewRunStatus.Unavailable => PackIconLucideKind.FolderX,
        OverviewRunStatus.Overlap => PackIconLucideKind.TriangleAlert,
        OverviewRunStatus.Skipped => PackIconLucideKind.SkipForward,
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
        OnPropertyChanged(nameof(IsUnchanged));
        OnPropertyChanged(nameof(GroupOrder));
        OnPropertyChanged(nameof(GroupKey));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusIconKind));
        OnPropertyChanged(nameof(BreakdownText));
    }

    public void ApplySyncReport(SyncReport report, SyncVerifyState verify)
    {
        SyncCopied = report.CopiedCount;
        SyncDeleted = report.DeletedCount;
        SyncErrors = report.Errors.Count;
        SyncMismatches = report.Mismatches.Count;
        SyncVerify = verify;
        Status = OverviewRunStatus.Synced;
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
        OnPropertyChanged(nameof(NewestModified));
        OnPropertyChanged(nameof(FreshnessOrder));
        OnPropertyChanged(nameof(FreshnessLead));
    }

    internal void AdvanceDirection()
    {
        Profile.Mode = (Profile.Mode + 1) % SyncOptions.Modes.Count;
        OnPropertyChanged(nameof(SelectedModeIndex));
        AdvanceDirectionChanged();
    }

    private void AdvanceDirectionChanged()
    {
        OnPropertyChanged(nameof(DirectionIconKind));
        OnPropertyChanged(nameof(DirectionArrow));
        OnPropertyChanged(nameof(DirectionTooltip));
        OnPropertyChanged(nameof(WinnerApplicable));
        OnPropertyChanged(nameof(WinnerText));
    }

    private void AdvanceWinnerChanged()
    {
        OnPropertyChanged(nameof(WinnerIconKind));
        OnPropertyChanged(nameof(WinnerTooltip));
        OnPropertyChanged(nameof(WinnerText));
    }

    private static string FormatStamp(DateTime? value)
    {
        return value is { } stamp ? stamp.ToString("yyyy-MM-dd HH:mm") : "–";
    }

    private string SyncSummary()
    {
        var parts = new List<string>();

        if (SyncCopied > 0)
        {
            parts.Add($"скопировано {SyncCopied}");
        }

        if (SyncDeleted > 0)
        {
            parts.Add($"удалено {SyncDeleted}");
        }

        if (SyncErrors > 0)
        {
            parts.Add($"ошибок {SyncErrors}");
        }

        if (SyncMismatches > 0)
        {
            parts.Add($"расхождений {SyncMismatches}");
        }

        if (SyncVerify == SyncVerifyState.Interrupted)
        {
            parts.Add("проверка прервана");
        }

        return parts.Count > 0 ? $"Синхронизировано: {string.Join(" · ", parts)}" : "Синхронизировано: изменений не потребовалось";
    }

    [RelayCommand]
    private void OpenInSync()
    {
        _openInSync(Profile, Status == OverviewRunStatus.Compared ? Comparison : null);
    }

}
