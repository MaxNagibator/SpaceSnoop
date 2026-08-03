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

    [RelayCommand]
    private void Enable()
    {
        SetEnabled(true);
    }

    [RelayCommand]
    private void Disable()
    {
        SetEnabled(false);
    }

    [RelayCommand]
    private void ApplyExclusions()
    {
        var value = Exclusions.Trim();

        ApplyToSelected(
            profile => profile.Exclusions = value,
            value.Length == 0 ? "исключения очищены" : $"исключения «{value}»");
    }

    [RelayCommand]
    private void ApplySchedule()
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

        foreach (var profile in targets)
        {
            SyncScheduler.Remove(profile.TaskName, out _);
            _owner.Profiles.Remove(profile);
        }

        _owner.Persist();
        Message = string.Empty;
        _logger.ScheduleBulkRemoved(targets.Length);
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

    private void SetEnabled(bool value)
    {
        var targets = SelectedProfiles();

        if (targets.Length == 0)
        {
            return;
        }

        // TODO: задачи Планировщика переписываются синхронно на UI-потоке; при десятках профилей выносить в Task.Run с индикатором прогресса
        var applied = targets.Count(profile => profile.ApplyEnabled(value));

        _owner.Persist();

        var rest = applied < targets.Length ? " (остальные не прошли проверку, причина в карточке)" : string.Empty;

        Message = value
            ? $"Включено: {applied} из {targets.Length}{rest}"
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
            Message = "Ни один профиль не подходит для этого действия.";
            return;
        }

        foreach (var profile in targets)
        {
            apply(profile);
        }

        _owner.Persist();

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

        Message = $"Применено к {targets.Length}: {what}";
        _logger.ScheduleBulkApplied(what, targets.Length);
    }
}
