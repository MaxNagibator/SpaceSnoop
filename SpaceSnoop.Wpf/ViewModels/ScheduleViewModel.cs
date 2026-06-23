using KeepShell.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ScheduleViewModel : ObservableObject, IPageHeader, IPageRefresh
{
    private readonly IDialogService _dialogs;
    private readonly ILogger<ScheduleViewModel> _logger;

    private bool _migrated;

    public ScheduleViewModel(ISettingsStore settings, IDialogService dialogs, ILogger<ScheduleViewModel> logger)
    {
        Settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        foreach (var model in SyncProfileStore.Load(settings))
        {
            Profiles.Add(new(this, model));
        }

        Profiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProfiles));
    }

    public ISettingsStore Settings { get; }

    public IReadOnlyList<string> Modes { get; } = ["Слева направо", "Справа налево", "Двусторонний"];

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
        SyncProfileStore.Save(Settings, Profiles.Select(profile => profile.ToModel()));
    }

    public bool Confirm(string title, string message)
    {
        return _dialogs.Confirm(title, message);
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
            Exclusions = (Settings.GetStringValue(SettingsKeys.SyncExclusions) ?? string.Empty).Trim(),
        };

        var profile = new SyncProfileViewModel(this, model) { IsEditing = true };
        Profiles.Add(profile);
        Persist();
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

        var path = Path.Combine(AppStorage.DataDirectory, AppInfo.SyncLogFileName);

        if (!File.Exists(path))
        {
            OnPropertyChanged(nameof(HasHistory));
            return;
        }

        try
        {
            var lines = File.ReadLines(path)
                .Where(static line => line.StartsWith('[') && line.Contains("Автосинхронизация"))
                .Reverse()
                .Take(40);

            foreach (var line in lines)
            {
                History.Add(new(line, LineHasErrors(line)));
            }
        }
        catch (IOException)
        {
            // TODO: журнал может быть занят пишущим процессом – пропускаем, обновится позже
        }

        OnPropertyChanged(nameof(HasHistory));
    }
}

public sealed record ScheduleRunEntry(string Text, bool HasErrors);
