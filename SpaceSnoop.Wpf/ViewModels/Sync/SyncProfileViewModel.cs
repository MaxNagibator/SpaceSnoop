using MahApps.Metro.IconPacks;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncProfileViewModel : ObservableObject
{
    private readonly ScheduleViewModel _parent;

    private bool _suppress;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _leftPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private string _rightPath;

    [ObservableProperty]
    private string _exclusions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(MirrorApplicable))]
    [NotifyPropertyChangedFor(nameof(MirrorWarning))]
    [NotifyPropertyChangedFor(nameof(WinnerApplicable))]
    private int _selectedModeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MirrorApplicable))]
    [NotifyPropertyChangedFor(nameof(MirrorWarning))]
    private int _selectedWinnerIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MirrorWarning))]
    private bool _mirror;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeApplicable))]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private int _selectedIntervalIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScheduleSummary))]
    private string _time;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    private bool _enabled;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _confirmingDelete;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    private bool _isScheduled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    private bool _osEnabled = true;

    [ObservableProperty]
    private string _nextRun = "–";

    [ObservableProperty]
    private string _lastRun = "–";

    [ObservableProperty]
    private string _lastResult = "–";

    [ObservableProperty]
    private bool _isStale;

    public SyncProfileViewModel(ScheduleViewModel parent, SyncProfile model)
    {
        _parent = parent;
        Id = model.Id;

        _suppress = true;
        _name = model.Name;
        _leftPath = model.Left;
        _rightPath = model.Right;
        _exclusions = model.Exclusions;
        _selectedModeIndex = Math.Clamp(model.Mode, 0, 2);
        _selectedWinnerIndex = SyncProfile.IndexOfWinner(model.Winner);
        _mirror = model.Mirror;
        _selectedIntervalIndex = model.Interval switch
        {
            ScheduleInterval.Hourly => 1,
            ScheduleInterval.OnLogon => 2,
            _ => 0,
        };

        _time = model.Time;
        _enabled = model.Enabled;
        _suppress = false;
    }

    public string Id { get; }

    public IReadOnlyList<SegmentOption> Modes => _parent.Modes;

    public IReadOnlyList<SegmentOption> Winners => _parent.Winners;

    public IReadOnlyList<string> Intervals => _parent.Intervals;

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Без названия" : Name;

    public bool TimeApplicable => SelectedIntervalIndex != 2;

    public bool WinnerApplicable => SelectedModeIndex == 2;

    public bool MirrorApplicable => SelectedModeIndex != 2 || SelectedWinnerIndex is 1 or 2;

    public bool MirrorWarning => Mirror && MirrorApplicable;

    public PackIconLucideKind StatusIconKind =>
        Enabled && IsScheduled && OsEnabled ? PackIconLucideKind.CalendarCheck : PackIconLucideKind.CalendarOff;

    public string Summary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(LeftPath) || string.IsNullOrWhiteSpace(RightPath))
            {
                return "Каталоги не заданы.";
            }

            var arrow = SelectedModeIndex switch
            {
                1 => "←",
                2 => "↔",
                _ => "→",
            };

            return $"{LeftPath}   {arrow}   {RightPath}";
        }
    }

    public string ScheduleSummary => SelectedIntervalIndex switch
    {
        1 => $"Каждый час, начиная с {Time}",
        2 => "При входе в систему",
        _ => $"Ежедневно в {Time}",
    };

    public SyncProfile ToModel()
    {
        return new()
        {
            Id = Id,
            Name = Name.Trim(),
            Left = LeftPath.Trim(),
            Right = RightPath.Trim(),
            Mode = SelectedModeIndex,
            Winner = SyncProfile.WinnerFromIndex(SelectedWinnerIndex),
            Mirror = Mirror,
            Exclusions = Exclusions.Trim(),
            Interval = SelectedIntervalIndex switch
            {
                1 => ScheduleInterval.Hourly,
                2 => ScheduleInterval.OnLogon,
                _ => ScheduleInterval.Daily,
            },
            Time = Time.Trim(),
            Enabled = Enabled,
        };
    }

    public void RefreshStatus()
    {
        ApplyStatus(SyncScheduler.Query(TaskName));
    }

    public void ApplyStatus(ScheduleStatus status)
    {
        IsScheduled = status.Exists;
        OsEnabled = !status.Exists || status.Enabled;
        NextRun = status.Exists ? status.NextRun : "–";
        LastRun = status.Exists ? status.LastRun : "–";
        LastResult = status.Exists ? status.LastResultText : "–";
        IsStale = status.Exists && SyncScheduler.IsStale(status.Action, Environment.ProcessPath ?? string.Empty);
    }

    private static void Browse(Action<string> assign)
    {
        var dialog = new OpenFolderDialog { Title = "Выберите каталог" };

        if (dialog.ShowDialog() == true)
        {
            assign(dialog.FolderName);
        }
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        if (value && !ValidateForScheduling(out var reason))
        {
            Message = reason;
            _suppress = true;
            Enabled = false;
            _suppress = false;
            return;
        }

        Apply();
    }

    [RelayCommand]
    private void BrowseLeft()
    {
        Browse(path => LeftPath = path);
    }

    [RelayCommand]
    private void BrowseRight()
    {
        Browse(path => RightPath = path);
    }

    [RelayCommand]
    private void Edit()
    {
        Message = string.Empty;
        ConfirmingDelete = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        var model = SyncProfileStore.Find(_parent.Settings, Id);

        if (model is not null)
        {
            _suppress = true;
            Name = model.Name;
            LeftPath = model.Left;
            RightPath = model.Right;
            Exclusions = model.Exclusions;
            SelectedModeIndex = Math.Clamp(model.Mode, 0, 2);
            SelectedWinnerIndex = SyncProfile.IndexOfWinner(model.Winner);
            Mirror = model.Mirror;
            SelectedIntervalIndex = model.Interval switch
            {
                ScheduleInterval.Hourly => 1,
                ScheduleInterval.OnLogon => 2,
                _ => 0,
            };

            Time = model.Time;
            Enabled = model.Enabled;
            _suppress = false;
        }

        Message = string.Empty;
        IsEditing = false;
    }

    [RelayCommand]
    private void Save()
    {
        if (Enabled && !ValidateForScheduling(out var reason))
        {
            Message = reason;
            return;
        }

        Apply();
        IsEditing = false;
    }

    [RelayCommand]
    private void ArmDelete()
    {
        ConfirmingDelete = true;
    }

    [RelayCommand]
    private void CancelDelete()
    {
        ConfirmingDelete = false;
    }

    [RelayCommand]
    private void Delete()
    {
        SyncScheduler.Remove(TaskName, out _);
        _parent.RemoveProfile(this);
    }

    [RelayCommand]
    private void RunNow()
    {
        _parent.Persist();
        _parent.Settings.Flush();

        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe))
        {
            Message = "Не удалось определить путь к приложению.";
            return;
        }

        try
        {
            var info = new ProcessStartInfo(exe) { UseShellExecute = false };
            info.ArgumentList.Add(AppInfo.SyncArgument);
            info.ArgumentList.Add(Id);
            Process.Start(info);

            _parent.LogRunNow(DisplayName);
            Message = "Запущено в фоне – результат появится в истории.";
        }
        catch (Exception exception)
        {
            Message = $"Не удалось запустить: {exception.Message}";
        }
    }

    private void Apply()
    {
        _parent.Persist();

        if (Enabled)
        {
            TimeSpan.TryParse(Time, out var time);

            if (!SyncScheduler.Create(TaskName, ToModel().Interval, time, $"{AppInfo.SyncArgument} {Id}", out var error))
            {
                Message = $"Не удалось создать задачу: {error}";
                _parent.LogTaskFailed(DisplayName, error);
                return;
            }

            Message = "Расписание сохранено.";
        }
        else
        {
            if (SyncScheduler.Exists(TaskName))
            {
                SyncScheduler.Disable(TaskName, out _);
            }

            Message = "Профиль сохранён, автозапуск выключен.";
        }

        _parent.LogSaved(DisplayName, Enabled);
        RefreshStatus();
    }

    private bool ValidateForScheduling(out string reason)
    {
        var left = LeftPath.Trim();
        var right = RightPath.Trim();

        if (left.Length == 0 || right.Length == 0)
        {
            reason = "Укажите оба каталога перед включением.";
            return false;
        }

        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            reason = "Один из каталогов не существует.";
            return false;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            reason = "Каталоги не должны совпадать или быть вложены друг в друга.";
            return false;
        }

        if (TimeApplicable && (!TimeSpan.TryParse(Time, out var time) || time < TimeSpan.Zero || time.TotalHours >= 24))
        {
            reason = "Время укажите в формате ЧЧ:ММ, например 03:00.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    internal string TaskName => SyncScheduler.TaskNameFor(Id);
}
