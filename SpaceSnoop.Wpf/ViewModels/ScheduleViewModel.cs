using MahApps.Metro.IconPacks;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ScheduleViewModel(ISettingsStore settings)
    : ObservableObject, IPageHeader, IPageRefresh
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
    private bool _exists;

    [ObservableProperty]
    private string _message = string.Empty;

    public IReadOnlyList<string> Intervals { get; } = ["Ежедневно", "Каждый час", "При входе в систему"];

    public string PageTitle => "Расписание";

    public string PageDescription =>
        "Периодический автозапуск синхронизации через Планировщик Windows с текущими настройками страницы «Синхронизация».";

    public string? RefreshTooltip => "Проверить задачу в Планировщике";

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

    ICommand IPageRefresh.RefreshCommand => RefreshStatusCommand;

    public void Refresh()
    {
        Exists = SyncScheduler.Exists();
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(MirrorWarning));
    }

    [RelayCommand]
    private void RefreshStatus()
    {
        Refresh();
    }

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
            ? Applied("Расписание сохранено.")
            : $"Не удалось создать задачу: {error}";
    }

    [RelayCommand(CanExecute = nameof(Exists))]
    private void Remove()
    {
        Message = SyncScheduler.Remove(out var error)
            ? Applied("Расписание удалено.")
            : $"Не удалось удалить задачу: {error}";
    }

    private string Applied(string message)
    {
        Exists = SyncScheduler.Exists();
        return message;
    }
}
