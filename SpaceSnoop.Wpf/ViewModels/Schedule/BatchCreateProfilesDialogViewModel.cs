using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

public sealed partial class BatchCreateProfilesDialogViewModel : ObservableObject, IDialogViewModel, IDisposable
{
    private readonly IReadOnlyList<SyncProfile> _existing;
    private readonly ISettingsStore _settings;
    private readonly IFilePicker _filePicker;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger? _logger;
    private readonly Lock _rebuildGate = new();

    private CancellationTokenSource? _rebuildCts;
    private int _rebuildGeneration;
    private bool _isRebuilding;
    private bool _closed;

    [ObservableProperty]
    private string _sourceParent = string.Empty;

    [ObservableProperty]
    private string _destParent = string.Empty;

    [ObservableProperty]
    private int _selectedModeIndex;

    [ObservableProperty]
    private int _selectedWinnerIndex;

    [ObservableProperty]
    private bool _mirror;

    [ObservableProperty]
    private string _exclusions = string.Empty;

    private int _selectedSort;

    public event EventHandler<bool>? RequestClose;

    public string Title => "Создать пачку профилей";

    public bool PathSuggest { get; }

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

    public IReadOnlyList<string> SortOptions { get; } = ["Имя (А–Я)", "Имя (Я–А)", "Сначала новые"];

    public IReadOnlyList<SegmentOption> SortSegmentOptions { get; } =
    [
        new(PackIconLucideKind.ArrowDownAZ, "Имя (А–Я)", "Сортировать пары по имени от А до Я"),
        new(PackIconLucideKind.ArrowUpZA, "Имя (Я–А)", "Сортировать пары по имени от Я до А"),
        new(PackIconLucideKind.Sparkles, "Сначала новые", "Сначала пары, для которых ещё нет профиля"),
    ];

