using MahApps.Metro.IconPacks;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase
{
    private static readonly Dictionary<string, string> LegacyTitleToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Сканирование"] = SectionKey.Scan,
        ["Синхронизация"] = SectionKey.Sync,
        ["Логи"] = SectionKey.Logs,
        ["О программе"] = SectionKey.About,
    };

    private readonly NavigationItem _settingsItem;

    private readonly Dictionary<string, NavigationItem> _sectionByKey;

    [ObservableProperty]
    private bool _isTarkovBootPlaying;

    public ShellViewModel(
        ThemeViewModel theme,
        ScanViewModel scan,
        SyncViewModel sync,
        LogsViewModel logs,
        AboutViewModel about,
        SettingsViewModel settingsPage,
        ModalHostViewModel modal,
        ShellPreferences preferences)
        : base(modal)
    {
        Theme = theme;
        Theme.PropertyChanged += OnThemePropertyChanged;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;

        var scanItem = new NavigationItem("Сканирование", PackIconLucideKind.HardDrive, scan);
        var syncItem = new NavigationItem("Синхронизация", PackIconLucideKind.FolderSync, sync);
        var logsItem = new NavigationItem("Логи", PackIconLucideKind.ScrollText, logs);
        var aboutItem = new NavigationItem("О программе", PackIconLucideKind.Info, about);

        Sections.Add(scanItem);
        Sections.Add(syncItem);
        Sections.Add(logsItem);
        Sections.Add(aboutItem);

        _sectionByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionKey.Scan] = scanItem,
            [SectionKey.Sync] = syncItem,
            [SectionKey.Logs] = logsItem,
            [SectionKey.About] = aboutItem,
        };

        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settingsPage);

        IsNavCollapsed = Preferences.NavCollapsed;
        Selected = ResolveStartupSection();
    }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Preferences { get; }

    public bool IsElevated { get; } = AdminElevation.IsElevated;

    public string ElevationCaption => IsElevated ? "Администратор" : "Обычный режим";

    public override IPageHeader? EffectivePageHeader => Preferences.ShowPageHeader ? CurrentPageHeader : null;

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        StatusText = value?.Title ?? "Готов";

        if (value is not null && Sections.Contains(value))
        {
            var key = _sectionByKey.FirstOrDefault(p => p.Value == value).Key;
            if (key is not null)
            {
                Preferences.LastPage = key;
            }
        }
    }

    protected override void OnNavCollapsedChanged(bool value)
    {
        Preferences.NavCollapsed = value;
    }

    protected override void NavigateToSettings()
    {
        Selected = _settingsItem;
    }

    private void OnPreferencesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ShellPreferences.ShowPageHeader))
        {
            return;
        }

        OnPropertyChanged(nameof(EffectivePageHeader));
        OnPropertyChanged(nameof(ContentMargin));
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ThemeViewModel.Current) || Theme.Current != AppTheme.Tarkov)
        {
            return;
        }

        IsTarkovBootPlaying = true;
    }

    [RelayCommand]
    private void RestartAsAdmin()
    {
        if (AdminElevation.TryRestartAsAdmin())
        {
            Application.Current.Shutdown();
        }
    }

    private NavigationItem ResolveStartupSection()
    {
        var target = Preferences.StartupPage switch
        {
            StartupPage.Scan => FindSectionByKey(SectionKey.Scan),
            StartupPage.Sync => FindSectionByKey(SectionKey.Sync),
            StartupPage.Logs => FindSectionByKey(SectionKey.Logs),
            StartupPage.LastUsed => FindSectionByLastPage(Preferences.LastPage),
            _ => null,
        };

        return target ?? Sections[0];
    }

    private NavigationItem? FindSectionByKey(string key)
    {
        return _sectionByKey.GetValueOrDefault(key);
    }

    private NavigationItem? FindSectionByLastPage(string? lastPage)
    {
        if (string.IsNullOrEmpty(lastPage))
        {
            return null;
        }

        if (_sectionByKey.TryGetValue(lastPage, out var item))
        {
            return item;
        }

        if (LegacyTitleToKey.TryGetValue(lastPage, out var legacyKey))
        {
            return _sectionByKey.GetValueOrDefault(legacyKey);
        }

        return null;
    }
}
