using KeepShell.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

public sealed partial class ScheduleViewModel : ObservableObject, IPageHeader, IPageRefresh
{
    private readonly IDialogService _dialogs;
    private readonly ILogger<ScheduleViewModel> _logger;

    private bool _migrated;
    private bool _persisting;

    [ObservableProperty]
    private int _bulkModeIndex = -1;

    [ObservableProperty]
    private int _bulkWinnerIndex = -1;

    [ObservableProperty]
    private int _bulkIntervalIndex = -1;

    [ObservableProperty]
    private string _bulkTime = "03:00";

    [ObservableProperty]
    private string _bulkExclusions = string.Empty;

    [ObservableProperty]
    private string _bulkMessage = string.Empty;

    public ScheduleViewModel(ISettingsStore settings, IDialogService dialogs, ILogger<ScheduleViewModel> logger)
    {
        Settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        ReloadProfiles();

        Profiles.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasProfiles));
            NotifySelectionChanged();
        };

        Settings.Changed += OnSettingsChanged;
    }

    public ISettingsStore Settings { get; }

    public IReadOnlyList<SegmentOption> Modes => SyncOptions.Modes;

    public IReadOnlyList<SegmentOption> Winners => SyncOptions.Winners;

    public IReadOnlyList<string> Intervals { get; } = ["Ежедневно", "Каждый час", "При входе в систему"];

    public ObservableCollection<SyncProfileViewModel> Profiles { get; } = [];

    public ObservableCollection<ScheduleRunEntry> History { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

    public int SelectedCount => Profiles.Count(profile => profile.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionSummary => $"Выбрано профилей: {SelectedCount} из {Profiles.Count}";

    public bool HasHistory => History.Count > 0;

    public string PageTitle => "Расписание";

    public string PageDescription =>
        "Профили автосинхронизации через Планировщик Windows: каждый со своими каталогами, направлением и временем запуска.";

    public string? RefreshTooltip => "Обновить статусы и историю";

    ICommand IPageRefresh.RefreshCommand => RefreshStatusesCommand;

    public void Refresh()
    {
        _ = RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        EnsureMigrated();

        var profiles = Profiles.ToArray();
        var statuses = await Task.Run(() => Array.ConvertAll(profiles, profile => SyncScheduler.Query(profile.TaskName)));

        for (var i = 0; i < profiles.Length; i++)
        {
            profiles[i].ApplyStatus(statuses[i]);
        }

        LoadHistory();
    }

    public void Persist()
    {
        _persisting = true;

        try
        {
            SyncProfileStore.Save(Settings, Profiles.Select(profile => profile.ToModel()));
        }
        finally
        {
            _persisting = false;
        }
    }

    public void RemoveProfile(SyncProfileViewModel profile)
    {
        Profiles.Remove(profile);
        Persist();
        _logger.ScheduleProfileRemoved(profile.DisplayName);
    }

    public void LogSaved(string name, bool enabled)
    {
        _logger.ScheduleProfileSaved(name, enabled);
    }

    public void LogRunNow(string name)
    {
        _logger.ScheduleProfileRunNow(name);
    }

    public void LogTaskFailed(string name, string error)
    {
        _logger.ScheduleTaskFailed(name, error);
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));

        BulkModeIndex = -1;
        BulkWinnerIndex = -1;
        BulkIntervalIndex = -1;
        BulkMessage = string.Empty;
    }

    internal static bool LineHasErrors(string line)
    {
        return !line.Contains(", 0 ошибок", StringComparison.Ordinal);
    }

    partial void OnBulkModeIndexChanged(int value)
    {
        if (value < 0)
        {
            return;
        }

        if (!ConfirmDirectionChange(value))
        {
            BulkModeIndex = -1;
            return;
        }

        ApplyToSelected(profile => profile.SelectedModeIndex = value, $"направление «{SyncOptions.Modes[value].Text}»");
    }

    partial void OnBulkWinnerIndexChanged(int value)
    {
        if (value < 0)
        {
            return;
        }

        ApplyToSelected(profile => profile.SelectedWinnerIndex = value, $"победитель «{SyncOptions.Winners[value].Text}»");
    }

    [RelayCommand]
    private void SelectAllProfiles()
    {
        SetSelection(true);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SetSelection(false);
    }

    [RelayCommand]
    private void BulkMirrorOn()
    {
        SetMirror(true);
    }

    [RelayCommand]
    private void BulkMirrorOff()
    {
        SetMirror(false);
    }

    [RelayCommand]
    private void BulkEnable()
    {
        SetEnabled(true);
    }

    [RelayCommand]
    private void BulkDisable()
    {
        SetEnabled(false);
    }

    [RelayCommand]
    private void BulkApplyExclusions()
    {
        var value = BulkExclusions.Trim();

        ApplyToSelected(
            profile => profile.Exclusions = value,
            value.Length == 0 ? "исключения очищены" : $"исключения «{value}»");
    }

    [RelayCommand]
    private void BulkApplySchedule()
    {
        if (BulkIntervalIndex < 0)
        {
            BulkMessage = "Выберите периодичность.";
            return;
        }

        var interval = BulkIntervalIndex;
        var time = BulkTime.Trim();
        var timeApplicable = interval != 2;

        if (timeApplicable && !SyncProfile.IsValidTime(time))
        {
            BulkMessage = "Время укажите в формате ЧЧ:ММ, например 03:00.";
            return;
        }

        ApplyToSelected(
            profile =>
            {
                profile.SelectedIntervalIndex = interval;

                if (timeApplicable)
                {
                    profile.Time = time;
                }
            },
            $"расписание «{Intervals[interval]}»",
            reschedule: true);
    }

    [RelayCommand]
    private void BulkDelete()
    {
        var targets = SelectedProfiles();

        if (targets.Length == 0)
        {
            return;
        }

        var choice = StyledMessageBox.Show($"""
                                            Удалить выбранные профили ({targets.Length}) и их задачи в Планировщике?

                                            Синхронизированные файлы не затрагиваются.
                                            """,
            "Удаление профилей",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (choice != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (var profile in targets)
        {
            SyncScheduler.Remove(profile.TaskName, out _);
            Profiles.Remove(profile);
        }

        Persist();
        BulkMessage = string.Empty;
        _logger.ScheduleBulkRemoved(targets.Length);
    }

    private SyncProfileViewModel[] SelectedProfiles()
    {
        return Profiles.Where(profile => profile.IsSelected).ToArray();
    }

    private void SetSelection(bool value)
    {
        foreach (var profile in Profiles)
        {
            profile.IsSelected = value;
        }
    }

    private bool ConfirmDirectionChange(int mode)
    {
        var mirrored = SelectedProfiles().Count(profile => profile.Mirror && profile.SelectedModeIndex != mode);

        if (mirrored == 0)
        {
            return true;
        }

        var side = mode switch
        {
            1 => "слева",
            2 => "на проигравшей стороне",
            _ => "справа",
        };

        var choice = StyledMessageBox.Show($"""
                                            У выбранных профилей с включённым «Зеркалом» ({mirrored}) смена направления меняет сторону удаления: лишнее будет уходить в корзину {side}.

                                            Сменить направление?
                                            """,
            "Смена направления",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return choice == MessageBoxResult.Yes;
    }

    private void SetMirror(bool value)
    {
        var selected = SelectedProfiles();
        var targets = Array.FindAll(selected, profile => profile.MirrorApplicable);
        var skipped = selected.Length - targets.Length;

        if (targets.Length == 0)
        {
            BulkMessage = $"Зеркало неприменимо ни к одному из выбранных ({skipped}): в двустороннем режиме нужен победитель «Слева» или «Справа».";
            return;
        }

        ApplyTo(targets, profile => profile.Mirror = value, value ? "зеркало включено" : "зеркало выключено");

        if (skipped > 0)
        {
            BulkMessage += $", пропущено {skipped} (зеркало неприменимо)";
        }
    }

    private void SetEnabled(bool value)
    {
        var targets = SelectedProfiles();

        if (targets.Length == 0)
        {
            return;
        }

        // TODO: задачи Планировщика переписываются синхронно на UI-потоке; при десятках профилей выносить в Task.Run с индикатором прогресса
        var applied = targets.Count(profile => profile.ApplyEnabled(value));

        Persist();

        BulkMessage = value
            ? $"Включено: {applied} из {targets.Length}" + (applied < targets.Length ? " (остальные не прошли проверку, причина в карточке)" : string.Empty)
            : $"Выключено: {applied}";

        _logger.ScheduleBulkApplied(value ? "включение" : "выключение", applied);
    }

    private void ApplyToSelected(Action<SyncProfileViewModel> apply, string what, bool reschedule = false)
    {
        ApplyTo(SelectedProfiles(), apply, what, reschedule);
    }

    private void ApplyTo(SyncProfileViewModel[] targets, Action<SyncProfileViewModel> apply, string what, bool reschedule = false)
    {
        if (targets.Length == 0)
        {
            BulkMessage = "Ни один профиль не подходит для этого действия.";
            return;
        }

        foreach (var profile in targets)
        {
            apply(profile);
        }

        Persist();

        if (reschedule)
        {
            foreach (var profile in targets)
            {
                if (profile.Enabled)
                {
                    profile.ApplySchedule();
                }
            }
        }

        BulkMessage = $"Применено к {targets.Length}: {what}";
        _logger.ScheduleBulkApplied(what, targets.Length);
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (!_persisting && key == SettingsKeys.ScheduleProfiles)
        {
            ReloadProfiles();
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var model = new SyncProfile
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = $"Профиль {Profiles.Count + 1}",
            Left = (Settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim(),
            Right = (Settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim(),
            Mode = Settings.GetInt(SettingsKeys.SyncMode),
            Mirror = Settings.GetBool(SettingsKeys.SyncMirror),
            Winner = SyncProfile.WinnerFromIndex(Settings.GetInt(SettingsKeys.SyncWinner)),
            Exclusions = (Settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim(),
        };

        var profile = new SyncProfileViewModel(this, model) { IsEditing = true };
        Profiles.Add(profile);
        Persist();
    }

    [RelayCommand]
    private async Task CreateBatch()
    {
        var dialog = new BatchCreateProfilesDialogViewModel(Settings);

        if (!await _dialogs.ShowAsync(dialog) || dialog.CreatedProfiles.Count == 0)
        {
            return;
        }

        foreach (var model in dialog.CreatedProfiles)
        {
            Profiles.Add(new(this, model));
        }

        Persist();
        _logger.ScheduleBatchCreated(dialog.CreatedProfiles.Count);
    }

    [RelayCommand]
    private Task RefreshStatuses()
    {
        return RefreshAsync();
    }

    private void EnsureMigrated()
    {
        if (_migrated)
        {
            return;
        }

        _migrated = true;

        if (Profiles.Count > 0)
        {
            return;
        }

        var left = (Settings.GetStringValue(SettingsKeys.SyncLeft) ?? string.Empty).Trim();
        var right = (Settings.GetStringValue(SettingsKeys.SyncRight) ?? string.Empty).Trim();
        var legacyExists = SyncScheduler.Exists(SyncScheduler.LegacyTaskName);

        if (!legacyExists && (left.Length == 0 || right.Length == 0))
        {
            return;
        }

        var exclusions = (Settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim();

        if (exclusions.Length == 0)
        {
            exclusions = (Settings.GetStringValue(SettingsKeys.DefaultExclusions) ?? string.Empty).Trim();
        }

        var model = new SyncProfile
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            Name = "По умолчанию",
            Left = left,
            Right = right,
            Mode = Settings.GetInt(SettingsKeys.SyncMode),
            Mirror = Settings.GetBool(SettingsKeys.SyncMirror),
            Winner = SyncProfile.WinnerFromIndex(Settings.GetInt(SettingsKeys.SyncWinner)),
            Exclusions = exclusions,
            Enabled = legacyExists,
        };

        Profiles.Add(new(this, model));
        Persist();

        if (!legacyExists)
        {
            return;
        }

        SyncScheduler.Remove(SyncScheduler.LegacyTaskName, out _);
        SyncScheduler.Create(SyncScheduler.TaskNameFor(model.Id), ScheduleInterval.Daily, new(3, 0, 0), $"{AppInfo.SyncArgument} {model.Id}", out _);
        _logger.ScheduleProfileMigrated(model.Name);
    }

    private void LoadHistory()
    {
        History.Clear();

        var lines = SyncLog.ReadTail(
            static line => line.StartsWith('[') && line.Contains("Автосинхронизация"),
            40);

        foreach (var line in lines)
        {
            History.Add(new(line, LineHasErrors(line)));
        }

        OnPropertyChanged(nameof(HasHistory));
    }

    private void ReloadProfiles()
    {
        Profiles.Clear();

        foreach (var model in SyncProfileStore.Load(Settings))
        {
            Profiles.Add(new(this, model));
        }

        OnPropertyChanged(nameof(HasProfiles));
    }
}

public sealed record ScheduleRunEntry(string Text, bool HasErrors);
