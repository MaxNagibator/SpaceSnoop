using MahApps.Metro.IconPacks;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed class SyncLedgerViewModel : ObservableObject
{
    private readonly SyncGitViewModel _git;
    private readonly Func<SyncDirection> _direction;

    private ComparisonResult? _result;
    private bool _hashesCompared;
    private Dictionary<ComparisonStatus, int> _stats = NewZeroStats();
    private Dictionary<ComparisonStatus, int> _dirStats = NewZeroStats();
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

    public string PlanVolumeHint => ConfirmDialogViewModel.AsText(BuildPlanLines(_plan, null, BuildReceivers(_plan)));

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

    private NewerSide EffectiveNewerSide => CombineNewer(_freshness.Verdict, _git.GitInSync, _git.GitNewerSign);

    private bool GitDecidesNewer => _git.GitInSync || _git.GitNewerSign != 0;

    internal static NewerSide CombineNewer(NewerSide freshness, bool gitInSync, int gitNewerSign)
    {
        if (gitInSync)
        {
            return NewerSide.Tie;
        }

        return gitNewerSign switch
        {
            < 0 => NewerSide.Left,
            > 0 => NewerSide.Right,
            _ => freshness,
        };
    }

    internal static (Dictionary<ComparisonStatus, int> Files, Dictionary<ComparisonStatus, int> Dirs) CountRemaining(
        DirectoryComparison root,
        IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        var files = NewZeroStats();
        var dirs = NewZeroStats();
        WalkRemaining(root, outcomes, files, dirs);
        return (files, dirs);
    }

    internal static List<ConfirmLine> BuildPlanLines(
        PlannedActions planned,
        string? direction,
        IReadOnlyList<PlanReceiver> receivers,
        bool bothWays = false)
    {
        var lines = new List<ConfirmLine>();

        if (direction is not null)
        {
            lines.Add(new ConfirmTextLine($"Направление: {direction}."));
            lines.Add(new ConfirmGapLine());
        }

        if (planned.Total == 0)
        {
            lines.Add(new ConfirmTextLine("Изменений нет."));
            return lines;
        }

        if (planned.Copies > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Скопировать файлов",
                $"{planned.Copies:N0}",
                SizeFormatter.Format(planned.CopyBytes)));

            if (planned.ModifiedCopies > 0)
            {
                lines.Add(new ConfirmMetricLine(
                    "новых",
                    $"{planned.NewCopies:N0}",
                    SizeFormatter.Format(planned.NewCopyBytes),
                    ConfirmMetricTone.Sub));

                lines.Add(new ConfirmMetricLine(
                    "изменённых",
                    $"{planned.ModifiedCopies:N0}",
                    SizeFormatter.Format(planned.ModifiedCopyBytes),
                    ConfirmMetricTone.Sub));
            }
        }

        if (planned.DirCopies > 0)
        {
            lines.Add(new ConfirmMetricLine("Создать каталогов", $"{planned.DirCopies:N0}", string.Empty));
        }

        if (planned.Deletes > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Удалить файлов в корзину",
                $"{planned.Deletes:N0}",
                SizeFormatter.Format(planned.DeleteFileBytes),
                ConfirmMetricTone.Danger));
        }

        if (planned.DirDeletes > 0)
        {
            lines.Add(new ConfirmMetricLine(
                "Удалить каталогов целиком",
                $"{planned.DirDeletes:N0}",
                SizeFormatter.Format(planned.DeleteDirBytes),
                ConfirmMetricTone.Danger));

            var whole = planned.DirDeletes == 1
                ? "Каталог уходит в корзину со всем содержимым"
                : "Каталоги уходят в корзину со всем содержимым";

            lines.Add(new ConfirmTextLine(
                planned.Deletes > 0
                    ? $"{whole} – эти файлы в число {planned.Deletes:N0} не входят."
                    : $"{whole}.",
                ConfirmTextTone.Muted));
        }

        var space = DescribeReceivers(receivers, bothWays);

        if (space.Count > 0)
        {
            lines.Add(new ConfirmGapLine());
            lines.AddRange(space);
        }

        if (planned.DeleteBytes > 0)
        {
            lines.Add(new ConfirmGapLine());
            lines.Add(new ConfirmTextLine(
                "Удалённое уходит в корзину – место освободится после её очистки.",
                ConfirmTextTone.Muted));
        }

        return lines;
    }

    internal static string DescribePlanVolume(PlannedActions planned)
    {
        var parts = new List<string>();

        if (planned.CopyBytes > 0)
        {
            parts.Add($"копирование ≈{SizeFormatter.Format(planned.CopyBytes)}");
        }

        if (planned.DeleteBytes > 0)
        {
            parts.Add($"в корзину ≈{SizeFormatter.Format(planned.DeleteBytes)}");
        }

        return parts.Count > 0 ? string.Join(" · ", parts) : $"действий: {planned.Total:N0}";
    }

    internal static SyncVerifyState ResolveVerify(bool requested, SyncReport report)
    {
        if (!requested)
        {
            return SyncVerifyState.None;
        }

        return report.Verified ? SyncVerifyState.Completed : SyncVerifyState.Interrupted;
    }

    internal static string DescribeVerify(bool requested, SyncReport report)
    {
        return ResolveVerify(requested, report) switch
        {
            SyncVerifyState.Completed => $", расхождений: {report.Mismatches.Count:N0}",
            SyncVerifyState.Interrupted => $", проверка прервана (расхождений к тому моменту: {report.Mismatches.Count:N0})",
            _ => string.Empty,
        };
    }

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
            _stats = NewZeroStats();
            _dirStats = NewZeroStats();
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

        var (files, dirs) = CountRemaining(_result.Root, outcomes);
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
        var lines = BuildPlanLines(planned, null, BuildReceivers(planned), direction.BothWays);

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
            Summary = DescribePlanVolume(planned),
            Warning = deletes > 0 ? DescribeDeletionRecency(current) : null,
        };
    }

    private static Dictionary<ComparisonStatus, int> NewZeroStats()
    {
        var stats = new Dictionary<ComparisonStatus, int>();

        foreach (var status in Enum.GetValues<ComparisonStatus>())
        {
            stats[status] = 0;
        }

        return stats;
    }

    private static void WalkRemaining(
        DirectoryComparison dir,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> files,
        Dictionary<ComparisonStatus, int> dirs)
    {
        foreach (var file in dir.Files)
        {
            CountRemainingFile(file, outcomes, files);
        }

        foreach (var sub in dir.SubDirectories)
        {
            CountRemainingDirectory(sub, outcomes, dirs);
            WalkRemaining(sub, outcomes, files, dirs);
        }
    }

    private static void CountRemainingFile(
        FileComparison file,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> files)
    {
        if (outcomes.GetValueOrDefault(file) == SyncOutcome.Applied)
        {
            if (file.Action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                files[ComparisonStatus.Identical]++;
            }

            return;
        }

        files[file.Status]++;
    }

    private static void CountRemainingDirectory(
        DirectoryComparison dir,
        IReadOnlyDictionary<object, SyncOutcome> outcomes,
        Dictionary<ComparisonStatus, int> dirs)
    {
        if (outcomes.GetValueOrDefault(dir) == SyncOutcome.Applied)
        {
            if (dir.Action is not (SyncAction.DeleteLeft or SyncAction.DeleteRight))
            {
                dirs[ComparisonStatus.Identical]++;
            }

            return;
        }

        dirs[dir.Status]++;
    }

    private static List<ConfirmLine> DescribeReceivers(IReadOnlyList<PlanReceiver> receivers, bool bothWays)
    {
        var lines = new List<ConfirmLine>();

        foreach (var receiver in receivers)
        {
            if (receiver.Required <= 0)
            {
                continue;
            }

            if (lines.Count > 0)
            {
                lines.Add(new ConfirmGapLine());
            }

            lines.Add(new ConfirmTextLine($"Приёмник {receiver.Path}", ConfirmTextTone.Muted));
            lines.Add(new ConfirmMetricLine("Потребуется", string.Empty, $"≈{SizeFormatter.Format(receiver.Required)}"));

            if (receiver.Free is not { } free)
            {
                lines.Add(new ConfirmMetricLine("Свободно", string.Empty, "неизвестно"));
                continue;
            }

            var enough = free >= receiver.Required;

            lines.Add(new ConfirmMetricLine(
                "Свободно",
                string.Empty,
                SizeFormatter.Format(free),
                enough ? ConfirmMetricTone.None : ConfirmMetricTone.Danger));

            if (!enough)
            {
                lines.Add(new ConfirmTextLine(
                    $"Не хватает ≈{SizeFormatter.Format(receiver.Required - free)}.",
                    ConfirmTextTone.Danger));
            }
        }

        if (bothWays && lines.Count > 0 && receivers.Count(static receiver => receiver.Required > 0) == 1)
        {
            lines.Add(new ConfirmTextLine("Во встречном направлении копирования нет.", ConfirmTextTone.Muted));
        }

        return lines;
    }

    private static long? TryGetFreeSpace(string path)
    {
        // TODO: свободное место на UNC-приёмнике не читается – DriveInfo знает только локальные корни; перейти на GetDiskFreeSpaceEx, когда появятся жалобы на сетевые папки
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);

            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? DescribeDeletionRecency(ComparisonResult result)
    {
        var (_, newest) = SyncFreshness.DeletionRecency(result.Root);

        return newest is { } when
            ? $"Новейшее из удаляемого изменено {SyncGitViewModel.FormatStamp(when)} ({SyncGitViewModel.FormatAge(when)}) – убедитесь, что зеркалите не более свежую папку."
            : null;
    }

    private List<PlanReceiver> BuildReceivers(PlannedActions planned)
    {
        if (_result is null)
        {
            return [];
        }

        var receivers = new List<PlanReceiver>();

        if (planned.RequiredLeftBytes > 0)
        {
            receivers.Add(new(_result.LeftPath, planned.RequiredLeftBytes, TryGetFreeSpace(_result.LeftPath)));
        }

        if (planned.RequiredRightBytes > 0)
        {
            receivers.Add(new(_result.RightPath, planned.RequiredRightBytes, TryGetFreeSpace(_result.RightPath)));
        }

        return receivers;
    }

    private string BuildSyncCommandHint()
    {
        if (_result is null)
        {
            return "Сначала выполните сравнение.";
        }

        var direction = _direction();
        var lines = BuildPlanLines(_plan, direction.Text, BuildReceivers(_plan), direction.BothWays);

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
