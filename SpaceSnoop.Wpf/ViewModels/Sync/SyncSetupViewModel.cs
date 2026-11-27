using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncSetupViewModel : ObservableObject
{
    private static readonly SyncMode[] ModeOrder = [SyncMode.LeftToRight, SyncMode.RightToLeft, SyncMode.Bidirectional];
    private static readonly SyncWinner[] WinnerOrder = [SyncWinner.Newest, SyncWinner.Left, SyncWinner.Right];

    private readonly ISettingsStore _settings;
    private readonly IFilePicker _filePicker;
    private readonly Func<bool> _canRun;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Lock _checkGate = new();

    private bool _suppressPersist;
    private int _leftCheckGeneration;
    private int _rightCheckGeneration;
    private CancellationTokenSource? _leftCheckCts;
    private CancellationTokenSource? _rightCheckCts;

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

    public event Action? InputChanged;

    public event Action? ModeChanged;

    public event Action<SyncProfile>? ProfileSelected;

    public SyncQuickProfilesViewModel Profiles { get; }

    public OperationPreferences Preferences { get; }

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

    public string WinnerHint => "Победитель решает изменённые и спорные файлы; при зеркале – что удалять на проигравшей стороне.";

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

    internal string? ActiveProfileId { get; private set; }

    internal SyncMode CurrentMode => ModeOrder[Math.Clamp(SelectedModeIndex, 0, ModeOrder.Length - 1)];

    internal SyncWinner CurrentWinner => WinnerOrder[Math.Clamp(SelectedWinnerIndex, 0, WinnerOrder.Length - 1)];

    internal static Func<string, bool> DirectoryExists { get; set; } = Directory.Exists;

    internal int NetworkDebounceMs { get; set; } = AppDefaults.PathProbeDebounceMs;

    internal int ProbeTimeoutMs { get; set; } = AppDefaults.PathProbeTimeoutMs;

    internal Task PendingLeftCheck { get; private set; } = Task.CompletedTask;

    internal Task PendingRightCheck { get; private set; } = Task.CompletedTask;

    public SyncSetupViewModel(
        ISettingsStore settings,
        IDialogService dialogs,
        OperationPreferences operations,
        IFilePicker filePicker,
        Func<bool> canRun,
        Action<string> setStatus,
        IUiDispatcher uiDispatcher)
    {
        _settings = settings;
        Preferences = operations;
        _filePicker = filePicker;
        _canRun = canRun;
        _uiDispatcher = uiDispatcher;

        Profiles = new(settings, dialogs, BuildCurrentProfile, RaiseProfileSelected, canRun, setStatus, uiDispatcher);
        LoadSettings();
        Profiles.Load();
        RememberKnownPaths();
    }

    public void Apply(SyncProfile profile)
    {
        NetworkDiscovery.Remember(profile.Left);
        NetworkDiscovery.Remember(profile.Right);
        LeftPath = profile.Left;
        RightPath = profile.Right;
        Exclusions = profile.Exclusions;
        SelectedModeIndex = Math.Clamp(profile.Mode, 0, ModeOrder.Length - 1);
        Mirror = profile.Mirror;
        SelectedWinnerIndex = SyncProfile.IndexOfWinner(profile.Winner);
        ActiveProfileId = profile.Id;
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

    private Task CheckPathAsync(string value, bool isLeft)
    {
        var trimmed = value.Trim();
        int generation;
        CancellationTokenSource cts;

        lock (_checkGate)
        {
            generation = isLeft ? ++_leftCheckGeneration : ++_rightCheckGeneration;
            var previous = isLeft ? _leftCheckCts : _rightCheckCts;
            previous?.Cancel();
            previous?.Dispose();

            if (trimmed.Length == 0 || UncPath.IsIncompleteUnc(trimmed))
            {
                if (isLeft)
                {
                    _leftCheckCts = null;
                }
                else
                {
                    _rightCheckCts = null;
                }

                SetPathInvalid(isLeft, false);
                return Task.CompletedTask;
            }

            cts = new();

            if (isLeft)
            {
                _leftCheckCts = cts;
            }
            else
            {
                _rightCheckCts = cts;
            }
        }

        return ProbePathAsync(trimmed, generation, isLeft, DirectoryExists, cts.Token);
    }

    private async Task ProbePathAsync(
        string path,
        int generation,
        bool isLeft,
        Func<string, bool> existsProbe,
        CancellationToken token)
    {
        try
        {
            if (UncPath.IsUnc(path) && NetworkDebounceMs > 0)
            {
                await Task.Delay(NetworkDebounceMs, token).ConfigureAwait(false);
            }

            var result = await PathProbe.CheckAsync(path, existsProbe, ProbeTimeoutMs, token).ConfigureAwait(false);

            if (result.Status == PathProbeStatus.Cancelled || IsCheckStale(isLeft, generation))
            {
                return;
            }

            var exists = result.Status == PathProbeStatus.Exists;

            if (exists)
            {
                NetworkDiscovery.Remember(path);
            }

            _uiDispatcher.Invoke(() =>
            {
                if (IsCheckStale(isLeft, generation))
                {
                    return;
                }

                SetPathInvalid(isLeft, !exists);
            });
        }
        catch (OperationCanceledException)
        {
            return;
        }
    }

    private void RememberKnownPaths()
    {
        NetworkDiscovery.Remember(LeftPath);
        NetworkDiscovery.Remember(RightPath);

        foreach (var item in Profiles.Items)
        {
            if (!item.IsDefault)
            {
                NetworkDiscovery.Remember(item.Model.Left);
                NetworkDiscovery.Remember(item.Model.Right);
            }
        }
    }

    private bool IsCheckStale(bool isLeft, int generation)
    {
        lock (_checkGate)
        {
            return generation != (isLeft ? _leftCheckGeneration : _rightCheckGeneration);
        }
    }

    private void SetPathInvalid(bool isLeft, bool value)
    {
        if (isLeft)
        {
            LeftPathInvalid = value;
        }
        else
        {
            RightPathInvalid = value;
        }
    }

    private void Browse(Action<string> assign)
    {
        if (_filePicker.PickFolder("Выберите каталог") is { } path)
        {
            assign(path);
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
        PendingLeftCheck = CheckPathAsync(value, isLeft: true);
        ActiveProfileId = null;
        Profiles.MarkCurrent();
        InputChanged?.Invoke();
    }

    partial void OnRightPathChanged(string value)
    {
        Persist(SettingsKeys.SyncRight, value);
        PendingRightCheck = CheckPathAsync(value, isLeft: false);
        ActiveProfileId = null;
        Profiles.MarkCurrent();
        InputChanged?.Invoke();
    }

    partial void OnExclusionsChanged(string value)
    {
        OnPropertyChanged(nameof(ExclusionsEmpty));
        Persist(SettingsKeys.SyncExclusions, value);
        Profiles.MarkCurrent();
        InputChanged?.Invoke();
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
        var mirrorTarget = Mirror ? SyncProfile.MirrorTarget(CurrentMode, CurrentWinner, LeftPath, RightPath) : null;
        return new()
        {
            Id = id,
            Name = name,
            Left = LeftPath.Trim(),
            Right = RightPath.Trim(),
            Mode = SelectedModeIndex,
            Mirror = Mirror,
            PermanentNetworkDeleteTarget = string.Equals(existing?.PermanentNetworkDeleteTarget, mirrorTarget, StringComparison.Ordinal)
                ? existing?.PermanentNetworkDeleteTarget
                : null,
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
        Exclusions = _settings.GetStringValue(SettingsKeys.SyncExclusions) ?? Preferences.DefaultExclusions;

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