    public int SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value < 0)
            {
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedSort, value))
            {
                return;
            }

            _settings.SetInt(SettingsKeys.BatchSort, value);

            var sorted = ProfileBatch.Sort(Rows, value);

            for (var index = 0; index < sorted.Count; index++)
            {
                var current = Rows.IndexOf(sorted[index]);

                if (current != index)
                {
                    Rows.Move(current, index);
                }
            }
        }
    }

    public PackIconLucideKind DirectionIconKind => SelectedModeIndex switch
    {
        1 => PackIconLucideKind.ArrowLeft,
        2 => PackIconLucideKind.ArrowRightLeft,
        _ => PackIconLucideKind.ArrowRight,
    };

    public string DirectionHint => SelectedModeIndex switch
    {
        1 => "Направление: справа налево. Клик – сменить, ПКМ – поменять каталоги местами.",
        2 => "Направление: двустороннее. Клик – сменить, ПКМ – поменять каталоги местами.",
        _ => "Направление: слева направо. Клик – сменить, ПКМ – поменять каталоги местами.",
    };

    public ObservableCollection<BatchPairRow> Rows { get; } = [];

    public IReadOnlyList<SyncProfile> CreatedProfiles { get; private set; } = [];

    public int SelectedCount => Rows.Count(row => row.Include && row.IsSelectable);

    public bool HasRows => Rows.Count > 0;

    public int SelectableCount => Rows.Count(row => row.IsSelectable);

    public string SelectionSummary => $"Отмечено: {SelectedCount} из {SelectableCount}";

    public string CreateCaption => SelectedCount > 0
        ? $"Создать {Plural.Format(SelectedCount, "профиль", "профиля", "профилей")}"
        : "Создать профили";

    public bool? AllSelected
    {
        get
        {
            if (SelectableCount == 0 || SelectedCount == 0)
            {
                return false;
            }

            return SelectedCount == SelectableCount ? true : null;
        }
        set
        {
            if (value is not bool target)
            {
                return;
            }

            foreach (var row in Rows.Where(static row => row.IsSelectable))
            {
                row.Include = target;
            }
        }
    }

    public bool WinnerApplicable => SelectedModeIndex == 2;

    public bool MirrorApplicable => SelectedModeIndex != 2 || SelectedWinnerIndex is 1 or 2;

    internal static Func<string, string, IReadOnlyList<SyncProfile>, CancellationToken, IReadOnlyList<BatchPairRow>> PairBuilder { get; set; } = ProfileBatch.BuildPairs;

    internal Task PendingRebuild { get; private set; } = Task.CompletedTask;

    public BatchCreateProfilesDialogViewModel(
        ISettingsStore settings,
        IFilePicker filePicker,
        IUiDispatcher uiDispatcher,
        ILogger? logger = null)
    {
        _settings = settings;
        _filePicker = filePicker;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _existing = SyncProfileStore.Load(settings);
        PathSuggest = settings.GetBool(SettingsKeys.SyncPathSuggest, AppDefaults.SyncPathSuggestDefault);

        _sourceParent = settings.GetStringValue(SettingsKeys.BatchSource) ?? string.Empty;
        _destParent = settings.GetStringValue(SettingsKeys.BatchDest) ?? string.Empty;
        _selectedModeIndex = Math.Clamp(settings.GetInt(SettingsKeys.BatchMode, settings.GetInt(SettingsKeys.SyncMode)), 0, Modes.Count - 1);
        _selectedWinnerIndex = Math.Clamp(settings.GetInt(SettingsKeys.BatchWinner, settings.GetInt(SettingsKeys.SyncWinner)), 0, Winners.Count - 1);
        _mirror = settings.GetBool(SettingsKeys.BatchMirror, settings.GetBool(SettingsKeys.SyncMirror));
        _selectedSort = Math.Clamp(settings.GetInt(SettingsKeys.BatchSort), 0, SortOptions.Count - 1);
        _exclusions = settings.GetStringValue(SettingsKeys.BatchExclusions) ?? FallbackExclusions(settings);

        Rebuild();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BatchPairRow.Include))
        {
            return;
        }

        NotifySelectionChanged();
    }

    private static string FallbackExclusions(ISettingsStore settings)
    {
        var exclusions = (settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim();

        if (exclusions.Length == 0)
        {
            exclusions = (settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty).Trim();
        }

        return exclusions;
    }

    private void Browse(Action<string> assign)
    {
        if (_filePicker.PickFolder("Выберите каталог") is { } path)
        {
            assign(path);
        }
    }

    private void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectionSummary));
        OnPropertyChanged(nameof(CreateCaption));
        OnPropertyChanged(nameof(AllSelected));
        CreateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseSource()
    {
        Browse(path => SourceParent = path);
    }

    [RelayCommand]
    private void BrowseDest()
    {
        Browse(path => DestParent = path);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        CancelPendingRebuild();

        CreatedProfiles = Rows
            .Where(row => row.Include && row.IsSelectable)
            .Select(row => new SyncProfile
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                Name = row.Name,
                Left = row.Left,
                Right = row.Right,
                Mode = SelectedModeIndex,
                Winner = SyncProfile.WinnerFromIndex(SelectedWinnerIndex),
                Mirror = Mirror,
                Exclusions = Exclusions.Trim(),
            })
            .ToList();

        RequestClose?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        CancelPendingRebuild();
        RequestClose?.Invoke(this, false);
    }

    public void Dispose()
    {
        CancelPendingRebuild();
    }

    [RelayCommand]
    private void CycleMode()
    {
        SelectedModeIndex = (SelectedModeIndex + 1) % Modes.Count;
    }

    [RelayCommand]
    private void SwapPaths()
    {
        (SourceParent, DestParent) = (DestParent, SourceParent);
    }

    partial void OnSourceParentChanged(string value)
    {
        _settings.SetValue(SettingsKeys.BatchSource, value);
        Rebuild();
    }

    partial void OnDestParentChanged(string value)
    {
        _settings.SetValue(SettingsKeys.BatchDest, value);
        Rebuild();
    }

    partial void OnSelectedModeIndexChanged(int value)
    {
        _settings.SetInt(SettingsKeys.BatchMode, value);
        OnPropertyChanged(nameof(WinnerApplicable));
        OnPropertyChanged(nameof(MirrorApplicable));
        OnPropertyChanged(nameof(DirectionIconKind));
        OnPropertyChanged(nameof(DirectionHint));
    }

    partial void OnSelectedWinnerIndexChanged(int value)
    {
        _settings.SetInt(SettingsKeys.BatchWinner, value);
        OnPropertyChanged(nameof(MirrorApplicable));
    }

    partial void OnMirrorChanged(bool value)
    {
        _settings.SetBool(SettingsKeys.BatchMirror, value);
    }

    partial void OnExclusionsChanged(string value)
    {
        _settings.SetValue(SettingsKeys.BatchExclusions, value);
    }

    private void Rebuild()
    {
        CancellationTokenSource? current = null;
        int generation;
        var source = SourceParent;
        var dest = DestParent;
        bool empty;

        lock (_rebuildGate)
        {
            var previous = _rebuildCts;
            generation = ++_rebuildGeneration;
            empty = _closed || string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(dest);

            if (empty)
            {
                _rebuildCts = null;
            }
            else
            {
                current = new();
                _rebuildCts = current;
            }

            previous?.Cancel();
        }

        if (empty)
        {
            _isRebuilding = false;
            ApplyRows([]);
            PendingRebuild = Task.CompletedTask;
            return;
        }

        _isRebuilding = true;
        CreateCommand.NotifyCanExecuteChanged();
        PendingRebuild = RebuildCoreAsync(source, dest, generation, current!);
    }

    private async Task RebuildCoreAsync(string source, string dest, int generation, CancellationTokenSource cts)
    {
        var token = cts.Token;
        IReadOnlyList<BatchPairRow> pairs = [];

        try
        {
            var builder = PairBuilder;
            pairs = await Task.Run(() => builder(source, dest, _existing, token), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            _logger?.ScheduleBatchBuildFailed(exception, source);
        }
        finally
        {
            lock (_rebuildGate)
            {
                if (ReferenceEquals(_rebuildCts, cts))
                {
                    _rebuildCts = null;
                }

                cts.Dispose();
            }
        }

        if (token.IsCancellationRequested || IsStale(generation))
        {
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (IsStale(generation))
            {
                return;
            }

            _isRebuilding = false;
            ApplyRows(pairs);
        });
    }

    private void ApplyRows(IReadOnlyList<BatchPairRow> pairs)
    {
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();

        foreach (var row in ProfileBatch.Sort(pairs, SelectedSort))
        {
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        NotifySelectionChanged();
    }

    private void CancelPendingRebuild()
    {
        lock (_rebuildGate)
        {
            _closed = true;
            _rebuildGeneration++;
            _rebuildCts?.Cancel();
            _rebuildCts = null;
        }
    }

    private bool IsStale(int generation)
    {
        lock (_rebuildGate)
        {
            return _closed || generation != _rebuildGeneration;
        }
    }

    private bool CanCreate()
    {
        return !_isRebuilding && SelectedCount > 0;
    }
}
