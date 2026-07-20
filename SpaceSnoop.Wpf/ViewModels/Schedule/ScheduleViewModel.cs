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

    public ScheduleViewModel(ISettingsStore settings, IDialogService dialogs, ILogger<ScheduleViewModel> logger)
    {
        Settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        ReloadProfiles();
        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProfiles));
        Settings.Changed += OnSettingsChanged;
    }

    public ISettingsStore Settings { get; }

    public IReadOnlyList<string> Modes { get; } = ["Слева направо", "Справа налево", "Двусторонний"];

    public IReadOnlyList<string> Winners { get; } = ["Новее", "Слева", "Справа"];

    public IReadOnlyList<string> Intervals { get; } = ["Ежедневно", "Каждый час", "При входе в систему"];

    public ObservableCollection<SyncProfileViewModel> Profiles { get; } = [];

    public ObservableCollection<ScheduleRunEntry> History { get; } = [];

    public bool HasProfiles => Profiles.Count > 0;

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

    internal static bool LineHasErrors(string line)
    {
        return !line.Contains(", 0 ошибок", StringComparison.Ordinal);
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
