using MahApps.Metro.IconPacks;
using System.Globalization;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncProfileViewModel : ObservableObject
{
    private readonly ScheduleViewModel _parent;

    private bool _suppress;
    private int _editSession;
    private bool _confirmedEnabled;

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
    private bool _isSelected;

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

    internal int EnableAttempt { get; private set; }

    internal string TaskName => SyncScheduler.TaskNameFor(Id);

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
        _confirmedEnabled = model.Enabled;
        _suppress = false;
    }

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
            Enabled = _confirmedEnabled,
        };
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

    internal async Task<bool> ValidateEnableAsync(bool value)
    {
        if (!value || await RefuseSchedulingAsync() is not { } reason)
        {
            return true;
        }

        Message = reason;
        CommitEnabled(false);

        return false;
    }

    internal void CommitEnabled(bool value)
    {
        _suppress = true;
        Enabled = value;
        _confirmedEnabled = value;
        _suppress = false;
    }

    internal void ApplySchedule()
    {
        ApplyScheduleOutcome(_parent.Scheduler.Apply(BuildScheduleRequest()));
    }

    internal ScheduleRequest BuildScheduleRequest(bool? enabled = null)
    {
        TimeSpan.TryParse(Time, CultureInfo.InvariantCulture, out var time);

        return new(enabled ?? Enabled, TaskName, ToModel().Interval, time, $"{AppInfo.SyncArgument} {Id}");
    }

    internal void ApplyScheduleOutcome(ScheduleOutcome outcome)
    {
        if (!outcome.Ok)
        {
            Message = $"Не удалось применить расписание: {outcome.Error}";
            _parent.LogTaskFailed(DisplayName, outcome.Error);
            return;
        }

        Message = Enabled
            ? "Расписание сохранено."
            : "Профиль сохранён, автозапуск выключен.";

        _parent.LogSaved(DisplayName, Enabled);
        ApplyStatus(outcome.Status);
    }

    private void Browse(Action<string> assign)
    {
        if (_parent.FilePicker.PickFolder("Выберите каталог") is { } path)
        {
            assign(path);
        }
    }

    partial void OnIsSelectedChanged(bool value)
    {
        _parent.Bulk.NotifySelectionChanged();
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_suppress)
        {
            return;
        }

        _ = ApplyEnabledAsync(value);
    }

    private async Task ApplyEnabledAsync(bool value)
    {
        var attempt = ++EnableAttempt;

        try
        {
            var reason = value ? await RefuseSchedulingAsync() : null;

            if (attempt != EnableAttempt || Enabled != value || !_parent.Profiles.Contains(this))
            {
                return;
            }

            if (reason is null)
            {
                _confirmedEnabled = value;
                ApplySchedule();
            }
            else
            {
                Message = reason;
                CommitEnabled(false);
            }

            _parent.Persist();
        }
        catch (Exception exception)
        {
            Message = $"Не удалось применить расписание: {exception.Message}";
            _parent.LogTaskFailed(DisplayName, exception.Message);
        }
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
        _editSession++;
        Message = string.Empty;
        ConfirmingDelete = false;
        IsSelected = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void Cancel()
    {
        EnableAttempt++;
        _editSession++;
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
            _confirmedEnabled = model.Enabled;
            _suppress = false;
        }

        Message = string.Empty;
        IsEditing = false;
    }

    [RelayCommand]
    private async Task Save()
    {
        var session = _editSession;
        var reason = Enabled ? await RefuseSchedulingAsync() : null;

        if (session != _editSession || !_parent.Profiles.Contains(this))
        {
            return;
        }

        if (Enabled && reason is not null)
        {
            Message = reason;
            return;
        }

        _confirmedEnabled = Enabled;
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

        if (_parent.Shell.Start(exe, AppInfo.SyncArgument, Id))
        {
            _parent.LogRunNow(DisplayName);
            Message = "Запущено в фоне – результат появится в истории.";
        }
        else
        {
            Message = "Не удалось запустить";
        }
    }

    private void Apply()
    {
        _parent.Persist();
        ApplySchedule();
    }

    private async Task<string?> RefuseSchedulingAsync()
    {
        while (true)
        {
            var input = CaptureSchedulingInput();
            var reason = await Task.Run(() => RefuseScheduling(input));

            if (input == CaptureSchedulingInput())
            {
                return reason;
            }
        }
    }

    private SchedulingInput CaptureSchedulingInput()
    {
        return new(LeftPath.Trim(), RightPath.Trim(), TimeApplicable && !SyncProfile.IsValidTime(Time));
    }

    private static string? RefuseScheduling(SchedulingInput input)
    {
        if (input.Left.Length == 0 || input.Right.Length == 0)
        {
            return "Укажите оба каталога перед включением.";
        }

        if (!Directory.Exists(input.Left) || !Directory.Exists(input.Right))
        {
            return "Один из каталогов не существует.";
        }

        if (SyncRootsCheck.Refusal(input.Left, input.Right) is { } refusal)
        {
            return refusal;
        }

        return input.TimeInvalid ? "Время укажите в формате ЧЧ:ММ, например 03:00." : null;
    }

    private readonly record struct SchedulingInput(string Left, string Right, bool TimeInvalid);
}
