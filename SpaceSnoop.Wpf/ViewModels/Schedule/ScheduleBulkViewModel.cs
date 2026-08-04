using KeepShell.Services;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels.Schedule;

public sealed partial class ScheduleBulkViewModel : ObservableObject
{
    private readonly ScheduleViewModel _owner;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;

    [ObservableProperty]
    private int _modeIndex = -1;

    [ObservableProperty]
    private int _winnerIndex = -1;

    [ObservableProperty]
    private int _intervalIndex = -1;

    [ObservableProperty]
    private string _time = "03:00";

    [ObservableProperty]
    private string _exclusions = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelRunCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyScheduleCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private bool _isRunning;

    private CancellationTokenSource? _cts;

    internal ScheduleBulkViewModel(ScheduleViewModel owner, IDialogService dialogs, ILogger logger)
    {
        _owner = owner;
        _dialogs = dialogs;
        _logger = logger;
    }

    public IReadOnlyList<SegmentOption> Modes => _owner.Modes;

    public IReadOnlyList<SegmentOption> Winners => _owner.Winners;

    public IReadOnlyList<string> Intervals => _owner.Intervals;

    public int SelectedCount => _owner.Profiles.Count(profile => profile.IsSelected);

    public bool HasSelection => SelectedCount > 0;

    public string SelectionSummary => $"Выбрано профилей: {SelectedCount} из {_owner.Profiles.Count}";

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionSummary));

        ModeIndex = -1;
        WinnerIndex = -1;
        IntervalIndex = -1;
        Message = string.Empty;
    }

    partial void OnModeIndexChanged(int value)
    {
        if (value < 0)
        {
            return;
        }

        if (!ConfirmDirectionChange(value))
        {
            ModeIndex = -1;
            return;
        }

        ApplyToSelected(profile => profile.SelectedModeIndex = value, $"направление «{SyncOptions.Modes[value].Text}»");
    }

    partial void OnWinnerIndexChanged(int value)
    {
        if (value < 0)
        {
            return;
        }

        ApplyToSelected(profile => profile.SelectedWinnerIndex = value, $"победитель «{SyncOptions.Winners[value].Text}»");
    }

    [RelayCommand]
    private void SelectAll()
    {
        SetSelection(true);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SetSelection(false);
    }

    [RelayCommand]
    private void MirrorOn()
    {
        SetMirror(true);
    }

    [RelayCommand]
    private void MirrorOff()
    {
        SetMirror(false);
    }

    private bool CanRun()
    {
        return !IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task EnableAsync()
    {
        return SetEnabledAsync(true);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task DisableAsync()
    {
        return SetEnabledAsync(false);
    }

    private bool CanCancelRun()
    {
        return IsRunning;
    }

    [RelayCommand(CanExecute = nameof(CanCancelRun))]
    private void CancelRun()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void ApplyExclusions()
    {
        var value = Exclusions.Trim();

        ApplyToSelected(
            profile => profile.Exclusions = value,
            value.Length == 0 ? "исключения очищены" : $"исключения «{value}»");
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyScheduleAsync()
    {
        if (IntervalIndex < 0)
        {
            Message = "Выберите периодичность.";
            return;
        }

        var interval = IntervalIndex;
        var time = Time.Trim();
        var timeApplicable = interval != 2;

        if (timeApplicable && !SyncProfile.IsValidTime(time))
        {
            Message = "Время укажите в формате ЧЧ:ММ, например 03:00.";
            return;
        }

        var targets = SelectedProfiles();

        var changed = ApplyTo(
            targets,
            profile =>
            {
                profile.SelectedIntervalIndex = interval;

                if (timeApplicable)
                {
                    profile.Time = time;
                }
            },
            $"расписание «{Intervals[interval]}»");

        if (!changed)
        {
            return;
        }

        var scheduled = Array.FindAll(targets, profile => profile.Enabled);
        var run = await RewriteTasksAsync(scheduled);

        if (run.Cancelled)
        {
            Message += $"; задачи переписаны у {run.Applied} из {scheduled.Length}, дальше отменено";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DeleteAsync()
    {
        var targets = SelectedProfiles();

        if (targets.Length == 0)
        {
            return;
        }

        var confirm = new ConfirmDialogViewModel(
            "Удаление профилей",
            PackIconLucideKind.Trash2,
            [
                new ConfirmMetricLine("Удалить профилей", $"{targets.Length:N0}", string.Empty, ConfirmMetricTone.Danger),
                new ConfirmTextLine("Вместе с ними уйдут их задачи в Планировщике.", ConfirmTextTone.Muted),
                new ConfirmGapLine(),
                new ConfirmTextLine("Синхронизированные файлы не затрагиваются."),
            ],
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Удалить профили", ConfirmChoiceKind.Destructive),
            ])
        {
            Warning = "Профили удаляются мимо корзины – восстановить их нельзя.",
        };

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        var detached = new List<SyncProfileViewModel>();

        var removed = await RunBatchAsync(targets, async profile =>
        {
            var taskName = profile.TaskName;
            await Task.Run(() => _owner.Scheduler.Remove(taskName), CancellationToken.None);
            detached.Add(profile);

            return true;
        });

        foreach (var profile in detached)
        {
            _owner.Profiles.Remove(profile);
        }

        _owner.Persist();

        Message = removed.Cancelled
            ? $"Удалено профилей: {removed.Applied} из {targets.Length}, дальше отменено"
            : string.Empty;

        _logger.ScheduleBulkRemoved(removed.Applied);
    }

    private SyncProfileViewModel[] SelectedProfiles()
    {
        return _owner.Profiles.Where(profile => profile.IsSelected).ToArray();
    }

    private void SetSelection(bool value)
    {
        foreach (var profile in _owner.Profiles)
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

        return _dialogs.ConfirmWarning("Смена направления", $"""
                                                              У выбранных профилей с включённым «Зеркалом» ({mirrored}) смена направления меняет сторону удаления: лишнее будет уходить в корзину {side}.

                                                              Сменить направление?
                                                              """);
    }

    private void SetMirror(bool value)
    {
        var selected = SelectedProfiles();
        var targets = Array.FindAll(selected, profile => profile.MirrorApplicable);
        var skipped = selected.Length - targets.Length;

        if (targets.Length == 0)
        {
            Message = $"Зеркало неприменимо ни к одному из выбранных ({skipped}): в двустороннем режиме нужен победитель «Слева» или «Справа».";
            return;
        }

        ApplyTo(targets, profile => profile.Mirror = value, value ? "зеркало включено" : "зеркало выключено");

        if (skipped > 0)
        {
            Message += $", пропущено {skipped} (зеркало неприменимо)";
        }
    }

    private async Task SetEnabledAsync(bool value)
    {
        var targets = SelectedProfiles();

        if (targets.Length == 0)
        {
            return;
        }

        var valid = Array.FindAll(targets, profile => profile.ValidateEnable(value));

        var run = await RunBatchAsync(valid, async profile =>
        {
            var request = profile.BuildScheduleRequest(value);
            var outcome = await Task.Run(() => _owner.Scheduler.Apply(request), CancellationToken.None);

            if (outcome.Ok)
            {
                profile.CommitEnabled(value);
            }

            profile.ApplyScheduleOutcome(outcome);

            return outcome.Ok;
        });

        _owner.Persist();

        var rest = valid.Length < targets.Length ? " (остальные не прошли проверку, причина в карточке)" : string.Empty;
        var tail = run.Cancelled ? ", дальше отменено" : string.Empty;

        Message = value
            ? $"Включено: {run.Applied} из {targets.Length}{rest}{tail}"
            : $"Выключено: {run.Applied}{tail}";

        _logger.ScheduleBulkApplied(value ? "включение" : "выключение", run.Applied);
    }

    private Task<BatchRun> RewriteTasksAsync(SyncProfileViewModel[] targets)
    {
        return RunBatchAsync(targets, async profile =>
        {
            var request = profile.BuildScheduleRequest();
            var outcome = await Task.Run(() => _owner.Scheduler.Apply(request), CancellationToken.None);

            profile.ApplyScheduleOutcome(outcome);

            return outcome.Ok;
        });
    }

    private async Task<BatchRun> RunBatchAsync(SyncProfileViewModel[] targets, Func<SyncProfileViewModel, Task<bool>> step)
    {
        if (targets.Length == 0 || IsRunning)
        {
            return new(0, false);
        }

        _cts = new();
        var token = _cts.Token;
        IsRunning = true;

        var applied = 0;
        var cancelled = false;

        try
        {
            foreach (var profile in targets)
            {
                if (token.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                if (await step(profile))
                {
                    applied++;
                }
            }
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }

        return new(applied, cancelled);
    }

    private void ApplyToSelected(Action<SyncProfileViewModel> apply, string what)
    {
        ApplyTo(SelectedProfiles(), apply, what);
    }

    private bool ApplyTo(SyncProfileViewModel[] targets, Action<SyncProfileViewModel> apply, string what)
    {
        if (targets.Length == 0)
        {
            Message = "Ни один профиль не подходит для этого действия.";
            return false;
        }

        foreach (var profile in targets)
        {
            apply(profile);
        }

        _owner.Persist();

        Message = $"Применено к {targets.Length}: {what}";
        _logger.ScheduleBulkApplied(what, targets.Length);

        return true;
    }

    private readonly record struct BatchRun(int Applied, bool Cancelled);
}
