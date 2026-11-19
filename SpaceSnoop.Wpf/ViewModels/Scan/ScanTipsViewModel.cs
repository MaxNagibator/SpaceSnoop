namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanTipsViewModel : ObservableObject
{
    private static readonly ScanTip[] Tips =
    [
        new(
            "Меню по правой кнопке",
            "Щёлкните правой кнопкой по строке дерева или по плитке карты. В меню можно открыть папку в проводнике, пометить её на удаление или упаковать в архив."),
        new(
            "Карта вместо списка",
            "Значок карты в заголовке «Структура каталога» показывает ту же папку плитками. Чем крупнее плитка, тем больше места она занимает."),
        new(
            "Долгий скан можно прервать",
            "Нажмите Esc или кнопку «Остановить». Папку можно будет отсканировать заново в любой момент."),
    ];

    private readonly ISettingsStore _settings;
    private readonly Func<bool> _welcomeVisible;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsVisible))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(Text))]
    [NotifyPropertyChangedFor(nameof(Counter))]
    private ScanTip? _current;

    public bool IsVisible => Current is not null;

    public string Title => Current?.Title ?? string.Empty;

    public string Text => Current?.Text ?? string.Empty;

    public string Counter => Current is null ? string.Empty : $"Совет {Array.IndexOf(Tips, Current) + 1} из {Tips.Length}";

    public ScanTipsViewModel(ISettingsStore settings, Func<bool> welcomeVisible)
    {
        _settings = settings;
        _welcomeVisible = welcomeVisible;
    }

    internal void OnScanCompleted()
    {
        if (Current is not null || _welcomeVisible())
        {
            return;
        }

        var shown = Math.Max(0, _settings.GetInt(SettingsKeys.ScanTipsShown, AppDefaults.ScanTipsShownDefault));

        if (shown >= Tips.Length)
        {
            return;
        }

        _settings.SetInt(SettingsKeys.ScanTipsShown, shown + 1);
        Current = Tips[shown];
    }

    internal void ShowForAutomation(int index)
    {
        Current = Tips[index];
    }

    [RelayCommand]
    private void Dismiss()
    {
        Current = null;
    }
}
