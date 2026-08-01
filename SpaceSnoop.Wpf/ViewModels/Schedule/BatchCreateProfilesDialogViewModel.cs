using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

public sealed partial class BatchCreateProfilesDialogViewModel : ObservableObject, IDialogViewModel
{
    private readonly IReadOnlyList<SyncProfile> _existing;
    private readonly ISettingsStore _settings;

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

    [ObservableProperty]
    private int _selectedSort;

    public BatchCreateProfilesDialogViewModel(ISettingsStore settings)
    {
        _settings = settings;
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

    public event EventHandler<bool>? RequestClose;

    public string Title => "Создать пачку профилей";

    public bool PathSuggest { get; }

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

    public IReadOnlyList<string> SortOptions { get; } = ["Имя (А–Я)", "Имя (Я–А)", "Сначала новые"];

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

    public int SelectedCount => Rows.Count(row => row.Include);

    public bool HasRows => Rows.Count > 0;

    public string SelectionSummary => $"Отмечено: {SelectedCount} из {Rows.Count}";

    public string CreateCaption => SelectedCount > 0
        ? $"Создать {Plural.Format(SelectedCount, "профиль", "профиля", "профилей")}"
        : "Создать профили";

    public bool? AllSelected
    {
        get
        {
            if (Rows.Count == 0 || SelectedCount == 0)
            {
                return false;
            }

            return SelectedCount == Rows.Count ? true : null;
        }
        set
        {
            if (value is not bool target)
            {
                return;
            }

            foreach (var row in Rows)
            {
                row.Include = target;
            }
        }
    }

    public bool WinnerApplicable => SelectedModeIndex == 2;

    public bool MirrorApplicable => SelectedModeIndex != 2 || SelectedWinnerIndex is 1 or 2;

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

    private static void Browse(Action<string> assign)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите каталог" };

        if (dialog.ShowDialog() == true)
        {
            assign(dialog.FolderName);
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
        CreatedProfiles = Rows
            .Where(row => row.Include)
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
        RequestClose?.Invoke(this, false);
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
        foreach (var row in Rows)
        {
            row.PropertyChanged -= OnRowChanged;
        }

        Rows.Clear();

        var pairs = ProfileBatch.BuildPairs(SourceParent, DestParent, _existing);

        foreach (var row in ProfileBatch.Sort(pairs, SelectedSort))
        {
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }

        OnPropertyChanged(nameof(HasRows));
        NotifySelectionChanged();
    }

    partial void OnSelectedSortChanged(int value)
    {
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

    private bool CanCreate()
    {
        return SelectedCount > 0;
    }
}
