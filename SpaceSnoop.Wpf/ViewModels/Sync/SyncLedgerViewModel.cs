using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed class SyncLedgerViewModel : ObservableObject
{
    private readonly SyncGitViewModel _git;
    private readonly Func<SyncDirection> _direction;

    private ComparisonResult? _result;
    private bool _hashesCompared;
    private Dictionary<ComparisonStatus, int> _stats = SyncPlanNarrative.NewZeroStats();
    private Dictionary<ComparisonStatus, int> _dirStats = SyncPlanNarrative.NewZeroStats();
    private FreshnessSummary _freshness;
    private PlannedActions _plan = PlannedActions.Empty;
    private int _total;

    internal SyncLedgerViewModel(SyncGitViewModel git, Func<SyncDirection> direction)
    {
        _git = git;
        _direction = direction;
        _git.StateChanged += NotifyNewerBadgeChanged;
    }

    public bool HasResult => _result is not null;

    public bool SyncIsPrimary => HasActionableChanges();

    public int IdenticalCount => _stats[ComparisonStatus.Identical];

    public int LeftOnlyCount => _stats[ComparisonStatus.LeftOnly];

    public int RightOnlyCount => _stats[ComparisonStatus.RightOnly];

    public int ModifiedCount => _stats[ComparisonStatus.Modified];

    public int ConflictCount => _stats[ComparisonStatus.Conflict];

    public int IdenticalDirCount => _dirStats[ComparisonStatus.Identical];

    public int LeftOnlyDirCount => _dirStats[ComparisonStatus.LeftOnly];

    public int RightOnlyDirCount => _dirStats[ComparisonStatus.RightOnly];

    public int ModifiedDirCount => _dirStats[ComparisonStatus.Modified];

    public int DifferingTotal => LeftOnlyCount + RightOnlyCount + ModifiedCount + ConflictCount;

    public bool HasDifferences => DifferingTotal > 0;

    public double LeftOnlyFraction => Fraction(ComparisonStatus.LeftOnly);

    public double RightOnlyFraction => Fraction(ComparisonStatus.RightOnly);

    public double ModifiedFraction => Fraction(ComparisonStatus.Modified);

    public double ConflictFraction => Fraction(ComparisonStatus.Conflict);

    public bool SyncIsDestructive => _plan.Deletes + _plan.DirDeletes > 0;

    public string CompareCommandHint => HasResult
        ? "Пересчитать различия заново. Файлы не трогает."
        : "Сравнить каталоги и показать различия. Файлы не трогает.";

    public string SyncCommandHint => BuildSyncCommandHint();

    public bool HasPlanVolume => _plan.CopyBytes > 0 || _plan.DeleteBytes > 0;

    public bool HasPlanCopy => _plan.CopyBytes > 0;

    public bool HasPlanTrash => _plan.DeleteBytes > 0;

    public string PlanCopyText => SizeFormatter.Format(_plan.CopyBytes);

    public string PlanTrashText => SizeFormatter.Format(_plan.DeleteBytes);

    public string PlanVolumeHint => ConfirmDialogViewModel.AsText(
        SyncPlanNarrative.BuildPlanLines(_plan, null, SyncPlanNarrative.BuildReceivers(_result, _plan)));

    public bool HasConflicts => ConflictCount > 0;

    public bool ShowNewerBadge => _result is not null && EffectiveNewerSide != NewerSide.None;

    public PackIconLucideKind NewerBadgeIconKind => EffectiveNewerSide switch
    {
        NewerSide.Left => PackIconLucideKind.ArrowLeft,
        NewerSide.Right => PackIconLucideKind.ArrowRight,
        _ => PackIconLucideKind.ArrowRightLeft,
    };

    public bool NewerIsLeft => EffectiveNewerSide == NewerSide.Left;

    public bool NewerIsRight => EffectiveNewerSide == NewerSide.Right;

    public string NewerBadgeText => EffectiveNewerSide switch
    {
        NewerSide.Left => "Слева",
        NewerSide.Right => "Справа",
        NewerSide.Tie => "Поровну",
        _ => string.Empty,
    };

    public string NewerBadgeTooltip => GitDecidesNewer
        ? $"Свежее по коммитам git: слева {SyncGitViewModel.FormatStamp(_git.LeftState?.CommittedAt?.LocalDateTime)}, справа {SyncGitViewModel.FormatStamp(_git.RightState?.CommittedAt?.LocalDateTime)}."
        : $"Свежее по новейшему изменённому файлу: слева {SyncGitViewModel.FormatStamp(_freshness.LeftChangedMax)}, справа {SyncGitViewModel.FormatStamp(_freshness.RightChangedMax)}.{Environment.NewLine}"
          + $"Изменённых новее: слева {_freshness.LeftNewer:N0}, справа {_freshness.RightNewer:N0}.";

    public string CompositionHint =>
        $"Состав различий ({DifferingTotal:N0} файлов): только слева {LeftOnlyCount:N0}, только справа {RightOnlyCount:N0}, изменены {ModifiedCount:N0}, конфликты {ConflictCount:N0}.{Environment.NewLine}"
        + $"Каталоги: только слева {LeftOnlyDirCount:N0}, только справа {RightOnlyDirCount:N0}, изменены {ModifiedDirCount:N0}, одинаковые {IdenticalDirCount:N0}.{Environment.NewLine}"
        + $"Одинаковых файлов {IdenticalCount:N0} – в полосу не входят.";

    internal int Total => _total;

    internal PlannedActions CurrentPlan => _result?.CountPlannedActions() ?? PlannedActions.Empty;

    private NewerSide EffectiveNewerSide => SyncPlanNarrative.CombineNewer(_freshness.Verdict, _git.GitInSync, _git.GitNewerSign);

    private bool GitDecidesNewer => _git.GitInSync || _git.GitNewerSign != 0;

    internal bool HasActionableChanges()
    {
        return _result is not null && _result.CountPlannedActions().Total > 0;
    }

    internal void Update(ComparisonResult? result, bool hashesCompared)
    {
        _result = result;
        _hashesCompared = hashesCompared;

        if (result is null)
        {
            _stats = SyncPlanNarrative.NewZeroStats();
            _dirStats = SyncPlanNarrative.NewZeroStats();
            _freshness = default;
            _plan = PlannedActions.Empty;
            _total = 0;
        }
        else
        {
            var stats = result.GetStatistics();

            _stats = stats;
            _dirStats = result.GetDirectoryStatistics();
            _freshness = SyncFreshness.Compute(result.Root);
            _plan = result.CountPlannedActions();
            _total = stats.Values.Sum();
        }

        NotifyLedgerChanged();
    }

    internal void RefreshAfterSync(IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        if (_result is null)
        {
            return;
        }

        var (files, dirs) = SyncPlanNarrative.CountRemaining(_result.Root, outcomes);
        _stats = files;
        _dirStats = dirs;
        _total = files.Values.Sum();
        _freshness = default;
        _plan = PlannedActions.Empty;
        NotifyLedgerChanged();
    }

    internal ConfirmDialogViewModel BuildSyncConfirmation(ComparisonResult current, ConfirmChoice hashes)
    {
        var direction = _direction();
        var planned = CurrentPlan;
        var deletes = planned.Deletes + planned.DirDeletes;
        var offerHashes = planned.ModifiedCopies > 0 && !_hashesCompared;
        var lines = SyncPlanNarrative.BuildPlanLines(planned, null, SyncPlanNarrative.BuildReceivers(_result, planned), direction.BothWays);

        if (offerHashes)
        {
            lines.Add(new ConfirmGapLine());
            lines.Add(new ConfirmTextLine(
                "Изменённые отличаются размером или датой. «Сверить хеши» сравнит их содержимым.",
                ConfirmTextTone.Muted));
        }

        var choices = new List<ConfirmChoice> { new("Отмена", ConfirmChoiceKind.Dismissive) };

        if (offerHashes)
        {
            choices.Add(hashes);
        }

        choices.Add(new("Синхронизировать", deletes > 0 ? ConfirmChoiceKind.Destructive : ConfirmChoiceKind.Primary));

        return new($"Синхронизация {direction.Text}", direction.IconKind, lines, choices)
        {
            Summary = SyncPlanNarrative.DescribePlanVolume(planned),
            Warning = BuildSyncWarning(current, deletes),
        };
    }

    private static string? BuildSyncWarning(ComparisonResult current, int deletes)
    {
        var incomplete = SyncPlanNarrative.DescribeIncomplete(current);
        var recency = deletes > 0 ? SyncPlanNarrative.DescribeDeletionRecency(current) : null;

        return string.Join(' ', new[] { incomplete, recency }.OfType<string>()) is { Length: > 0 } warning
            ? warning
            : null;
    }

    private string BuildSyncCommandHint()
    {
        if (_result is null)
        {
            return "Сначала выполните сравнение.";
        }

        var direction = _direction();
        var lines = SyncPlanNarrative.BuildPlanLines(_plan, direction.Text, SyncPlanNarrative.BuildReceivers(_result, _plan), direction.BothWays);

        if (SyncIsDestructive)
        {
            lines.Add(new ConfirmGapLine());
            lines.Add(new ConfirmTextLine("Удаление идёт в корзину, копирование перезаписывает файлы на приёмнике."));
        }

        return ConfirmDialogViewModel.AsText(lines);
    }

    private double Fraction(ComparisonStatus status)
    {
        var differing = DifferingTotal;

        return differing > 0 ? (double)_stats[status] / differing : 0;
    }

    private void NotifyLedgerChanged()
    {
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(IdenticalCount));
        OnPropertyChanged(nameof(LeftOnlyCount));
        OnPropertyChanged(nameof(RightOnlyCount));
        OnPropertyChanged(nameof(ModifiedCount));
        OnPropertyChanged(nameof(ConflictCount));
        OnPropertyChanged(nameof(IdenticalDirCount));
        OnPropertyChanged(nameof(LeftOnlyDirCount));
        OnPropertyChanged(nameof(RightOnlyDirCount));
        OnPropertyChanged(nameof(ModifiedDirCount));
        OnPropertyChanged(nameof(DifferingTotal));
        OnPropertyChanged(nameof(HasDifferences));
        OnPropertyChanged(nameof(LeftOnlyFraction));
        OnPropertyChanged(nameof(RightOnlyFraction));
        OnPropertyChanged(nameof(ModifiedFraction));
        OnPropertyChanged(nameof(ConflictFraction));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(CompositionHint));
        OnPropertyChanged(nameof(SyncIsDestructive));
        OnPropertyChanged(nameof(CompareCommandHint));
        OnPropertyChanged(nameof(SyncCommandHint));
        OnPropertyChanged(nameof(HasPlanVolume));
        OnPropertyChanged(nameof(HasPlanCopy));
        OnPropertyChanged(nameof(HasPlanTrash));
        OnPropertyChanged(nameof(PlanCopyText));
        OnPropertyChanged(nameof(PlanTrashText));
        OnPropertyChanged(nameof(PlanVolumeHint));
        NotifyNewerBadgeChanged();
        OnPropertyChanged(nameof(SyncIsPrimary));
    }

    private void NotifyNewerBadgeChanged()
    {
        OnPropertyChanged(nameof(ShowNewerBadge));
        OnPropertyChanged(nameof(NewerBadgeIconKind));
        OnPropertyChanged(nameof(NewerBadgeText));
        OnPropertyChanged(nameof(NewerBadgeTooltip));
        OnPropertyChanged(nameof(NewerIsLeft));
        OnPropertyChanged(nameof(NewerIsRight));
    }
}
