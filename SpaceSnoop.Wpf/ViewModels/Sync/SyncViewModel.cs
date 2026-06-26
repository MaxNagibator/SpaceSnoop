using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using SpaceSnoop.Wpf.Diff;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;
    private static readonly SyncMode[] ModeOrder = [SyncMode.LeftToRight, SyncMode.RightToLeft, SyncMode.Bidirectional];
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SyncViewModel> _logger;
    private readonly ILogger<SyncEngine> _engineLogger;
    private readonly ILogger<DirectoryComparer> _comparerLogger;
    private readonly HashSet<DirectoryComparison> _collapsed = [];

    private ComparisonResult? _result;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private CancellationTokenSource? _cts;
    private bool _suppressPersist;
    private bool _gitPromptDeclined;
    private bool _isIndeterminate = true;
    private double _progressValue;
    private double _progressMax = 1;
    private Dictionary<ComparisonStatus, int> _stats = NewZeroStats();
    private Dictionary<ComparisonStatus, int> _dirStats = NewZeroStats();
    private FreshnessSummary _freshness;
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

    public SyncViewModel(ISettingsStore settings, IDialogService dialogs, OperationPreferences operations, ILogger<SyncViewModel> logger, ILogger<SyncEngine> engineLogger, ILogger<DirectoryComparer> comparerLogger)
    {
        _settings = settings;
        _dialogs = dialogs;
        Operations = operations;
        _logger = logger;
        _engineLogger = engineLogger;
        _comparerLogger = comparerLogger;
        Profiles = new(settings, dialogs, BuildCurrentProfile, ApplyProfile, () => !IsBusy, message => StatusCaption = message);
        LoadSettings();
        Profiles.Load();
        _settings.Changed += OnSettingsChanged;
    }

    public OperationPreferences Operations { get; }

    public RangeObservableCollection<SyncNodeViewModel> Rows { get; } = [];

    public SyncQuickProfilesViewModel Profiles { get; }

    public IReadOnlyList<string> Modes { get; } = ["Слева направо", "Справа налево", "Двусторонний"];

    public bool HasResult => _result is not null;

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

    public bool MirrorApplicable => CurrentMode != SyncMode.Bidirectional;

    public bool IsBidirectional => CurrentMode == SyncMode.Bidirectional;

    public string MirrorHint => CurrentMode switch
    {
        SyncMode.RightToLeft => "Зеркало: удалять слева то, чего нет справа (в корзину).",
        SyncMode.Bidirectional => "Зеркало доступно только при одностороннем направлении.",
        _ => "Зеркало: удалять справа то, чего нет слева (в корзину).",
    };

    public bool MirrorDeletes => Mirror && MirrorApplicable;

    public bool ShowNewerBadge => _result is not null && _freshness.Verdict != NewerSide.None;

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

    public bool ExclusionsEmpty => string.IsNullOrWhiteSpace(Exclusions);

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public string CompositionHint =>
        $"Файлы: только слева {LeftOnlyCount:N0}, только справа {RightOnlyCount:N0}, изменены {ModifiedCount:N0}, конфликты {ConflictCount:N0}, одинаковые {IdenticalCount:N0}.{Environment.NewLine}"
        + $"Каталоги: только слева {LeftOnlyDirCount:N0}, только справа {RightOnlyDirCount:N0}, изменены {ModifiedDirCount:N0}, одинаковые {IdenticalDirCount:N0}.";

    public string LeftOnlyHint => $"Файлов: {LeftOnlyCount:N0}; каталогов: {LeftOnlyDirCount:N0}";

    public string RightOnlyHint => $"Файлов: {RightOnlyCount:N0}; каталогов: {RightOnlyDirCount:N0}";

    public string ModifiedHint => $"Файлов: {ModifiedCount:N0}; каталогов: {ModifiedDirCount:N0}";

    public string ConflictHint => $"Файлов: {ConflictCount:N0}";

    public string IdenticalHint => $"Файлов: {IdenticalCount:N0}; каталогов: {IdenticalDirCount:N0}";

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

    private SyncMode CurrentMode => ModeOrder[Math.Clamp(SelectedModeIndex, 0, ModeOrder.Length - 1)];

    private PlannedActions CurrentPlan => _result?.CountPlannedActions() ?? new(0, 0, 0, 0, 0);

    public void NotifyActionsChanged()
    {
        foreach (var row in Rows)
        {
            row.RefreshSubtreeAction();
        }

        UpdateSummary();
    }

    public void ToggleExpand(DirectoryComparison dir)
    {
        if (!_collapsed.Remove(dir))
        {
            _collapsed.Add(dir);
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
        catch (InvalidOperationException ex)
        {
            _dialogs.Warning("Сравнение содержимого", ex.Message);
            return;
        }
        catch (Exception ex)
        {
            _logger.ContentCompareFailed(ex, file.RelativePath);
            _dialogs.Error("Сравнение содержимого", ex.Message);
            return;
        }

        _logger.ContentCompareOpened(file.RelativePath, built.Added, built.Removed);

        var dialog = new FileDiffDialogViewModel(_settings, file.Name, leftPath, rightPath, built.Lines, built.Added, built.Removed);
        await _dialogs.ShowAsync(dialog);
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

    internal static string AddGitExclusion(string exclusions)
    {
        var parts = exclusions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Any(static part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase))
            ? exclusions
            : string.IsNullOrWhiteSpace(exclusions)
                ? ".git"
                : $"{exclusions.TrimEnd()},.git";
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles)
        {
            Profiles.Load();
        }
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
        var left = ReadTextLines(leftPath);
        var right = ReadTextLines(rightPath);
        var lines = TextDiff.Compute(left, right);
        var added = lines.Count(static l => l.Kind == DiffLineKind.Added);
        var removed = lines.Count(static l => l.Kind == DiffLineKind.Removed);
        return new(lines, added, removed);
    }

    private static string[] ReadTextLines(string path)
    {
        var info = new FileInfo(path);

        if (!info.Exists)
        {
            throw new InvalidOperationException($"Файл не найден: {path}");
        }

        if (info.Length > MaxDiffBytes)
        {
            throw new InvalidOperationException("Файл слишком велик для построчного сравнения (> 5 МБ).");
        }

        // TODO: бинарь определяем по NUL-байту; кодировку доверяем File.ReadAllLines (BOM → UTF-8)
        if (Array.IndexOf(File.ReadAllBytes(path), (byte)0) >= 0)
        {
            throw new InvalidOperationException("Файл выглядит двоичным – построчное сравнение недоступно.");
        }

        return File.ReadAllLines(path);
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

    private void WriteSyncLog(SyncReport report)
    {
        try
        {
            var logPath = Path.Combine(AppStorage.DataDirectory, AppInfo.SyncLogFileName);
            using var writer = new StreamWriter(logPath, true);
            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация: {report.SuccessCount} успешно, {report.Errors.Count} ошибок");
            report.WriteDetails(writer);
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
    private async Task CompareAsync()
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

        var prepared = await RunAsync("Сравнение каталогов:", (token, progress) =>
        {
            var comparer = new DirectoryComparer(filter, _comparerLogger);
            var compared = comparer.Compare(left, right, token, progress);
            compared.ApplyMode(mode, mirror);
            return new ComparePreparation(compared, BuildDirSizeCache(compared.Root));
        });

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
        var lines = BuildPlanLines(planned, true);

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

        var result = _result;
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
        }, planned.Total);

        stopwatch.Stop();

        if (report is null)
        {
            return;
        }

        _logger.SyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

        if (verify)
        {
            _logger.SyncVerified(report.Applied.Count, report.Mismatches.Count);
        }

        WriteSyncLog(report);
        _outcomes = SyncOutcomes.Build(result, report.Errors, report.Mismatches);
        RebuildRows();

        var verifyText = verify ? $", расхождений: {report.Mismatches.Count:N0}" : string.Empty;
        SummaryText = $"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с. Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}";
        StatusCaption = SummaryText;

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
                done.Add($"скопировано: {report.CopiedCount:N0}");
            }

            if (report.DeletedCount > 0)
            {
                done.Add($"удалено в корзину: {report.DeletedCount:N0}");
            }

            var detail = done.Count > 0 ? string.Join(", ", done) : "изменений не потребовалось";
            _dialogs.Info("Синхронизация", $"Готово за {stopwatch.Elapsed.TotalSeconds:F1} с. {char.ToUpperInvariant(detail[0])}{detail[1..]}.");
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

        _result.ApplyMode(CurrentMode, Mirror);
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
        Profiles.MarkCurrent();
    }

    partial void OnRightPathChanged(string value)
    {
        Persist(SettingsKeys.SyncRight, value);
        RightPathInvalid = PathMissing(value);
        Profiles.MarkCurrent();
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

    private async Task<T?> RunAsync<T>(string caption, Func<CancellationToken, IProgress<OperationProgress>, T> work, int total = 0)
        where T : class
    {
        _cts = new();
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
        _dirSizeCache = null;
        _outcomes = [];
        _collapsed.Clear();
        Rows.ReplaceAll([]);
        UpdateSummary();
    }

    private void ApplyProfile(SyncProfile profile)
    {
        ClearComparison();
        LeftPath = profile.Left;
        RightPath = profile.Right;
        Exclusions = profile.Exclusions;
        SelectedModeIndex = Math.Clamp(profile.Mode, 0, ModeOrder.Length - 1);
        Mirror = profile.Mirror;
        StatusCaption = $"Профиль применён: {profile.Name}.";
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
        if (_result is null)
        {
            Rows.ReplaceAll([]);
            return;
        }

        var buffer = new List<SyncNodeViewModel>();

        if (FlatView)
        {
            var files = CollectVisibleFiles(_result.Root, ShowIdentical, HideApplied, _outcomes);
            var search = SearchText.Trim();

            if (search.Length > 0)
            {
                files = files.Where(file => file.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var file in SortFlatFiles(files, FlatSort, FlatSortDescending))
            {
                buffer.Add(new(file, 0, this, true) { Outcome = _outcomes.GetValueOrDefault(file) });
            }

            var dirs = CollectEmptyDirs(_result.Root, HideApplied, _outcomes);

            if (search.Length > 0)
            {
                dirs = dirs.Where(dir => dir.RelativePath.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var dir in dirs.OrderBy(dir => dir.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                buffer.Add(new(dir, 0, false, 0, 0, this, true) { Outcome = _outcomes.GetValueOrDefault(dir) });
            }
        }
        else
        {
            FlattenDirectory(_result.Root, 0, buffer);
        }

        Rows.ReplaceAll(buffer);
    }

    private void FlattenDirectory(DirectoryComparison dir, int indent, List<SyncNodeViewModel> buffer)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (!ShowIdentical && sub.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            var outcome = _outcomes.GetValueOrDefault(sub);

            if (HideApplied && outcome == SyncOutcome.Applied)
            {
                continue;
            }

            var expanded = !_collapsed.Contains(sub);
            var sizes = _dirSizeCache?.GetValueOrDefault(sub);
            buffer.Add(new(sub, indent, expanded, sizes?.Left ?? 0, sizes?.Right ?? 0, this) { Outcome = outcome });

            if (expanded)
            {
                FlattenDirectory(sub, indent + 1, buffer);
            }
        }

        foreach (var file in dir.Files)
        {
            if (!ShowIdentical && file.Status == ComparisonStatus.Identical)
            {
                continue;
            }

            var outcome = _outcomes.GetValueOrDefault(file);

            if (HideApplied && outcome == SyncOutcome.Applied)
            {
                continue;
            }

            buffer.Add(new(file, indent, this) { Outcome = outcome });
        }
    }

    private void UpdateSummary()
    {
        if (_result is null)
        {
            SummaryText = "Сравнение не выполнялось.";
            _stats = NewZeroStats();
            _dirStats = NewZeroStats();
            _freshness = default;
            _total = 0;
            NotifyLedgerChanged();
            HasPending = false;
            SyncCommand.NotifyCanExecuteChanged();
            HashCommand.NotifyCanExecuteChanged();
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
        _total = stats.Values.Sum();
        NotifyLedgerChanged();

        HasPending = _result.HasPendingResolution();
        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
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

    private List<string> BuildPlanLines(PlannedActions planned, bool includeDirection)
    {
        var lines = new List<string>();

        if (includeDirection)
        {
            lines.Add($"Направление: {DirectionText()}.");
            lines.Add(string.Empty);
        }

        if (planned.Copies > 0)
        {
            lines.Add($"Скопировать файлов: {planned.Copies:N0}");

            if (planned.ModifiedCopies > 0)
            {
                lines.Add($"    – новых: {planned.NewCopies:N0}");
                lines.Add($"    – изменённых: {planned.ModifiedCopies:N0}");
            }
        }

        if (planned.DirCopies > 0)
        {
            lines.Add($"Создать каталогов: {planned.DirCopies:N0}");
        }

        if (planned.Deletes > 0)
        {
            lines.Add($"Удалить файлов в корзину: {planned.Deletes:N0}");
        }

        if (planned.DirDeletes > 0)
        {
            lines.Add($"Удалить каталогов в корзину: {planned.DirDeletes:N0}");
        }

        if (planned.Total == 0)
        {
            lines.Add("Изменений нет.");
        }

        return lines;
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
        OnPropertyChanged(nameof(LeftOnlyHint));
        OnPropertyChanged(nameof(RightOnlyHint));
        OnPropertyChanged(nameof(ModifiedHint));
        OnPropertyChanged(nameof(ConflictHint));
        OnPropertyChanged(nameof(IdenticalHint));
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
        ShowIdentical = _settings.GetBool(SettingsKeys.SyncShowIdentical);
        ShowSizes = _settings.GetBool(SettingsKeys.SyncShowSizes, AppDefaults.SyncShowSizesDefault);
        ShowModified = _settings.GetBool(SettingsKeys.SyncShowModified);
        BlankAbsent = _settings.GetBool(SettingsKeys.SyncBlankAbsent);
        Verify = _settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);
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

    private sealed record ComparePreparation(ComparisonResult Result, Dictionary<DirectoryComparison, (long Left, long Right)> Sizes);

    private sealed record FileDiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed);
}
