using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Core.Git;
using SpaceSnoop.Wpf.Diff;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;
    private static readonly SyncMode[] ModeOrder = [SyncMode.LeftToRight, SyncMode.RightToLeft, SyncMode.Bidirectional];
    private static readonly SyncWinner[] WinnerOrder = [SyncWinner.Newest, SyncWinner.Left, SyncWinner.Right];
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SyncViewModel> _logger;
    private readonly ILogger<SyncEngine> _engineLogger;
    private readonly ILogger<DirectoryComparer> _comparerLogger;
    private readonly ToastNotifier _notifier;
    private readonly AgentPreferences _agent;
    private readonly GitService _git = new();
    private readonly HashSet<DirectoryComparison> _collapsed = [];
    private readonly HashSet<string> _collapsedSubGroups = new(StringComparer.OrdinalIgnoreCase);

    private ComparisonResult? _result;
    private SyncReport? _lastReport;
    private GitRepoState? _leftGit;
    private GitRepoState? _rightGit;
    private bool _gitHistoryLoaded;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private CancellationTokenSource? _cts;
    private string? _activeProfileId;
    private bool _suppressPersist;
    private bool _gitPromptDeclined;
    private bool _gitGroupExpanded;
    private bool _isIndeterminate = true;
    private double _progressValue;
    private double _progressMax = 1;
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
    private int _gitHistoryCount = AppDefaults.GitHistoryCountDefault;

    [ObservableProperty]
    private bool _hideApplied;

    [ObservableProperty]
    private bool _flatView;

    [ObservableProperty]
    private SyncFlatSortField _flatSort = AppDefaults.SyncFlatSortDefault;

    [ObservableProperty]
    private bool _flatSortDescending;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    [NotifyCanExecuteChangedFor(nameof(HashCommand))]
    [NotifyCanExecuteChangedFor(nameof(SyncCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportComparisonCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusCaption;

    [ObservableProperty]
    private string? _progressDetail;

    [ObservableProperty]
    private string _summaryText = "Сравнение не выполнялось.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllSkipCommand))]
    private bool _hasPending;

    public SyncViewModel(ISettingsStore settings, IDialogService dialogs, OperationPreferences operations, AgentPreferences agent, ILogger<SyncViewModel> logger, ILogger<SyncEngine> engineLogger, ILogger<DirectoryComparer> comparerLogger, ToastNotifier notifier)
    {
        _settings = settings;
        _dialogs = dialogs;
        Operations = operations;
        _agent = agent;
        _logger = logger;
        _engineLogger = engineLogger;
        _comparerLogger = comparerLogger;
        _notifier = notifier;
        Profiles = new(settings, dialogs, BuildCurrentProfile, ApplyProfile, () => !IsBusy, message => StatusCaption = message);
        LoadSettings();
        Profiles.Load();
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<string>? AskAgentRequested;

    public event Action<SyncProfileRun>? ProfileRunCompleted;

    public static int[] GitHistoryCounts { get; } = [4, 8, 16, 32];

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

    public double IdenticalFraction => Fraction(ComparisonStatus.Identical);

    public double LeftOnlyFraction => Fraction(ComparisonStatus.LeftOnly);

    public double RightOnlyFraction => Fraction(ComparisonStatus.RightOnly);

    public double ModifiedFraction => Fraction(ComparisonStatus.Modified);

    public double ConflictFraction => Fraction(ComparisonStatus.Conflict);

    public bool HasPlanVolume => _plan.CopyBytes > 0 || _plan.DeleteBytes > 0;

    public bool HasPlanCopy => _plan.CopyBytes > 0;

    public bool HasPlanTrash => _plan.DeleteBytes > 0;

    public string PlanCopyText => SizeFormatter.Format(_plan.CopyBytes);

    public string PlanTrashText => SizeFormatter.Format(_plan.DeleteBytes);

    public string PlanVolumeHint => string.Join(Environment.NewLine, BuildPlanLines(_plan, null, BuildReceivers(_plan)));

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
        NewerSide.Left => "СЛЕВА",
        NewerSide.Right => "СПРАВА",
        NewerSide.Tie => "ПОРОВНУ",
        _ => string.Empty,
    };

    public string NewerBadgeTooltip => GitDecidesNewer
        ? $"Свежее по коммитам git: слева {FormatStamp(_leftGit?.CommittedAt?.LocalDateTime)}, справа {FormatStamp(_rightGit?.CommittedAt?.LocalDateTime)}."
        : $"Свежее по новейшему изменённому файлу: слева {FormatStamp(_freshness.LeftChangedMax)}, справа {FormatStamp(_freshness.RightChangedMax)}.{Environment.NewLine}"
          + $"Изменённых новее: слева {_freshness.LeftNewer:N0}, справа {_freshness.RightNewer:N0}.";

    public bool HasGit => _leftGit is not null || _rightGit is not null;

    public bool LeftIsRepo => _leftGit is not null;

    public bool RightIsRepo => _rightGit is not null;

    public string LeftGitBranch => FormatBranch(_leftGit);

    public string RightGitBranch => FormatBranch(_rightGit);

    public string LeftGitHead => FormatHead(_leftGit);

    public string RightGitHead => FormatHead(_rightGit);

    public string LeftGitDirty => FormatDirty(_leftGit);

    public string RightGitDirty => FormatDirty(_rightGit);

    public bool LeftGitIsDirty => _leftGit?.IsDirty == true;

    public bool RightGitIsDirty => _rightGit?.IsDirty == true;

    public string LeftGitUpstream => FormatUpstream(_leftGit);

    public string RightGitUpstream => FormatUpstream(_rightGit);

    public bool GitInSync =>
        _leftGit is not null
        && _rightGit is not null
        && _leftGit.HasCommits
        && _rightGit.HasCommits
        && string.Equals(_leftGit.Oid, _rightGit.Oid, StringComparison.OrdinalIgnoreCase)
        && !_leftGit.IsDirty
        && !_rightGit.IsDirty;

    public PackIconLucideKind GitVerdictIconKind
    {
        get
        {
            if (GitInSync)
            {
                return PackIconLucideKind.Check;
            }

            return GitNewerSign switch
            {
                < 0 => PackIconLucideKind.ArrowLeft,
                > 0 => PackIconLucideKind.ArrowRight,
                _ => PackIconLucideKind.GitCompareArrows,
            };
        }
    }

    public bool GitShowsNewer => GitNewerSign != 0;

    public string GitVerdictText
    {
        get
        {
            if (_leftGit is null || _rightGit is null)
            {
                return "одна сторона не репозиторий";
            }

            if (!_leftGit.HasCommits || !_rightGit.HasCommits)
            {
                return "нет коммитов";
            }

            if (!string.Equals(_leftGit.Oid, _rightGit.Oid, StringComparison.OrdinalIgnoreCase))
            {
                var newer = DescribeNewer(_leftGit.CommittedAt, _rightGit.CommittedAt);

                return newer.Length == 0 ? "разные коммиты" : $"разные коммиты, {newer}";
            }

            return _leftGit.IsDirty || _rightGit.IsDirty ? "тот же коммит, есть изменения" : "синхронны";
        }
    }

    public IReadOnlyList<GitCommit> LeftGitLog { get; private set; } = [];

    public IReadOnlyList<GitCommit> RightGitLog { get; private set; } = [];

    public bool GitHistoryExpanded { get; private set; }

    public PackIconLucideKind GitHistoryIconKind => GitHistoryExpanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;

    public bool LeftGitLogEmpty => _gitHistoryLoaded && LeftGitLog.Count == 0;

    public bool RightGitLogEmpty => _gitHistoryLoaded && RightGitLog.Count == 0;

    public string LeftGitLogEmptyText => _leftGit is null ? "не репозиторий" : "нет коммитов";

    public string RightGitLogEmptyText => _rightGit is null ? "не репозиторий" : "нет коммитов";

    public string? GitTooltip
    {
        get
        {
            if (_leftGit is not { HasCommits: true } left
                || _rightGit is not { HasCommits: true } right
                || string.Equals(left.Oid, right.Oid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"Коммит слева: {FormatStamp(left.CommittedAt?.LocalDateTime)}, справа: {FormatStamp(right.CommittedAt?.LocalDateTime)}.{Environment.NewLine}"
                   + "«Новее» – по дате коммита, не по истории веток.";
        }
    }

    public bool ExclusionsEmpty => string.IsNullOrWhiteSpace(Exclusions);

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public string CompositionHint =>
        $"Файлы: только слева {LeftOnlyCount:N0}, только справа {RightOnlyCount:N0}, изменены {ModifiedCount:N0}, конфликты {ConflictCount:N0}, одинаковые {IdenticalCount:N0}.{Environment.NewLine}"
        + $"Каталоги: только слева {LeftOnlyDirCount:N0}, только справа {RightOnlyDirCount:N0}, изменены {ModifiedDirCount:N0}, одинаковые {IdenticalDirCount:N0}.";

    public bool ShowApplied
    {
        get => !HideApplied;
        set => HideApplied = !value;
    }

    public string PageTitle => "Синхронизация";

    public string PageDescription => "Сравнение и синхронизация двух каталогов.";

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double ProgressValue
    {
        get => _progressValue;
        private set => SetProperty(ref _progressValue, value);
    }

    public double ProgressMax
    {
        get => _progressMax;
        private set => SetProperty(ref _progressMax, value);
    }

    public ICommand CancelCommand => CancelOperationCommand;

    private NewerSide EffectiveNewerSide => CombineNewer(_freshness.Verdict, GitInSync, GitNewerSign);

    private bool GitDecidesNewer => GitInSync || GitNewerSign != 0;

    private int GitNewerSign
    {
        get
        {
            if (_leftGit is not { HasCommits: true } left
                || _rightGit is not { HasCommits: true } right
                || string.Equals(left.Oid, right.Oid, StringComparison.OrdinalIgnoreCase)
                || left.CommittedAt is not { } l
                || right.CommittedAt is not { } r
                || l == r)
            {
                return 0;
            }

            return l > r ? -1 : 1;
        }
    }

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
        AddCollapsed(dir);
        RebuildRows();
    }

    public void ExpandSubtree(DirectoryComparison dir)
    {
        RemoveCollapsed(dir);
        RebuildRows();
    }

    [RelayCommand]
    public void CollapseAll()
    {
        if (_result is null)
        {
            return;
        }

        CollapseAllDirectories(_result.Root);
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
            built = await Task.Run(() => BuildContentDiff(leftPath, rightPath));
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
            StatusCaption = $"Профиль применён: {profile.Name}.";
            return;
        }

        AdoptComparison(comparison);
        StatusCaption = $"Профиль применён: {profile.Name}. Результат сравнения перенесён.";
    }

    internal static IEnumerable<FileComparison> SortFlatFiles(IEnumerable<FileComparison> files, SyncFlatSortField field, bool descending)
    {
        return field switch
        {
            SyncFlatSortField.Size => descending
                ? files.OrderByDescending(FileSize)
                : files.OrderBy(FileSize),
            SyncFlatSortField.Status => descending
                ? files.OrderByDescending(file => (int)file.Status).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(file => (int)file.Status).ThenBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase),
            _ => descending
                ? files.OrderByDescending(file => file.RelativePath, StringComparer.OrdinalIgnoreCase)
                : files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase),
        };

        static long FileSize(FileComparison file)
        {
            return Math.Max(file.LeftSize ?? 0, file.RightSize ?? 0);
        }
    }

    internal static IEnumerable<DirectoryComparison> CollectEmptyDirs(DirectoryComparison dir, bool hideApplied, IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (sub.Status is ComparisonStatus.LeftOnly or ComparisonStatus.RightOnly && SubtreeHasNoFiles(sub))
            {
                if (!hideApplied || outcomes.GetValueOrDefault(sub) != SyncOutcome.Applied)
                {
                    yield return sub;
                }

                continue;
            }

            foreach (var nested in CollectEmptyDirs(sub, hideApplied, outcomes))
            {
                yield return nested;
            }
        }

        static bool SubtreeHasNoFiles(DirectoryComparison node)
        {
            return node.Files.Count == 0 && node.SubDirectories.All(SubtreeHasNoFiles);
        }
    }

    internal static IEnumerable<FileComparison> CollectVisibleFiles(DirectoryComparison dir, bool showIdentical, bool hideApplied, IReadOnlyDictionary<object, SyncOutcome> outcomes)
    {
        foreach (var sub in dir.SubDirectories)
        {
            foreach (var file in CollectVisibleFiles(sub, showIdentical, hideApplied, outcomes))
            {
                yield return file;
            }
        }

        foreach (var file in dir.Files)
        {
            if (!showIdentical && file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            if (hideApplied && outcomes.GetValueOrDefault(file) == SyncOutcome.Applied)
            {
                continue;
            }

            yield return file;
        }
    }

    internal static string[] ParseGroupFolders(string folders)
    {
        return folders.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    internal static string? GroupedKey(string relativePath, IReadOnlyCollection<string> folders)
    {
        if (folders.Count == 0)
        {
            return null;
        }

        foreach (var segment in relativePath.Split('/', '\\'))
        {
            foreach (var folder in folders)
            {
                if (string.Equals(segment, folder, StringComparison.OrdinalIgnoreCase))
                {
                    return folder;
                }
            }
        }

        return null;
    }

    internal static bool IsGroupedPath(string relativePath, IReadOnlyCollection<string> folders)
    {
        return GroupedKey(relativePath, folders) is not null;
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

    // TODO: «новее» по дате коммита, не по предкам; ancestry-вердикт требует общего хранилища объектов (cross-repo merge-base)
    internal static string DescribeNewer(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is not { } l || right is not { } r || l == r)
        {
            return string.Empty;
        }

        var side = l > r ? "слева новее" : "справа новее";

        return $"{side} на {FormatAge((l - r).Duration())}";
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

    internal static List<string> BuildPlanLines(PlannedActions planned, string? direction, IReadOnlyList<PlanReceiver> receivers)
    {
        var lines = new List<string>();

        if (direction is not null)
        {
            lines.Add($"Направление: {direction}.");
            lines.Add(string.Empty);
        }

        if (planned.Total == 0)
        {
            lines.Add("Изменений нет.");
            return lines;
        }

        if (planned.Copies > 0)
        {
            lines.Add($"Скопировать файлов: {planned.Copies:N0} ({SizeFormatter.Format(planned.CopyBytes)})");

            if (planned.ModifiedCopies > 0)
            {
                lines.Add($"    – новых: {planned.NewCopies:N0} ({SizeFormatter.Format(planned.NewCopyBytes)})");
                lines.Add($"    – изменённых: {planned.ModifiedCopies:N0} ({SizeFormatter.Format(planned.ModifiedCopyBytes)})");
            }
        }

        if (planned.DirCopies > 0)
        {
            lines.Add($"Создать каталогов: {planned.DirCopies:N0}");
        }

        if (planned.Deletes > 0)
        {
            lines.Add($"Удалить файлов в корзину: {planned.Deletes:N0} ({SizeFormatter.Format(planned.DeleteFileBytes)})");
        }

        if (planned.DirDeletes > 0)
        {
            lines.Add($"Удалить каталогов в корзину: {planned.DirDeletes:N0} ({SizeFormatter.Format(planned.DeleteDirBytes)})");
        }

        var space = DescribeReceivers(receivers);

        if (space.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(space);
        }

        if (planned.DeleteBytes > 0)
        {
            lines.Add("Удалённое уходит в корзину – место освободится после её очистки.");
        }

        return lines;
    }

    internal Task CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteCompareAsync(cancellationToken);
    }

    internal Task<SyncReport?> SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteSyncAsync(false, cancellationToken);
    }

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

        var git = _leftGit is null && _rightGit is null
            ? null
            : new ComparisonExportGit(_leftGit, _rightGit, GitVerdictText);

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

    private static Dictionary<DirectoryComparison, (long Left, long Right)> BuildDirSizeCache(DirectoryComparison root)
    {
        var cache = new Dictionary<DirectoryComparison, (long Left, long Right)>();
        Accumulate(root, cache);
        return cache;
    }

    private static (long Left, long Right) Accumulate(DirectoryComparison dir, Dictionary<DirectoryComparison, (long Left, long Right)> cache)
    {
        long left = 0;
        long right = 0;

        foreach (var file in dir.Files)
        {
            if (file.LeftSize.HasValue)
            {
                left += file.LeftSize.Value;
            }

            if (file.RightSize.HasValue)
            {
                right += file.RightSize.Value;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            var (subLeft, subRight) = Accumulate(sub, cache);
            left += subLeft;
            right += subRight;
        }

        cache[dir] = (left, right);
        return (left, right);
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

    private static string FormatStamp(DateTime? value)
    {
        return value is { } stamp ? stamp.ToString("yyyy-MM-dd HH:mm") : "–";
    }

    private static string FormatBranch(GitRepoState? git)
    {
        return git is null ? string.Empty : git.IsDetached ? "detached" : git.Branch;
    }

    private static string FormatHead(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        if (!git.HasCommits)
        {
            return "нет коммитов";
        }

        return string.IsNullOrEmpty(git.Subject) ? git.ShortHash : $"{git.ShortHash} · {git.Subject}";
    }

    private static string FormatDirty(GitRepoState? git)
    {
        return git is null ? string.Empty : git.IsDirty ? $"{git.DirtyCount} изм." : "чисто";
    }

    private static string FormatUpstream(GitRepoState? git)
    {
        if (git is null || !git.HasUpstream)
        {
            return string.Empty;
        }

        var parts = new List<string>(2);

        if (git.Ahead > 0)
        {
            parts.Add($"↑{git.Ahead}");
        }

        if (git.Behind > 0)
        {
            parts.Add($"↓{git.Behind}");
        }

        return string.Join(" ", parts);
    }

    private static string FormatAge(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} дн.";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} ч.";
        }

        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} мин." : "<1 мин.";
    }

    private static IEnumerable<T> FilterBySearch<T>(IEnumerable<T> items, string search, Func<T, string> path)
    {
        return search.Length == 0
            ? items
            : items.Where(item => path(item).Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> DescribeReceivers(IReadOnlyList<PlanReceiver> receivers)
    {
        var lines = new List<string>();

        foreach (var receiver in receivers)
        {
            if (receiver.Required <= 0)
            {
                continue;
            }

            var required = SizeFormatter.Format(receiver.Required);

            if (receiver.Free is not { } free)
            {
                lines.Add($"Приёмник {receiver.Path}: потребуется ≈{required}, свободное место неизвестно.");
                continue;
            }

            lines.Add(free >= receiver.Required
                ? $"Приёмник {receiver.Path}: потребуется ≈{required}, свободно {SizeFormatter.Format(free)}."
                : $"Внимание: на {receiver.Path} не хватает ≈{SizeFormatter.Format(receiver.Required - free)} – потребуется ≈{required}, свободно {SizeFormatter.Format(free)}.");
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

    [RelayCommand]
    private async Task ToggleGitHistoryAsync()
    {
        GitHistoryExpanded = !GitHistoryExpanded;
        OnPropertyChanged(nameof(GitHistoryExpanded));
        OnPropertyChanged(nameof(GitHistoryIconKind));

        if (GitHistoryExpanded && !_gitHistoryLoaded)
        {
            await LoadGitHistoryAsync();
        }
    }

    private void AdoptComparison(ComparisonResult comparison)
    {
        _result = comparison;
        _dirSizeCache = BuildDirSizeCache(comparison.Root);
        _outcomes = [];
        comparison.ApplyMode(CurrentMode, Mirror, CurrentWinner);
        CollapseAllDirectories(comparison.Root);
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

        var filter = new ExclusionFilter(Exclusions);
        var stopwatch = Stopwatch.StartNew();

        _logger.CompareStarted(left, right);

        var mode = CurrentMode;
        var mirror = Mirror;
        var winner = CurrentWinner;

        var prepared = await RunAsync("Сравнение каталогов:", (token, progress) =>
        {
            var comparer = new DirectoryComparer(filter, _comparerLogger);
            var compared = comparer.Compare(left, right, token, progress);
            compared.ApplyMode(mode, mirror, winner);
            return new ComparePreparation(compared, BuildDirSizeCache(compared.Root));
        }, external: external);

        stopwatch.Stop();

        if (prepared is null)
        {
            return;
        }

        _result = prepared.Result;
        _dirSizeCache = prepared.Sizes;
        _outcomes = [];
        CollapseAllDirectories(_result.Root);
        RebuildRows();
        UpdateSummary();
        SummaryText = $"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с";
        StatusCaption = SummaryText;

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

    private async Task ReadGitStateAsync()
    {
        if (_result is null)
        {
            return;
        }

        var left = _result.LeftPath;
        var right = _result.RightPath;

        try
        {
            (_leftGit, _rightGit) = await Task.Run(async () => (await _git.ReadAsync(left), await _git.ReadAsync(right)));
        }
        catch (Exception ex)
        {
            _logger.GitStateFailed(ex.Unwrap());
            ClearGit();
            return;
        }

        if (_leftGit is not null || _rightGit is not null)
        {
            _logger.GitStateRead(FormatBranch(_leftGit), FormatBranch(_rightGit));
        }

        ResetGitHistory();
        NotifyGitChanged();

        if (GitHistoryExpanded)
        {
            await LoadGitHistoryAsync();
        }
    }

    private async Task LoadGitHistoryAsync()
    {
        if (_result is null)
        {
            return;
        }

        var left = _result.LeftPath;
        var right = _result.RightPath;
        var count = GitHistoryCount;

        try
        {
            (LeftGitLog, RightGitLog) = await Task.Run(async () =>
                (await _git.ReadHistoryAsync(left, count), await _git.ReadHistoryAsync(right, count)));
        }
        catch (Exception ex)
        {
            _logger.GitStateFailed(ex.Unwrap());
            LeftGitLog = [];
            RightGitLog = [];
        }

        _gitHistoryLoaded = true;
        NotifyGitChanged();
    }

    private void ResetGitHistory()
    {
        LeftGitLog = [];
        RightGitLog = [];
        _gitHistoryLoaded = false;
    }

    private void ClearGit()
    {
        _leftGit = null;
        _rightGit = null;
        GitHistoryExpanded = false;
        ResetGitHistory();
        NotifyGitChanged();
    }

    private void NotifyGitChanged()
    {
        OnPropertyChanged(nameof(HasGit));
        OnPropertyChanged(nameof(LeftIsRepo));
        OnPropertyChanged(nameof(RightIsRepo));
        OnPropertyChanged(nameof(LeftGitBranch));
        OnPropertyChanged(nameof(RightGitBranch));
        OnPropertyChanged(nameof(LeftGitHead));
        OnPropertyChanged(nameof(RightGitHead));
        OnPropertyChanged(nameof(LeftGitDirty));
        OnPropertyChanged(nameof(RightGitDirty));
        OnPropertyChanged(nameof(LeftGitIsDirty));
        OnPropertyChanged(nameof(RightGitIsDirty));
        OnPropertyChanged(nameof(LeftGitUpstream));
        OnPropertyChanged(nameof(RightGitUpstream));
        OnPropertyChanged(nameof(GitInSync));
        OnPropertyChanged(nameof(GitShowsNewer));
        OnPropertyChanged(nameof(GitVerdictIconKind));
        OnPropertyChanged(nameof(GitVerdictText));
        OnPropertyChanged(nameof(GitTooltip));
        OnPropertyChanged(nameof(LeftGitLog));
        OnPropertyChanged(nameof(RightGitLog));
        OnPropertyChanged(nameof(GitHistoryExpanded));
        OnPropertyChanged(nameof(GitHistoryIconKind));
        OnPropertyChanged(nameof(LeftGitLogEmpty));
        OnPropertyChanged(nameof(RightGitLogEmpty));
        OnPropertyChanged(nameof(LeftGitLogEmptyText));
        OnPropertyChanged(nameof(RightGitLogEmptyText));
        NotifyNewerBadgeChanged();
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

        var sizes = await RunAsync("Вычисление хешей:", (token, progress) =>
        {
            var done = 0;
            HashModifiedFiles(result.Root, result.LeftPath, result.RightPath, progress, ref done, token);
            return BuildDirSizeCache(result.Root);
        }, ModifiedCount);

        stopwatch.Stop();

        if (sizes is null)
        {
            return;
        }

        _dirSizeCache = sizes;
        _outcomes = [];
        RebuildRows();
        UpdateSummary();
        SummaryText = $"Хеши вычислены за {stopwatch.Elapsed.TotalSeconds:F2} с";
        StatusCaption = SummaryText;

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

        var planned = CurrentPlan;
        var lines = BuildPlanLines(planned, DirectionText(), BuildReceivers(planned));

        if (planned.ModifiedCopies > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Изменённые отличаются размером или датой – кнопка «Хеши» сверит содержимым.");
        }

        if (planned.Deletes + planned.DirDeletes > 0)
        {
            var (_, newest) = SyncFreshness.DeletionRecency(_result.Root);

            if (newest is { } when)
            {
                lines.Add(string.Empty);
                lines.Add($"Новейшее из удаляемого: {FormatStamp(when)} – убедитесь, что зеркалите не более свежую папку.");
            }
        }

        lines.Add(string.Empty);
        lines.Add("Продолжить?");

        if (!_dialogs.Confirm("Синхронизация", string.Join(Environment.NewLine, lines)))
        {
            return;
        }

        await ExecuteSyncAsync(true);
    }

    private async Task<SyncReport?> ExecuteSyncAsync(bool interactive, CancellationToken external = default)
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

        var report = await RunAsync("Синхронизация:", (token, progress) =>
        {
            var engine = new SyncEngine(_engineLogger);
            var executed = engine.Execute(result, token, progress);

            if (verify)
            {
                engine.Verify(executed, result.LeftPath, result.RightPath, token);
            }

            return executed;
        }, planned.Total, external, planned.CopyBytes);

        stopwatch.Stop();

        if (report is null)
        {
            return null;
        }

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

        var verifyText = verify ? $", расхождений: {report.Mismatches.Count:N0}" : string.Empty;
        var volumeText = report.CopiedBytes > 0 ? $" Перенесено: {SizeFormatter.Format(report.CopiedBytes)}." : string.Empty;
        SummaryText = $"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с.{volumeText} Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}";
        StatusCaption = SummaryText;

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
            ShowSyncOutcome(report, stopwatch.Elapsed);
        }

        return report;
    }

    private void ShowSyncOutcome(SyncReport report, TimeSpan elapsed)
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
        else
        {
            var done = new List<string>();

            if (report.CopiedCount > 0)
            {
                var copied = report.CopiedBytes > 0 ? $" ({SizeFormatter.Format(report.CopiedBytes)})" : string.Empty;
                done.Add($"скопировано: {report.CopiedCount:N0}{copied}");
            }

            if (report.DeletedCount > 0)
            {
                var deleted = report.DeletedBytes > 0 ? $" ({SizeFormatter.Format(report.DeletedBytes)})" : string.Empty;
                done.Add($"удалено в корзину: {report.DeletedCount:N0}{deleted}");
            }

            var detail = done.Count > 0 ? string.Join(", ", done) : "изменений не потребовалось";
            _dialogs.Info("Синхронизация", $"Готово за {elapsed.TotalSeconds:F1} с. {char.ToUpperInvariant(detail[0])}{detail[1..]}.");
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
        StatusCaption = $"Разрешено элементов: {count}.";
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
            StatusCaption = $"Сравнение выгружено: {Path.GetFileName(dialog.FileName)}";
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
    private void CancelOperation()
    {
        _cts?.Cancel();
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

    partial void OnIsBusyChanged(bool value)
    {
        Profiles.NotifyCanSaveChanged();
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

    partial void OnGitHistoryCountChanged(int value)
    {
        Persist(SettingsKeys.SyncGitHistoryCount, value.ToString());

        _gitHistoryLoaded = false;

        if (GitHistoryExpanded && HasGit)
        {
            _ = LoadGitHistoryAsync();
        }
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

    partial void OnFlatSortChanged(SyncFlatSortField value)
    {
        Persist(SettingsKeys.SyncFlatSort, value.ToString());

        if (_result is not null && FlatView)
        {
            RebuildRows();
        }
    }

    partial void OnFlatSortDescendingChanged(bool value)
    {
        Persist(SettingsKeys.SyncFlatSortDesc, value ? "true" : "false");

        if (_result is not null && FlatView)
        {
            RebuildRows();
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(SearchTextEmpty));

        if (_result is not null && FlatView)
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

    private async Task<T?> RunAsync<T>(
        string caption,
        Func<CancellationToken, IProgress<OperationProgress>, T> work,
        int total = 0,
        CancellationToken external = default,
        long totalBytes = 0)
        where T : class
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var token = _cts.Token;
        IsBusy = true;

        var operation = caption.TrimEnd(' ', ':');
        var determinate = total > 0;

        if (determinate)
        {
            ProgressMax = total;
            ProgressValue = 0;
            IsIndeterminate = false;
            StatusCaption = $"{caption} 0 / {total} (0 %)";
        }
        else
        {
            IsIndeterminate = true;
            StatusCaption = caption;
        }

        ProgressDetail = StatusCaption;

        var progress = new Progress<OperationProgress>(update =>
        {
            var tail = string.IsNullOrEmpty(update.Current) ? string.Empty : $" · {update.Current}";

            string head;

            if (determinate)
            {
                ProgressValue = update.Completed;
                var percent = update.Completed * 100 / total;
                head = $"{caption} {update.Completed} / {total} ({percent} %)";
            }
            else
            {
                head = $"{caption} {update.Completed}";
            }

            if (totalBytes > 0)
            {
                head += $" · {SizeFormatter.Format(update.Bytes)} из {SizeFormatter.Format(totalBytes)}";
            }

            StatusCaption = head;
            ProgressDetail = head + tail;
        });

        try
        {
            return await Task.Run(() => work(token, progress), token);
        }
        catch (OperationCanceledException)
        {
            _logger.SyncOperationCancelled(operation);
            SummaryText = "Операция отменена.";
            StatusCaption = SummaryText;
            return null;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _logger.SyncOperationFailed(cause, operation);
            _notifier.Notify($"Ошибка: {operation}", StatusSeverity.Error);
            _dialogs.Error("Ошибка", cause.Message);
            SummaryText = $"Ошибка: {cause.Message}";
            StatusCaption = SummaryText;
            return null;
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = true;
            ProgressValue = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void CollapseAllDirectories(DirectoryComparison root)
    {
        _collapsed.Clear();
        AddCollapsedChildren(root);
    }

    private void ClearComparison()
    {
        _result = null;
        _lastReport = null;
        _dirSizeCache = null;
        _outcomes = [];
        _collapsed.Clear();
        Rows.ReplaceAll([]);
        ClearGit();
        UpdateSummary();
    }

    private void AddCollapsed(DirectoryComparison dir)
    {
        _collapsed.Add(dir);
        AddCollapsedChildren(dir);
    }

    private void AddCollapsedChildren(DirectoryComparison dir)
    {
        foreach (var sub in dir.SubDirectories)
        {
            AddCollapsed(sub);
        }
    }

    private void RemoveCollapsed(DirectoryComparison dir)
    {
        _collapsed.Remove(dir);

        foreach (var sub in dir.SubDirectories)
        {
            RemoveCollapsed(sub);
        }
    }

    private void RebuildRows()
    {
        var result = _result;

        if (result is null)
        {
            Rows.ReplaceAll([]);
            return;
        }

        var buffer = FlatView ? BuildFlatRows(result.Root) : BuildTreeRows(result.Root);

        Rows.ReplaceAll(buffer);
    }

    private List<SyncNodeViewModel> BuildFlatRows(DirectoryComparison root)
    {
        var search = SearchText.Trim();
        var files = FilterBySearch(CollectVisibleFiles(root, ShowIdentical, HideApplied, _outcomes), search, static file => file.RelativePath);
        var sortedFiles = SortFlatFiles(files, FlatSort, FlatSortDescending).ToList();
        var dirs = FilterBySearch(CollectEmptyDirs(root, HideApplied, _outcomes), search, static dir => dir.RelativePath);
        var sortedDirs = dirs.OrderBy(dir => dir.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        var groupFolders = ParseGroupFolders(Operations.GroupFolders);
        var buffer = new List<SyncNodeViewModel>();

        AddUngroupedRows(buffer, sortedFiles, sortedDirs, groupFolders);
        AddGroupedRows(buffer, sortedFiles, sortedDirs, groupFolders);
        return buffer;
    }

    private List<SyncNodeViewModel> BuildTreeRows(DirectoryComparison root)
    {
        var buffer = new List<SyncNodeViewModel>();
        FlattenDirectory(root, 0, buffer);
        return buffer;
    }

    private void AddUngroupedRows(
        List<SyncNodeViewModel> buffer,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders)
    {
        foreach (var file in files.Where(file => !IsGroupedPath(file.RelativePath, groupFolders)))
        {
            buffer.Add(new(file, 0, this, true) { Outcome = _outcomes.GetValueOrDefault(file) });
        }

        foreach (var dir in dirs.Where(dir => !IsGroupedPath(dir.RelativePath, groupFolders)))
        {
            buffer.Add(new(dir, 0, false, 0, 0, this, true) { Outcome = _outcomes.GetValueOrDefault(dir) });
        }
    }

    private void AddGroupedRows(
        List<SyncNodeViewModel> buffer,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders)
    {
        var groupedFiles = files.Where(file => IsGroupedPath(file.RelativePath, groupFolders)).ToList();
        var groupedDirs = dirs.Where(dir => IsGroupedPath(dir.RelativePath, groupFolders)).ToList();
        var total = groupedFiles.Count + groupedDirs.Count;

        if (total == 0)
        {
            return;
        }

        buffer.Add(SyncNodeViewModel.CreateGroupHeader(total, _gitGroupExpanded, this));

        if (!_gitGroupExpanded)
        {
            return;
        }

        foreach (var folder in groupFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddGroup(buffer, folder, groupedFiles, groupedDirs, groupFolders);
        }
    }

    private void AddGroup(
        List<SyncNodeViewModel> buffer,
        string folder,
        IReadOnlyList<FileComparison> files,
        IReadOnlyList<DirectoryComparison> dirs,
        IReadOnlyCollection<string> groupFolders)
    {
        var subFiles = files.Where(file => string.Equals(GroupedKey(file.RelativePath, groupFolders), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var subDirs = dirs.Where(dir => string.Equals(GroupedKey(dir.RelativePath, groupFolders), folder, StringComparison.OrdinalIgnoreCase)).ToList();
        var total = subFiles.Count + subDirs.Count;

        if (total == 0)
        {
            return;
        }

        var expanded = !_collapsedSubGroups.Contains(folder);
        buffer.Add(SyncNodeViewModel.CreateSubGroupHeader(folder, total, expanded, this));

        if (expanded)
        {
            AddGroupItems(buffer, subFiles, subDirs);
        }
    }

    private void AddGroupItems(List<SyncNodeViewModel> buffer, IReadOnlyList<FileComparison> files, IReadOnlyList<DirectoryComparison> dirs)
    {
        foreach (var file in files)
        {
            buffer.Add(new(file, 2, this, true) { Outcome = _outcomes.GetValueOrDefault(file) });
        }

        foreach (var dir in dirs)
        {
            buffer.Add(new(dir, 2, false, 0, 0, this, true) { Outcome = _outcomes.GetValueOrDefault(dir) });
        }
    }

    private void FlattenDirectory(DirectoryComparison dir, int indent, List<SyncNodeViewModel> buffer)
    {
        AddVisibleDirectories(dir, indent, buffer);
        AddVisibleFiles(dir, indent, buffer);
    }

    private void AddVisibleDirectories(DirectoryComparison dir, int indent, List<SyncNodeViewModel> buffer)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (!IsVisible(sub))
            {
                continue;
            }

            var outcome = _outcomes.GetValueOrDefault(sub);
            var expanded = !_collapsed.Contains(sub);
            var sizes = _dirSizeCache?.GetValueOrDefault(sub);
            buffer.Add(new(sub, indent, expanded, sizes?.Left ?? 0, sizes?.Right ?? 0, this) { Outcome = outcome });

            if (expanded)
            {
                FlattenDirectory(sub, indent + 1, buffer);
            }
        }
    }

    private void AddVisibleFiles(DirectoryComparison dir, int indent, List<SyncNodeViewModel> buffer)
    {
        foreach (var file in dir.Files)
        {
            if (!IsVisible(file))
            {
                continue;
            }

            var outcome = _outcomes.GetValueOrDefault(file);
            buffer.Add(new(file, indent, this) { Outcome = outcome });
        }
    }

    private bool IsVisible(DirectoryComparison dir)
    {
        return (ShowIdentical || dir.Status != ComparisonStatus.Identical)
               && (!HideApplied || _outcomes.GetValueOrDefault(dir) != SyncOutcome.Applied);
    }

    private bool IsVisible(FileComparison file)
    {
        return (ShowIdentical || file.Status != ComparisonStatus.Identical)
               && (!HideApplied || _outcomes.GetValueOrDefault(file) != SyncOutcome.Applied);
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
        return _total > 0 ? (double)_stats[status] / _total : 0;
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
        OnPropertyChanged(nameof(IdenticalFraction));
        OnPropertyChanged(nameof(LeftOnlyFraction));
        OnPropertyChanged(nameof(RightOnlyFraction));
        OnPropertyChanged(nameof(ModifiedFraction));
        OnPropertyChanged(nameof(ConflictFraction));
        OnPropertyChanged(nameof(HasConflicts));
        OnPropertyChanged(nameof(CompositionHint));
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
        GitHistoryCount = _settings.GetInt(SettingsKeys.SyncGitHistoryCount, AppDefaults.GitHistoryCountDefault);
        HideApplied = _settings.GetBool(SettingsKeys.SyncHideApplied);
        FlatView = _settings.GetBool(SettingsKeys.SyncFlatView);
        FlatSort = _settings.GetEnum(SettingsKeys.SyncFlatSort, AppDefaults.SyncFlatSortDefault);
        FlatSortDescending = _settings.GetBool(SettingsKeys.SyncFlatSortDesc);

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
