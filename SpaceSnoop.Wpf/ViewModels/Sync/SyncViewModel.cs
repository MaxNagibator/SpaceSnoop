using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Diff;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ObservableObject, IPageHeader, IPageStatus, ISyncRowHost
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;
    private static readonly SyncMode[] ModeOrder = [SyncMode.LeftToRight, SyncMode.RightToLeft, SyncMode.Bidirectional];
    private static readonly SyncWinner[] WinnerOrder = [SyncWinner.Newest, SyncWinner.Left, SyncWinner.Right];
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SyncViewModel> _logger;
    private readonly CompareDirectoriesUseCase _compare;
    private readonly ExecuteSyncUseCase _sync;
    private readonly ToastNotifier _notifier;
    private readonly AgentPreferences _agent;
    private readonly HashSet<DirectoryComparison> _collapsed = [];
    private readonly HashSet<string> _collapsedSubGroups = new(StringComparer.OrdinalIgnoreCase);

    private ComparisonResult? _result;
    private SyncReport? _lastReport;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private string? _activeProfileId;
    private bool _suppressPersist;
    private bool _gitPromptDeclined;
    private bool _hashesCompared;
    private bool _gitGroupExpanded;
    private Dictionary<ComparisonStatus, int> _stats = NewZeroStats();
    private Dictionary<ComparisonStatus, int> _dirStats = NewZeroStats();
    private FreshnessSummary _freshness;
    private PlannedActions _plan = PlannedActions.Empty;
    private int _total;

    [ObservableProperty]
    private string _leftPath = string.Empty;

    [ObservableProperty]
    private string _rightPath = string.Empty;

    [ObservableProperty]
    private bool _leftPathInvalid;

    [ObservableProperty]
    private bool _rightPathInvalid;

    [ObservableProperty]
    private string _exclusions = string.Empty;

    [ObservableProperty]
    private int _selectedModeIndex;

    [ObservableProperty]
    private bool _mirror;

    [ObservableProperty]
    private int _selectedWinnerIndex;

    [ObservableProperty]
    private bool _showIdentical;

    [ObservableProperty]
    private bool _showSizes = AppDefaults.SyncShowSizesDefault;

    [ObservableProperty]
    private bool _showModified = AppDefaults.SyncShowModifiedDefault;

    [ObservableProperty]
    private bool _blankAbsent;

    [ObservableProperty]
    private bool _verify = AppDefaults.SyncVerifyDefault;

    [ObservableProperty]
    private bool _hideApplied;

    [ObservableProperty]
    private bool _flatView;

    [ObservableProperty]
    private SyncSortField _rowSort = AppDefaults.SyncFlatSortDefault;

    [ObservableProperty]
    private bool _rowSortDescending;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _summaryText = "Сравнение не выполнялось.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllSkipCommand))]
    private bool _hasPending;

    public SyncViewModel(ISettingsStore settings, IDialogService dialogs, OperationPreferences operations, AgentPreferences agent, ILogger<SyncViewModel> logger, CompareDirectoriesUseCase compare, ExecuteSyncUseCase sync, ToastNotifier notifier, PerformanceMonitor performance)
    {
        _settings = settings;
        _dialogs = dialogs;
        Operations = operations;
        _agent = agent;
        _logger = logger;
        _compare = compare;
        _sync = sync;
        _notifier = notifier;

        Session = new(dialogs, logger, notifier, performance, summary => SummaryText = summary);
        Session.PropertyChanged += OnSessionPropertyChanged;

        Git = new(settings, logger);
        Git.StateChanged += NotifyNewerBadgeChanged;

        Profiles = new(settings, dialogs, BuildCurrentProfile, ApplyProfile, () => !IsBusy, message => Session.StatusCaption = message);
        LoadSettings();
        Profiles.Load();
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<string>? AskAgentRequested;

    public event Action<SyncProfileRun>? ProfileRunCompleted;

    public SyncGitViewModel Git { get; }

    public SyncSessionViewModel Session { get; }

    public bool ChatEnabled => _agent.Enabled;

    public OperationPreferences Operations { get; }

    public RangeObservableCollection<SyncNodeViewModel> Rows { get; } = [];

    public SyncQuickProfilesViewModel Profiles { get; }

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

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

    public PackIconLucideKind DirectionIconKind => CurrentMode switch
    {
        SyncMode.RightToLeft => PackIconLucideKind.ArrowLeft,
        SyncMode.Bidirectional => PackIconLucideKind.ArrowRightLeft,
        _ => PackIconLucideKind.ArrowRight,
    };

    public string DirectionHint => CurrentMode switch
    {
        SyncMode.RightToLeft => "Направление: справа налево. Клик – сменить, ПКМ – поменять пути местами.",
        SyncMode.Bidirectional => "Направление: двустороннее. Клик – сменить, ПКМ – поменять пути местами.",
        _ => "Направление: слева направо. Клик – сменить, ПКМ – поменять пути местами.",
    };

    public bool MirrorApplicable => CurrentMode != SyncMode.Bidirectional || CurrentWinner is SyncWinner.Left or SyncWinner.Right;

    public bool IsBidirectional => CurrentMode == SyncMode.Bidirectional;

    public bool WinnerIsNewest => CurrentWinner == SyncWinner.Newest;

    public bool ShowConflictResolvers => IsBidirectional && WinnerIsNewest;

    public string WinnerHint => "Победитель решает изменённые и спорные файлы; при зеркале — что удалять на проигравшей стороне.";

    public string MirrorHint => CurrentMode switch
    {
        SyncMode.RightToLeft => "Зеркало: удалять слева то, чего нет справа (в корзину).",
        SyncMode.Bidirectional => CurrentWinner switch
        {
            SyncWinner.Left => "Зеркало победителя: удалять справа то, чего нет слева (в корзину).",
            SyncWinner.Right => "Зеркало победителя: удалять слева то, чего нет справа (в корзину).",
            _ => "Зеркало доступно при победителе «Слева» или «Справа».",
        },
        _ => "Зеркало: удалять справа то, чего нет слева (в корзину).",
    };

    public bool MirrorDeletes => Mirror && MirrorApplicable;

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
        ? $"Свежее по коммитам git: слева {SyncGitViewModel.FormatStamp(Git.LeftState?.CommittedAt?.LocalDateTime)}, справа {SyncGitViewModel.FormatStamp(Git.RightState?.CommittedAt?.LocalDateTime)}."
        : $"Свежее по новейшему изменённому файлу: слева {SyncGitViewModel.FormatStamp(_freshness.LeftChangedMax)}, справа {SyncGitViewModel.FormatStamp(_freshness.RightChangedMax)}.{Environment.NewLine}"
          + $"Изменённых новее: слева {_freshness.LeftNewer:N0}, справа {_freshness.RightNewer:N0}.";

    public bool ExclusionsEmpty => string.IsNullOrWhiteSpace(Exclusions);

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public bool SortByPath => RowSort is SyncSortField.Path or SyncSortField.None;

    public bool SortBySize => RowSort == SyncSortField.Size;

    public bool SortByModified => RowSort == SyncSortField.Modified;

    public PackIconLucideKind SortDirectionIconKind => RowSortDescending
        ? PackIconLucideKind.ArrowDown
        : PackIconLucideKind.ArrowUp;

    public string CompositionHint =>
        $"Состав различий ({DifferingTotal:N0} файлов): только слева {LeftOnlyCount:N0}, только справа {RightOnlyCount:N0}, изменены {ModifiedCount:N0}, конфликты {ConflictCount:N0}.{Environment.NewLine}"
        + $"Каталоги: только слева {LeftOnlyDirCount:N0}, только справа {RightOnlyDirCount:N0}, изменены {ModifiedDirCount:N0}, одинаковые {IdenticalDirCount:N0}.{Environment.NewLine}"
        + $"Одинаковых файлов {IdenticalCount:N0} – в полосу не входят.";

    public bool ShowApplied
    {
        get => !HideApplied;
        set => HideApplied = !value;
    }

    public string PageTitle => "Синхронизация";

    public string PageDescription => "Сравнение и синхронизация двух каталогов.";

    public bool IsBusy => Session.IsBusy;

    public string? StatusCaption => Session.StatusCaption;

    public bool IsIndeterminate => Session.IsIndeterminate;

    public double ProgressValue => Session.ProgressValue;

    public double ProgressMax => Session.ProgressMax;

    public ICommand CancelCommand => Session.CancelCommand;

    private NewerSide EffectiveNewerSide => CombineNewer(_freshness.Verdict, Git.GitInSync, Git.GitNewerSign);

    private bool GitDecidesNewer => Git.GitInSync || Git.GitNewerSign != 0;

    private PlannedActions CurrentPlan => _result?.CountPlannedActions() ?? PlannedActions.Empty;

    public void NotifyActionsChanged()
    {
        foreach (var row in Rows)
        {
            row.RefreshSubtreeAction();
        }

        UpdateSummary();
    }

    public void AskAgentAbout(SyncNodeViewModel node)
    {
        AskAgentRequested?.Invoke(ChatQuestion.ForSyncNode(node.RelativePath,
            node.Status,
            node.IsDirectory,
            node.LeftSizeText,
            node.RightSizeText,
            node.DiffReason));
    }

    public void ToggleExpand(DirectoryComparison dir)
    {
        if (!_collapsed.Remove(dir))
        {
            _collapsed.Add(dir);
        }

        RebuildRows();
    }

    public void ToggleGroup(string? key)
    {
        if (key is null)
        {
            _gitGroupExpanded = !_gitGroupExpanded;
        }
        else if (!_collapsedSubGroups.Remove(key))
        {
            _collapsedSubGroups.Add(key);
        }

        RebuildRows();
    }

    public void CollapseSubtree(DirectoryComparison dir)
    {
        SyncRowsProjector.AddCollapsed(_collapsed, dir);
        RebuildRows();
    }

    public void ExpandSubtree(DirectoryComparison dir)
    {
        SyncRowsProjector.RemoveCollapsed(_collapsed, dir);
        RebuildRows();
    }

    [RelayCommand]
    public void CollapseAll()
    {
        if (_result is null)
        {
            return;
        }

        SyncRowsProjector.CollapseAllDirectories(_collapsed, _result.Root);
        RebuildRows();
    }

    [RelayCommand]
    public void ExpandAll()
    {
        if (_result is null)
        {
            return;
        }

        _collapsed.Clear();
        RebuildRows();
    }

    public void ApplyToSubtree(DirectoryComparison dir, SyncAction action)
    {
        ApplyActionRecursive(dir, action);
        RebuildRows();
        UpdateSummary();
    }

    public async Task CompareContentAsync(FileComparison file)
    {
        if (_result is null)
        {
            return;
        }

        var leftPath = Path.Combine(_result.LeftPath, file.RelativePath);
        var rightPath = Path.Combine(_result.RightPath, file.RelativePath);

        FileDiffResult built;

        try
        {
            built = await Task.Run(() => BuildContentDiff(leftPath, rightPath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.ContentCompareFailed(ex, file.RelativePath);
            _dialogs.Error("Сравнение содержимого", ex.Message);
            return;
        }

        _logger.ContentCompareOpened(file.RelativePath, built.Added, built.Removed);

        var dialog = new FileDiffDialogViewModel(_settings, file, leftPath, rightPath, built.Lines, built.Added, built.Removed, built.Unavailable);
        await _dialogs.ShowAsync(dialog);
    }

    public void ApplyProfile(SyncProfile profile)
    {
        ApplyProfile(profile, null);
    }

    public void ApplyProfile(SyncProfile profile, ComparisonResult? comparison)
    {
        ClearComparison();
        LeftPath = profile.Left;
        RightPath = profile.Right;
        Exclusions = profile.Exclusions;
        SelectedModeIndex = Math.Clamp(profile.Mode, 0, ModeOrder.Length - 1);
        Mirror = profile.Mirror;
        SelectedWinnerIndex = SyncProfile.IndexOfWinner(profile.Winner);
        _activeProfileId = profile.Id;

        if (comparison is null)
        {
            Session.StatusCaption = $"Профиль применён: {profile.Name}.";
            return;
        }

        AdoptComparison(comparison);
        Session.StatusCaption = $"Профиль применён: {profile.Name}. Результат сравнения перенесён.";
    }

    internal static string AddGitExclusion(string exclusions)
    {
        var parts = exclusions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Any(static part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase))
            ? exclusions
            : string.IsNullOrWhiteSpace(exclusions)
                ? ".git"
                : $"{exclusions.TrimEnd()},.git";
    }

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

    internal Task CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteCompareAsync(cancellationToken);
    }

    internal Task<SyncRunResult?> SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteSyncAsync(false, cancellationToken);
    }

    internal TimeSpan LastSyncElapsed { get; private set; }

    internal SyncVerifyState LastVerifyState { get; private set; }

    internal ComparisonExportModel? BuildExportModel(int entryLimit)
    {
        return CaptureExportBuilder(entryLimit)?.Invoke();
    }

    internal Func<SyncPlanExportModel>? CapturePlanBuilder(int entryLimit)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var options = new ComparisonExportOptions(CurrentMode, CurrentWinner, Mirror, Exclusions.Trim());

        return () => SyncPlanExport.Build(result, options, AppInfo.Version, entryLimit);
    }

    internal Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var options = new ComparisonExportOptions(CurrentMode, CurrentWinner, Mirror, Exclusions.Trim());

        var git = Git.LeftState is null && Git.RightState is null
            ? null
            : new ComparisonExportGit(Git.LeftState, Git.RightState, Git.GitVerdictText);

        var lastSync = _lastReport is null
            ? null
            : new ComparisonExportSync(_lastReport.CopiedCount, _lastReport.DeletedCount, _lastReport.Errors, _lastReport.Mismatches);

        return () => ComparisonExport.Build(result, options, AppInfo.Version, entryLimit) with
        {
            Git = git,
            LastSync = lastSync,
        };
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles)
        {
            Profiles.Load();
        }
        else if (key == SettingsKeys.SyncGroupFolders && _result is not null && FlatView)
        {
            RebuildRows();
        }
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

    private static void ApplyActionRecursive(DirectoryComparison dir, SyncAction action)
    {
        if (dir.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly)
        {
            dir.Action = DirActionFor(dir, action);
        }

        foreach (var file in dir.Files)
        {
            if (file.Status != ComparisonStatus.Identical)
            {
                file.Action = action;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            ApplyActionRecursive(sub, action);
        }
    }

    private static SyncAction DirActionFor(DirectoryComparison dir, SyncAction requested)
    {
        return dir.Status == ComparisonStatus.LeftOnly
            ? requested switch
            {
                SyncAction.CopyToRight => SyncAction.CopyToRight,
                SyncAction.DeleteLeft => SyncAction.DeleteLeft,
                SyncAction.Skip => SyncAction.Skip,
                _ => dir.Action,
            }
            : requested switch
            {
                SyncAction.CopyToLeft => SyncAction.CopyToLeft,
                SyncAction.DeleteRight => SyncAction.DeleteRight,
                SyncAction.Skip => SyncAction.Skip,
                _ => dir.Action,
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

    private static FileDiffResult BuildContentDiff(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);

        if (!leftInfo.Exists && !rightInfo.Exists)
        {
            throw new InvalidOperationException($"Файл не найден ни с одной стороны: {leftPath}");
        }

        if (DiffUnavailable(leftInfo, out var reason) || DiffUnavailable(rightInfo, out reason))
        {
            return FileDiffResult.Unreadable(reason);
        }

        var lines = TextDiff.Compute(ReadLines(leftInfo), ReadLines(rightInfo));
        var added = lines.Count(static l => l.Kind == DiffLineKind.Added);
        var removed = lines.Count(static l => l.Kind == DiffLineKind.Removed);
        return new(lines, added, removed, null);

        static string[] ReadLines(FileInfo info)
        {
            return info.Exists ? File.ReadAllLines(info.FullName) : [];
        }
    }

    // TODO: бинарь определяем по NUL-байту; кодировку доверяем File.ReadAllLines (BOM → UTF-8)
    private static bool DiffUnavailable(FileInfo info, out string reason)
    {
        if (!info.Exists)
        {
            reason = string.Empty;
            return false;
        }

        if (info.Length > MaxDiffBytes)
        {
            reason = "Файл велик для построчного сравнения (> 5 МБ) – показано только сводное различие.";
            return true;
        }

        if (Array.IndexOf(File.ReadAllBytes(info.FullName), (byte)0) >= 0)
        {
            reason = "Файл выглядит двоичным – построчное сравнение недоступно, показано сводное различие.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool PathMissing(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path.Trim());
    }

    private static int CountGitDirectories(DirectoryComparison dir)
    {
        var count = 0;

        foreach (var sub in dir.SubDirectories)
        {
            if (string.Equals(sub.Name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                count++;
                continue;
            }

            count += CountGitDirectories(sub);
        }

        return count;
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

    private void AdoptComparison(ComparisonResult comparison)
    {
        _result = comparison;
        _dirSizeCache = SyncRowsProjector.BuildDirSizeCache(comparison.Root);
        _outcomes = [];
        _hashesCompared = false;
        comparison.ApplyMode(CurrentMode, Mirror, CurrentWinner);
        SyncRowsProjector.CollapseAllDirectories(_collapsed, comparison.Root);
        RebuildRows();
        UpdateSummary();
        SummaryText = "Результат сравнения перенесён со страницы «Обзор».";
        _ = ReadGitStateAsync();
    }

    private void HashModifiedFiles(DirectoryComparison dir, string leftBase, string rightBase, IProgress<OperationProgress> progress, ref int done, CancellationToken token)
    {
        foreach (var file in dir.Files.Where(static f => f.Status == ComparisonStatus.Modified))
        {
            token.ThrowIfCancellationRequested();

            var leftPath = Path.Combine(leftBase, file.RelativePath);
            var rightPath = Path.Combine(rightBase, file.RelativePath);

            try
            {
                file.LeftHash = FileHasher.ComputeHash(leftPath, token);
                file.RightHash = FileHasher.ComputeHash(rightPath, token);

                if (file.LeftHash == file.RightHash)
                {
                    file.Status = ComparisonStatus.Identical;
                    file.Action = SyncAction.Skip;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.HashFileFailed(ex, leftPath);
            }

            progress.Report(new(++done, file.RelativePath));
        }

        foreach (var sub in dir.SubDirectories)
        {
            HashModifiedFiles(sub, leftBase, rightBase, progress, ref done, token);
        }
    }

    private void WriteSyncLog(SyncReport report, bool interactive = true)
    {
        var origin = interactive ? string.Empty : " (запуск агентом через MCP)";

        try
        {
            SyncLog.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация{origin}: {report.SuccessCount} успешно, {report.Errors.Count} ошибок", report);
        }
        catch (Exception ex)
        {
            _logger.SyncLogWriteFailed(ex);
        }
    }

    private bool CanRun()
    {
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void BrowseLeft()
    {
        Browse(path => LeftPath = path);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void BrowseRight()
    {
        Browse(path => RightPath = path);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CompareAsync()
    {
        return ExecuteCompareAsync(CancellationToken.None);
    }

    private async Task ExecuteCompareAsync(CancellationToken external)
    {
        var left = LeftPath.Trim();
        var right = RightPath.Trim();

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            _dialogs.Warning("Сравнение", "Укажите обе директории.");
            return;
        }

        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            _dialogs.Warning("Сравнение", "Одна из директорий не существует.");
            return;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            _dialogs.Warning("Сравнение", "Каталоги совпадают или вложены друг в друга — синхронизация невозможна.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        _logger.CompareStarted(left, right);

        var request = new CompareDirectoriesRequest(left, right, Exclusions, CurrentMode, CurrentWinner, Mirror);

        var prepared = await Session.RunAsync("Сравнение каталогов:", (token, progress) =>
        {
            var compared = _compare.Execute(request, token, progress);
            return new ComparePreparation(compared, SyncRowsProjector.BuildDirSizeCache(compared.Root));
        }, external: external);

        stopwatch.Stop();

        if (prepared is null)
        {
            return;
        }

        _result = prepared.Result;
        _dirSizeCache = prepared.Sizes;
        _outcomes = [];
        _hashesCompared = false;
        SyncRowsProjector.CollapseAllDirectories(_collapsed, _result.Root);
        RebuildRows();
        UpdateSummary();
        SummaryText = $"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с";
        Session.StatusCaption = SummaryText;

        _logger.CompareFinished(_total, (long)stopwatch.Elapsed.TotalMilliseconds);
        RaiseProfileRun(_result, null, (long)stopwatch.Elapsed.TotalMilliseconds);

        await ReadGitStateAsync();
        await OfferToSkipGitAsync();
    }

    private async Task OfferToSkipGitAsync()
    {
        if (_result is null || _gitPromptDeclined)
        {
            return;
        }

        var gitFolders = CountGitDirectories(_result.Root);

        if (gitFolders == 0)
        {
            return;
        }

        var choice = _settings.GetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Ask);

        if (choice == GitFolderPromptChoice.Keep)
        {
            return;
        }

        if (choice == GitFolderPromptChoice.Ask)
        {
            var prompt = new GitFolderPromptViewModel(gitFolders);
            var skip = await _dialogs.ShowAsync(prompt);

            if (prompt.Choice != GitFolderPromptChoice.Ask)
            {
                _settings.SetEnum(SettingsKeys.SyncGitFolders, prompt.Choice);
            }

            if (!skip)
            {
                _gitPromptDeclined = true;
                return;
            }
        }

        var updatedExclusions = AddGitExclusion(Exclusions);

        if (string.Equals(updatedExclusions, Exclusions, StringComparison.Ordinal))
        {
            return;
        }

        _logger.SyncGitFoldersSkipped(gitFolders);
        Exclusions = updatedExclusions;
        await CompareAsync();
    }

    private Task ReadGitStateAsync()
    {
        return _result is null
            ? Task.CompletedTask
            : Git.ReadAsync(_result.LeftPath, _result.RightPath, CancellationToken.None);
    }

    private bool CanHash()
    {
        return !IsBusy && _result is not null;
    }

    [RelayCommand(CanExecute = nameof(CanHash))]
    private async Task HashAsync()
    {
        if (_result is null)
        {
            return;
        }

        var result = _result;
        var stopwatch = Stopwatch.StartNew();

        _logger.HashStarted();

        var sizes = await Session.RunAsync("Вычисление хешей:", (token, progress) =>
        {
            var done = 0;
            HashModifiedFiles(result.Root, result.LeftPath, result.RightPath, progress, ref done, token);
            return SyncRowsProjector.BuildDirSizeCache(result.Root);
        }, ModifiedCount, CancellationToken.None);

        stopwatch.Stop();

        if (sizes is null)
        {
            return;
        }

        _dirSizeCache = sizes;
        _outcomes = [];
        _hashesCompared = true;
        RebuildRows();
        UpdateSummary();
        SummaryText = $"Хеши вычислены за {stopwatch.Elapsed.TotalSeconds:F2} с";
        Session.StatusCaption = SummaryText;

        _logger.HashFinished((long)stopwatch.Elapsed.TotalMilliseconds);
    }

    private bool CanSync()
    {
        return !IsBusy && _result is not null && !HasPending && HasActionableChanges();
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (_result is null)
        {
            return;
        }

        if (_result.HasPendingResolution())
        {
            _dialogs.Warning("Неподтверждённые элементы", "Разрешите все неподтверждённые элементы перед синхронизацией.");
            return;
        }

        var hashes = new ConfirmChoice("Сверить хеши", ConfirmChoiceKind.Secondary);

        while (true)
        {
            if (_result is not { } current)
            {
                return;
            }

            var confirm = BuildSyncConfirmation(current, hashes);

            if (!await _dialogs.ShowAsync(confirm))
            {
                return;
            }

            if (!ReferenceEquals(confirm.Chosen, hashes))
            {
                break;
            }

            await HashCommand.ExecuteAsync(null);

            if (!HasActionableChanges() || _result is not { } hashed || hashed.HasPendingResolution())
            {
                return;
            }
        }

        await ExecuteSyncAsync(true, CancellationToken.None);
    }

    private ConfirmDialogViewModel BuildSyncConfirmation(ComparisonResult current, ConfirmChoice hashes)
    {
        var planned = CurrentPlan;
        var deletes = planned.Deletes + planned.DirDeletes;
        var offerHashes = planned.ModifiedCopies > 0 && !_hashesCompared;
        var lines = BuildPlanLines(planned, null, BuildReceivers(planned), CurrentMode == SyncMode.Bidirectional);

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

        return new($"Синхронизация {DirectionText()}", DirectionIconKind, lines, choices)
        {
            Summary = DescribePlanVolume(planned),
            Warning = deletes > 0 ? DescribeDeletionRecency(current) : null,
        };
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

    private static string? DescribeDeletionRecency(ComparisonResult result)
    {
        var (_, newest) = SyncFreshness.DeletionRecency(result.Root);

        return newest is { } when
            ? $"Новейшее из удаляемого изменено {SyncGitViewModel.FormatStamp(when)} ({SyncGitViewModel.FormatAge(when)}) – убедитесь, что зеркалите не более свежую папку."
            : null;
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

    private async Task<SyncRunResult?> ExecuteSyncAsync(bool interactive, CancellationToken external = default)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var planned = CurrentPlan;
        var stopwatch = Stopwatch.StartNew();

        _logger.SyncStarted(CurrentMode);

        var verify = Verify;

        var request = new ExecuteSyncRequest(result, SyncConflictPolicy.None, SyncDeleteUi.Interactive, verify);

        var report = await Session.RunAsync("Синхронизация:",
            (token, progress) => _sync.Execute(request, token, progress),
            planned.Total,
            external,
            planned.CopyBytes);

        stopwatch.Stop();

        if (report is null)
        {
            return null;
        }

        LastSyncElapsed = stopwatch.Elapsed;
        LastVerifyState = ResolveVerify(verify, report);

        _logger.SyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

        if (verify)
        {
            _logger.SyncVerified(report.Applied.Count, report.Mismatches.Count);
        }

        WriteSyncLog(report, interactive);
        _lastReport = report;
        _outcomes = SyncOutcomes.Build(result, report.Errors, report.Mismatches);
        RebuildRows();
        RefreshLedgerAfterSync();
        RaiseProfileRun(null, report, (long)stopwatch.Elapsed.TotalMilliseconds);
        await ReadGitStateAsync();

        var verifyText = DescribeVerify(verify, report);
        var volumeText = report.CopiedBytes > 0 ? $" Перенесено: {SizeFormatter.Format(report.CopiedBytes)}." : string.Empty;
        var rateText = SyncSessionViewModel.DescribeRate("Синхронизация", report, stopwatch.Elapsed);
        SummaryText = $"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с.{volumeText}{rateText} Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}";
        Session.StatusCaption = SummaryText;

        var toastVolume = report.CopiedBytes > 0 ? $" · {SizeFormatter.Format(report.CopiedBytes)}" : string.Empty;

        var syncToastMessage = report.Errors.Count > 0
            ? $"Синхронизация: применено {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}"
            : report.Mismatches.Count > 0
                ? $"Синхронизация: применено {report.SuccessCount:N0} · расхождений: {report.Mismatches.Count:N0}"
                : $"Синхронизация завершена: применено {report.SuccessCount:N0}{toastVolume}";

        var syncToastSeverity = report.Errors.Count > 0 ? StatusSeverity.Error
            : report.Mismatches.Count > 0 ? StatusSeverity.Warning
            : StatusSeverity.Success;

        _notifier.Notify(syncToastMessage, syncToastSeverity);

        if (interactive)
        {
            ShowSyncOutcome(report);
        }

        return new(report, stopwatch.Elapsed, LastVerifyState);
    }

    private void ShowSyncOutcome(SyncReport report)
    {
        if (report.Errors.Count > 0)
        {
            const int MaxShown = 20;
            var list = string.Join(Environment.NewLine, report.Errors.Take(MaxShown).Select(e => $"  {e.RelativePath}: {e.Message}"));

            if (report.Errors.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Errors.Count - MaxShown}";
            }

            _dialogs.Warning("Ошибки", $"Ошибки при синхронизации:{Environment.NewLine}{list}");
        }
        else if (report.Mismatches.Count > 0)
        {
            const int MaxShown = 20;
            var list = string.Join(Environment.NewLine, report.Mismatches.Take(MaxShown).Select(m => $"  {m.RelativePath}: {m.Reason}"));

            if (report.Mismatches.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Mismatches.Count - MaxShown}";
            }

            _dialogs.Warning("Расхождения после синхронизации", $"После применения проверка нашла расхождения:{Environment.NewLine}{list}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllToRight()
    {
        ResolveAll(SyncAction.CopyToRight);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllToLeft()
    {
        ResolveAll(SyncAction.CopyToLeft);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllSkip()
    {
        ResolveAll(SyncAction.Skip);
    }

    private void ResolveAll(SyncAction action)
    {
        if (_result is null)
        {
            return;
        }

        var count = _result.ResolveAllConflicts(action);
        RebuildRows();
        UpdateSummary();
        Session.StatusCaption = $"Разрешено элементов: {count}.";
    }

    private bool CanExport()
    {
        return !IsBusy && _result is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportComparison()
    {
        if (_result is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт сравнения",
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = BuildExportFileName(),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var model = BuildExportModel(ComparisonExport.DefaultEntryLimit)!;

            File.WriteAllText(dialog.FileName, ComparisonExport.ToJson(model));
            _logger.ComparisonExported(dialog.FileName, model.Entries.Count, model.OmittedEntries);

            var omitted = model.OmittedEntries > 0 ? $", пропущено {model.OmittedEntries:N0}" : string.Empty;
            Session.StatusCaption = $"Сравнение выгружено: {Path.GetFileName(dialog.FileName)}";
            _notifier.Notify($"Сравнение выгружено: записей {model.Entries.Count:N0}{omitted}", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.ComparisonExportFailed(ex, dialog.FileName);
            _dialogs.Error("Экспорт сравнения", ex.Message);
        }
    }

    private string BuildExportFileName()
    {
        var trimmed = LeftPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var raw = Path.GetFileName(trimmed);
        var name = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        return $"sync-compare-{(name.Length == 0 ? "root" : name)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    }

    [RelayCommand]
    private void CycleMode()
    {
        SelectedModeIndex = (SelectedModeIndex + 1) % ModeOrder.Length;
    }

    [RelayCommand]
    private void SwapPaths()
    {
        (LeftPath, RightPath) = (RightPath, LeftPath);
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        Persist(SettingsKeys.SyncMode, value.ToString());
        Profiles.MarkCurrent();
        OnPropertyChanged(nameof(DirectionIconKind));
        OnPropertyChanged(nameof(DirectionHint));
        OnPropertyChanged(nameof(MirrorApplicable));
        OnPropertyChanged(nameof(IsBidirectional));
        OnPropertyChanged(nameof(MirrorHint));
        OnPropertyChanged(nameof(MirrorDeletes));
        OnPropertyChanged(nameof(WinnerIsNewest));
        OnPropertyChanged(nameof(ShowConflictResolvers));
        ReapplyMode();
    }

    partial void OnSelectedWinnerIndexChanged(int value)
    {
        Persist(SettingsKeys.SyncWinner, value.ToString());
        Profiles.MarkCurrent();
        OnPropertyChanged(nameof(MirrorApplicable));
        OnPropertyChanged(nameof(MirrorHint));
        OnPropertyChanged(nameof(MirrorDeletes));
        OnPropertyChanged(nameof(WinnerIsNewest));
        OnPropertyChanged(nameof(ShowConflictResolvers));
        ReapplyMode();
    }

    partial void OnMirrorChanged(bool value)
    {
        Persist(SettingsKeys.SyncMirror, value ? "true" : "false");
        Profiles.MarkCurrent();
        OnPropertyChanged(nameof(MirrorHint));
        OnPropertyChanged(nameof(MirrorDeletes));
        ReapplyMode();
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IsBusy):
                OnPropertyChanged(nameof(IsBusy));
                Profiles.NotifyCanSaveChanged();
                CompareCommand.NotifyCanExecuteChanged();
                HashCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                BrowseLeftCommand.NotifyCanExecuteChanged();
                BrowseRightCommand.NotifyCanExecuteChanged();
                ExportComparisonCommand.NotifyCanExecuteChanged();
                break;

            case nameof(StatusCaption):
            case nameof(IsIndeterminate):
            case nameof(ProgressValue):
            case nameof(ProgressMax):
                OnPropertyChanged(e.PropertyName);
                break;
        }
    }

    private void ReapplyMode()
    {
        if (_result is null)
        {
            return;
        }

        _result.ApplyMode(CurrentMode, Mirror, CurrentWinner);
        RebuildRows();
        UpdateSummary();
    }

    partial void OnShowIdenticalChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowIdentical, value ? "true" : "false");

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnShowSizesChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowSizes, value ? "true" : "false");
    }

    partial void OnShowModifiedChanged(bool value)
    {
        Persist(SettingsKeys.SyncShowModified, value ? "true" : "false");
    }

    partial void OnBlankAbsentChanged(bool value)
    {
        Persist(SettingsKeys.SyncBlankAbsent, value ? "true" : "false");

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnVerifyChanged(bool value)
    {
        Persist(SettingsKeys.SyncVerify, value ? "true" : "false");
    }

    partial void OnHideAppliedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowApplied));
        Persist(SettingsKeys.SyncHideApplied, value ? "true" : "false");

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnFlatViewChanged(bool value)
    {
        Persist(SettingsKeys.SyncFlatView, value ? "true" : "false");

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnRowSortChanged(SyncSortField value)
    {
        Persist(SettingsKeys.SyncFlatSort, value.ToString());
        NotifySortChanged();

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnRowSortDescendingChanged(bool value)
    {
        Persist(SettingsKeys.SyncFlatSortDesc, value ? "true" : "false");
        NotifySortChanged();

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchTextEmpty));

        if (_result is not null)
        {
            RebuildRows();
        }
    }

    partial void OnLeftPathChanged(string value)
    {
        Persist(SettingsKeys.SyncLeft, value);
        LeftPathInvalid = PathMissing(value);
        _activeProfileId = null;
        Profiles.MarkCurrent();
        DiscardComparisonIfPathChanged();
    }

    partial void OnRightPathChanged(string value)
    {
        Persist(SettingsKeys.SyncRight, value);
        RightPathInvalid = PathMissing(value);
        _activeProfileId = null;
        Profiles.MarkCurrent();
        DiscardComparisonIfPathChanged();
    }

    private void RaiseProfileRun(ComparisonResult? comparison, SyncReport? report, long elapsedMs)
    {
        if (_activeProfileId is { } id)
        {
            ProfileRunCompleted?.Invoke(new(id, comparison, report, elapsedMs));
        }
    }

    private void DiscardComparisonIfPathChanged()
    {
        if (_result is null)
        {
            return;
        }

        if (!string.Equals(LeftPath.Trim(), _result.LeftPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(RightPath.Trim(), _result.RightPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearComparison();
        }
    }

    partial void OnExclusionsChanged(string value)
    {
        OnPropertyChanged(nameof(ExclusionsEmpty));
        Persist(SettingsKeys.SyncExclusions, value);
        Profiles.MarkCurrent();
    }

    private void Browse(Action<string> assign)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите каталог",
        };

        if (dialog.ShowDialog() == true)
        {
            assign(dialog.FolderName);
        }
    }

    private void ClearComparison()
    {
        _result = null;
        _lastReport = null;
        _dirSizeCache = null;
        _outcomes = [];
        _hashesCompared = false;
        _collapsed.Clear();
        Rows.ReplaceAll([]);
        Git.Clear();
        UpdateSummary();
    }

    private void RebuildRows()
    {
        var result = _result;

        if (result is null)
        {
            Rows.ReplaceAll([]);
            return;
        }

        var request = new SyncRowsRequest
        {
            Result = result,
            FlatView = FlatView,
            SearchText = SearchText,
            ShowIdentical = ShowIdentical,
            HideApplied = HideApplied,
            RowSort = RowSort,
            RowSortDescending = RowSortDescending,
            Outcomes = _outcomes,
            DirSizeCache = _dirSizeCache,
            Collapsed = _collapsed,
            CollapsedSubGroups = _collapsedSubGroups,
            GitGroupExpanded = _gitGroupExpanded,
            GroupFolders = Operations.GroupFolders,
        };

        Rows.ReplaceAll(SyncRowsProjector.Build(request, this));
    }

    private void UpdateSummary()
    {
        if (_result is null)
        {
            SummaryText = "Сравнение не выполнялось.";
            _stats = NewZeroStats();
            _dirStats = NewZeroStats();
            _freshness = default;
            _plan = PlannedActions.Empty;
            _total = 0;
            NotifyLedgerChanged();
            HasPending = false;
            SyncCommand.NotifyCanExecuteChanged();
            HashCommand.NotifyCanExecuteChanged();
            ExportComparisonCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(SyncIsPrimary));
            return;
        }

        var stats = _result.GetStatistics();

        SummaryText =
            $"Одинаковых: {stats[ComparisonStatus.Identical]}, "
            + $"только слева: {stats[ComparisonStatus.LeftOnly]}, "
            + $"только справа: {stats[ComparisonStatus.RightOnly]}, "
            + $"изменённых: {stats[ComparisonStatus.Modified]}, "
            + $"конфликтов: {stats[ComparisonStatus.Conflict]}";

        _stats = stats;
        _dirStats = _result.GetDirectoryStatistics();
        _freshness = SyncFreshness.Compute(_result.Root);
        _plan = _result.CountPlannedActions();
        _total = stats.Values.Sum();
        NotifyLedgerChanged();

        HasPending = _result.HasPendingResolution();
        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
        ExportComparisonCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(SyncIsPrimary));
    }

    private void RefreshLedgerAfterSync()
    {
        if (_result is null)
        {
            return;
        }

        var (files, dirs) = CountRemaining(_result.Root, _outcomes);
        _stats = files;
        _dirStats = dirs;
        _total = files.Values.Sum();
        _freshness = default;
        _plan = PlannedActions.Empty;
        NotifyLedgerChanged();
    }

    private string BuildSyncCommandHint()
    {
        if (_result is null)
        {
            return "Сначала выполните сравнение.";
        }

        var lines = BuildPlanLines(_plan, DirectionText(), BuildReceivers(_plan), CurrentMode == SyncMode.Bidirectional);

        if (SyncIsDestructive)
        {
            lines.Add(new ConfirmGapLine());
            lines.Add(new ConfirmTextLine("Удаление идёт в корзину, копирование перезаписывает файлы на приёмнике."));
        }

        return ConfirmDialogViewModel.AsText(lines);
    }

    private string DirectionText()
    {
        return CurrentMode switch
        {
            SyncMode.RightToLeft => "справа налево",
            SyncMode.Bidirectional => "в обе стороны",
            _ => "слева направо",
        };
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

    private double Fraction(ComparisonStatus status)
    {
        var differing = DifferingTotal;

        return differing > 0 ? (double)_stats[status] / differing : 0;
    }

    [RelayCommand]
    private void SortByColumn(SyncSortField field)
    {
        if (RowSort == field)
        {
            RowSortDescending = !RowSortDescending;
            return;
        }

        RowSort = field;
        RowSortDescending = field is SyncSortField.Size or SyncSortField.Modified;
    }

    private void NotifySortChanged()
    {
        OnPropertyChanged(nameof(SortByPath));
        OnPropertyChanged(nameof(SortBySize));
        OnPropertyChanged(nameof(SortByModified));
        OnPropertyChanged(nameof(SortDirectionIconKind));
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

    private bool HasActionableChanges()
    {
        return _result is not null && _result.CountPlannedActions().Total > 0;
    }

    private SyncProfile BuildCurrentProfile(string id, string name, SyncProfile? existing = null)
    {
        return new()
        {
            Id = id,
            Name = name,
            Left = LeftPath.Trim(),
            Right = RightPath.Trim(),
            Mode = SelectedModeIndex,
            Mirror = Mirror,
            Winner = CurrentWinner,
            Exclusions = Exclusions.Trim(),
            Interval = existing?.Interval ?? ScheduleInterval.Daily,
            Time = existing?.Time ?? "03:00",
            Enabled = existing?.Enabled ?? false,
        };
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        LeftPath = _settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty;
        RightPath = _settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty;
        Exclusions = _settings.GetStringValue(SettingsKeys.SyncExclusions) ?? Operations.DefaultExclusions;

        SelectedModeIndex = Math.Clamp(_settings.GetInt(SettingsKeys.SyncMode), 0, ModeOrder.Length - 1);
        Mirror = _settings.GetBool(SettingsKeys.SyncMirror);
        SelectedWinnerIndex = Math.Clamp(_settings.GetInt(SettingsKeys.SyncWinner), 0, WinnerOrder.Length - 1);
        ShowIdentical = _settings.GetBool(SettingsKeys.SyncShowIdentical);
        ShowSizes = _settings.GetBool(SettingsKeys.SyncShowSizes, AppDefaults.SyncShowSizesDefault);
        ShowModified = _settings.GetBool(SettingsKeys.SyncShowModified);
        BlankAbsent = _settings.GetBool(SettingsKeys.SyncBlankAbsent);
        Verify = _settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);
        HideApplied = _settings.GetBool(SettingsKeys.SyncHideApplied);
        FlatView = _settings.GetBool(SettingsKeys.SyncFlatView);
        RowSort = _settings.GetEnum(SettingsKeys.SyncFlatSort, AppDefaults.SyncFlatSortDefault);
        RowSortDescending = _settings.GetBool(SettingsKeys.SyncFlatSortDesc);

        _suppressPersist = false;
    }

    private void Persist(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(key, value);
    }

    internal SyncMode CurrentMode => ModeOrder[Math.Clamp(SelectedModeIndex, 0, ModeOrder.Length - 1)];

    internal SyncWinner CurrentWinner => WinnerOrder[Math.Clamp(SelectedWinnerIndex, 0, WinnerOrder.Length - 1)];

    private sealed record ComparePreparation(ComparisonResult Result, Dictionary<DirectoryComparison, (long Left, long Right)> Sizes);

    private sealed record FileDiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, string? Unavailable)
    {
        public static FileDiffResult Unreadable(string reason)
        {
            return new([], 0, 0, reason);
        }
    }
}
