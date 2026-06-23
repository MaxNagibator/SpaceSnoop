using KeepShell.Services.Modal;
using MahApps.Metro.IconPacks;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ScheduleDialogViewModel(ISettingsStore settings) : ObservableObject, IDialogViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TimeApplicable))]
    private int _selectedIntervalIndex;

    [ObservableProperty]
    private string _time = "03:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(StatusIconKind))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _exists = SyncScheduler.Exists();

    [ObservableProperty]
    private string _message = string.Empty;

    public event EventHandler<bool>? RequestClose;

    public string Title => "Расписание синхронизации";

    public IReadOnlyList<string> Intervals { get; } = ["Ежедневно", "Каждый час", "При входе в систему"];

    public string Summary
    {
        get
        {
            var left = settings.GetStringValue(SettingsKeys.SyncLeft);
            var right = settings.GetStringValue(SettingsKeys.SyncRight);

            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return "Каталоги не заданы на странице «Синхронизация».";
            }

            var arrow = settings.GetInt(SettingsKeys.SyncMode) switch
            {
                1 => "←",
                2 => "↔",
                _ => "→",
            };

            return $"{left}   {arrow}   {right}";
        }
    }

    public bool MirrorWarning => settings.GetBool(SettingsKeys.SyncMirror) && settings.GetInt(SettingsKeys.SyncMode) != 2;

    public bool TimeApplicable => SelectedIntervalIndex != 2;

    public string StatusText => Exists ? "Задача активна в Планировщике Windows." : "Расписание не настроено.";

    public PackIconLucideKind StatusIconKind => Exists ? PackIconLucideKind.CalendarCheck : PackIconLucideKind.CalendarOff;

    [RelayCommand]
    private void Save()
    {
        var interval = SelectedIntervalIndex switch
        {
            1 => ScheduleInterval.Hourly,
            2 => ScheduleInterval.OnLogon,
            _ => ScheduleInterval.Daily,
        };

        var time = TimeSpan.Zero;

        if (interval != ScheduleInterval.OnLogon
            && (!TimeSpan.TryParse(Time, out time) || time < TimeSpan.Zero || time.TotalHours >= 24))
        {
            Message = "Время укажите в формате ЧЧ:ММ, например 03:00.";
            return;
        }

        Message = SyncScheduler.Create(interval, time, out var error)
            ? Refresh("Расписание сохранено.")
            : $"Не удалось создать задачу: {error}";
    }

    [RelayCommand(CanExecute = nameof(Exists))]
    private void Remove()
    {
        Message = SyncScheduler.Remove(out var error)
            ? Refresh("Расписание удалено.")
            : $"Не удалось удалить задачу: {error}";
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke(this, false);
    }

    private string Refresh(string message)
    {
        Exists = SyncScheduler.Exists();
        return message;
    }
}
