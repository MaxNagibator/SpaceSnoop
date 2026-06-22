using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using SpaceSnoop.Wpf.Diff;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels;

// TODO: Шляпа с ILogger
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

    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private CancellationTokenSource? _cts;
    private bool _suppressPersist;
    private Dictionary<ComparisonStatus, int> _stats = NewZeroStats();
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
    private bool _showIdentical;

    [ObservableProperty]
    private bool _showSizes = AppDefaults.SyncShowSizesDefault;

    [ObservableProperty]
    private bool _showModified = AppDefaults.SyncShowModifiedDefault;

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
        LoadSettings();
    }

    public OperationPreferences Operations { get; }

    public RangeObservableCollection<SyncNodeViewModel> Rows { get; } = [];

    public IReadOnlyList<string> Modes { get; } = ["Слева направо", "Справа налево", "Двусторонний"];

    public bool HasResult => _result is not null;

    public int IdenticalCount => _stats[ComparisonStatus.Identical];

    public int LeftOnlyCount => _stats[ComparisonStatus.LeftOnly];

    public int RightOnlyCount => _stats[ComparisonStatus.RightOnly];

    public int ModifiedCount => _stats[ComparisonStatus.Modified];

    public int ConflictCount => _stats[ComparisonStatus.Conflict];

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

    public string PageTitle => "Синхронизация";

    public string PageDescription => "Сравнение и синхронизация двух каталогов.";

    public bool IsIndeterminate => true;

    public double ProgressValue => 0;

    public double ProgressMax => 1;

    public ICommand CancelCommand => CancelOperationCommand;

    private SyncMode CurrentMode => ModeOrder[Math.Clamp(SelectedModeIndex, 0, ModeOrder.Length - 1)];

    public void NotifyActionsChanged()
    {
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

    private static void ApplyActionRecursive(DirectoryComparison dir, SyncAction action)
    {
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

    private static (int Copies, int Deletes) CountPlannedActions(DirectoryComparison dir)
    {
        var copies = 0;
        var deletes = 0;

        foreach (var file in dir.Files)
        {
            switch (file.Action)
            {
                case SyncAction.CopyToRight or SyncAction.CopyToLeft:
                    copies++;
                    break;

                case SyncAction.DeleteLeft or SyncAction.DeleteRight:
                    deletes++;
                    break;
            }
        }

        foreach (var sub in dir.SubDirectories)
        {
            var (subCopies, subDeletes) = CountPlannedActions(sub);
            copies += subCopies;
            deletes += subDeletes;
        }

        return (copies, deletes);
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

    private void HashModifiedFiles(DirectoryComparison dir, string leftBase, string rightBase, CancellationToken token)
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
        }

        foreach (var sub in dir.SubDirectories)
        {
            HashModifiedFiles(sub, leftBase, rightBase, token);
        }
    }

    private void WriteSyncLog(SyncReport report)
    {
        try
        {
            var logPath = Path.Combine(AppContext.BaseDirectory, AppInfo.SyncLogFileName);
            using var writer = new StreamWriter(logPath, true);
            writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация: {report.SuccessCount} успешно, {report.Errors.Count} ошибок");

            foreach (var error in report.Errors)
            {
                writer.WriteLine($"  ОШИБКА: {error.RelativePath} ({error.Action}): {error.Message}");
            }
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

        var filter = new ExclusionFilter(Exclusions);
        var stopwatch = Stopwatch.StartNew();

        _logger.CompareStarted(left, right);

        var mode = CurrentMode;

        var prepared = await RunAsync("Сравнение...", token =>
        {
            var comparer = new DirectoryComparer(filter, _comparerLogger);
            var compared = comparer.Compare(left, right, token);
            compared.ApplyMode(mode);
            return new ComparePreparation(compared, BuildDirSizeCache(compared.Root));
        });

        stopwatch.Stop();

        if (prepared is null)
        {
            return;
        }

        _result = prepared.Result;
        _dirSizeCache = prepared.Sizes;
        _collapsed.Clear();
        RebuildRows();
        UpdateSummary();
        SummaryText = $"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с";
        StatusCaption = SummaryText;

        _logger.CompareFinished(_total, (long)stopwatch.Elapsed.TotalMilliseconds);
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

        var sizes = await RunAsync("Вычисление хешей...", token =>
        {
            HashModifiedFiles(result.Root, result.LeftPath, result.RightPath, token);
            return BuildDirSizeCache(result.Root);
        });

        stopwatch.Stop();

        if (sizes is null)
        {
            return;
        }

        _dirSizeCache = sizes;
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

        var (copies, deletes) = CountPlannedActions(_result.Root);
        var parts = new List<string>();

        if (copies > 0)
        {
            parts.Add($"скопировать – {copies}");
        }

        if (deletes > 0)
        {
            parts.Add($"удалить (в корзину) – {deletes}");
        }

        var summary = parts.Count > 0 ? string.Join(", ", parts) : "изменений нет";

        if (!_dialogs.Confirm("Синхронизация", $"Будет выполнено: {summary}. Начать?"))
        {
            return;
        }

        var result = _result;
        var stopwatch = Stopwatch.StartNew();

        _logger.SyncStarted(CurrentMode);

        var report = await RunAsync("Синхронизация...", token =>
        {
            var engine = new SyncEngine(_engineLogger);
            return engine.Execute(result, token);
        });

        stopwatch.Stop();

        if (report is null)
        {
            return;
        }

        _logger.SyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

        WriteSyncLog(report);
        SummaryText = $"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с. Успешно: {report.SuccessCount}, ошибок: {report.Errors.Count}";
        StatusCaption = SummaryText;

        if (report.Errors.Count > 0)
        {
            var list = string.Join(Environment.NewLine, report.Errors.Select(e => $"  {e.RelativePath}: {e.Message}"));
            _dialogs.Warning("Ошибки", $"Ошибки при синхронизации:{Environment.NewLine}{list}");
        }
        else
        {
            _dialogs.Info("Синхронизация", $"Готово. Успешно скопировано/удалено: {report.SuccessCount}.");
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
        OnPropertyChanged(nameof(DirectionIconKind));
        OnPropertyChanged(nameof(DirectionHint));

        if (_result is null)
        {
            return;
        }

        _result.ApplyMode(CurrentMode);
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

    partial void OnLeftPathChanged(string value)
    {
        Persist(SettingsKeys.SyncLeft, value);
        LeftPathInvalid = PathMissing(value);
    }

    partial void OnRightPathChanged(string value)
    {
        Persist(SettingsKeys.SyncRight, value);
        RightPathInvalid = PathMissing(value);
    }

    partial void OnExclusionsChanged(string value)
    {
        Persist(SettingsKeys.SyncExclusions, value);
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

    private async Task<T?> RunAsync<T>(string caption, Func<CancellationToken, T> work)
        where T : class
    {
        _cts = new();
        var token = _cts.Token;
        IsBusy = true;
        StatusCaption = caption;

        try
        {
            return await Task.Run(() => work(token), token);
        }
        catch (OperationCanceledException)
        {
            SummaryText = "Операция отменена.";
            StatusCaption = SummaryText;
            return null;
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _dialogs.Error("Ошибка", cause.Message);
            SummaryText = $"Ошибка: {cause.Message}";
            StatusCaption = SummaryText;
            return null;
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
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
        FlattenDirectory(_result.Root, 0, buffer);
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

            var expanded = !_collapsed.Contains(sub);
            var sizes = _dirSizeCache?.GetValueOrDefault(sub);
            buffer.Add(new(sub, indent, expanded, sizes?.Left ?? 0, sizes?.Right ?? 0, this));

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

            buffer.Add(new(file, indent, this));
        }
    }

    private void UpdateSummary()
    {
        if (_result is null)
        {
            SummaryText = "Сравнение не выполнялось.";
            _stats = NewZeroStats();
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
        _total = stats.Values.Sum();
        NotifyLedgerChanged();

        HasPending = _result.HasPendingResolution();
        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
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
        OnPropertyChanged(nameof(IdenticalFraction));
        OnPropertyChanged(nameof(LeftOnlyFraction));
        OnPropertyChanged(nameof(RightOnlyFraction));
        OnPropertyChanged(nameof(ModifiedFraction));
        OnPropertyChanged(nameof(ConflictFraction));
        OnPropertyChanged(nameof(HasConflicts));
    }

    private bool HasActionableChanges()
    {
        if (_result is null)
        {
            return false;
        }

        var stats = _result.GetStatistics();
        return stats.Any(kv => kv.Key != ComparisonStatus.Identical && kv.Value > 0);
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        LeftPath = _settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty;
        RightPath = _settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty;
        Exclusions = _settings.GetStringValue(SettingsKeys.SyncExclusions) ?? Operations.DefaultExclusions;

        SelectedModeIndex = Math.Clamp(_settings.GetInt(SettingsKeys.SyncMode), 0, ModeOrder.Length - 1);
        ShowIdentical = _settings.GetBool(SettingsKeys.SyncShowIdentical);
        ShowSizes = _settings.GetBool(SettingsKeys.SyncShowSizes, AppDefaults.SyncShowSizesDefault);
        ShowModified = _settings.GetBool(SettingsKeys.SyncShowModified);

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
