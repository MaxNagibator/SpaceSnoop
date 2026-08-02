using KeepShell.Services;
using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncSetupViewModel : ObservableObject
{
    private static readonly SyncMode[] ModeOrder = [SyncMode.LeftToRight, SyncMode.RightToLeft, SyncMode.Bidirectional];
    private static readonly SyncWinner[] WinnerOrder = [SyncWinner.Newest, SyncWinner.Left, SyncWinner.Right];

    private readonly ISettingsStore _settings;
    private readonly OperationPreferences _operations;
    private readonly Func<bool> _canRun;

    private string? _activeProfileId;
    private bool _suppressPersist;

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
    private bool _verify = AppDefaults.SyncVerifyDefault;

    public SyncSetupViewModel(
        ISettingsStore settings,
        IDialogService dialogs,
        OperationPreferences operations,
        Func<bool> canRun,
        Action<string> setStatus)
    {
        _settings = settings;
        _operations = operations;
        _canRun = canRun;

        Profiles = new(settings, dialogs, BuildCurrentProfile, RaiseProfileSelected, canRun, setStatus);
        LoadSettings();
        Profiles.Load();
    }

    public event Action? PathChanged;

    public event Action? ModeChanged;

    public event Action<SyncProfile>? ProfileSelected;

    public SyncQuickProfilesViewModel Profiles { get; }

    public OperationPreferences Preferences => _operations;

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

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

    public bool ExclusionsEmpty => string.IsNullOrWhiteSpace(Exclusions);

    internal string? ActiveProfileId => _activeProfileId;

    internal SyncMode CurrentMode => ModeOrder[Math.Clamp(SelectedModeIndex, 0, ModeOrder.Length - 1)];

    internal SyncWinner CurrentWinner => WinnerOrder[Math.Clamp(SelectedWinnerIndex, 0, WinnerOrder.Length - 1)];

    public void Apply(SyncProfile profile)
    {
        LeftPath = profile.Left;
        RightPath = profile.Right;
        Exclusions = profile.Exclusions;
        SelectedModeIndex = Math.Clamp(profile.Mode, 0, ModeOrder.Length - 1);
        Mirror = profile.Mirror;
        SelectedWinnerIndex = SyncProfile.IndexOfWinner(profile.Winner);
        _activeProfileId = profile.Id;
    }

    public void NotifyBusyChanged()
    {
        Profiles.NotifyCanSaveChanged();
        BrowseLeftCommand.NotifyCanExecuteChanged();
        BrowseRightCommand.NotifyCanExecuteChanged();
    }

    internal string DirectionText()
    {
        return CurrentMode switch
        {
            SyncMode.RightToLeft => "справа налево",
            SyncMode.Bidirectional => "в обе стороны",
            _ => "слева направо",
        };
    }

    private static bool PathMissing(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path.Trim());
    }

    private static void Browse(Action<string> assign)
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

    private bool CanRun()
    {
        return _canRun();
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

    partial void OnLeftPathChanged(string value)
    {
        Persist(SettingsKeys.SyncLeft, value);
        LeftPathInvalid = PathMissing(value);
        _activeProfileId = null;
        Profiles.MarkCurrent();
        PathChanged?.Invoke();
    }

    partial void OnRightPathChanged(string value)
    {
        Persist(SettingsKeys.SyncRight, value);
        RightPathInvalid = PathMissing(value);
        _activeProfileId = null;
        Profiles.MarkCurrent();
        PathChanged?.Invoke();
    }

    partial void OnExclusionsChanged(string value)
    {
        OnPropertyChanged(nameof(ExclusionsEmpty));
        Persist(SettingsKeys.SyncExclusions, value);
        Profiles.MarkCurrent();
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
        ModeChanged?.Invoke();
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
        ModeChanged?.Invoke();
    }

    partial void OnVerifyChanged(bool value)
    {
        Persist(SettingsKeys.SyncVerify, value ? "true" : "false");
    }

    partial void OnMirrorChanged(bool value)
    {
        Persist(SettingsKeys.SyncMirror, value ? "true" : "false");
        Profiles.MarkCurrent();
        OnPropertyChanged(nameof(MirrorHint));
        OnPropertyChanged(nameof(MirrorDeletes));
        ModeChanged?.Invoke();
    }

    private void RaiseProfileSelected(SyncProfile profile)
    {
        ProfileSelected?.Invoke(profile);
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
        Exclusions = _settings.GetStringValue(SettingsKeys.SyncExclusions) ?? _operations.DefaultExclusions;

        SelectedModeIndex = Math.Clamp(_settings.GetInt(SettingsKeys.SyncMode), 0, ModeOrder.Length - 1);
        Mirror = _settings.GetBool(SettingsKeys.SyncMirror);
        SelectedWinnerIndex = Math.Clamp(_settings.GetInt(SettingsKeys.SyncWinner), 0, WinnerOrder.Length - 1);
        Verify = _settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);

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
}
