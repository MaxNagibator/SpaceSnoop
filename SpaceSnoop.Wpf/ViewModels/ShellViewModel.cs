using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Windows.Input;

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

    private readonly AgentPreferences _agent;

    private readonly NavigationItem _chatItem;

    private readonly NavigationItem _logsItem;

    private readonly IApplicationLifetime _lifetime;

    private readonly CleanupPageViewModel _cleanup;

    [ObservableProperty]
    private bool _isTarkovBootPlaying;

    public ShellViewModel(
        ThemeViewModel theme,
        ScanViewModel scan,
        SyncViewModel sync,
        OverviewViewModel overview,
        ScheduleViewModel schedule,
        CleanupPageViewModel cleanup,
        ChatViewModel chat,
        LogsViewModel logs,
        PerformanceViewModel performance,
        AboutViewModel about,
        SettingsViewModel settingsPage,
        ModalHostViewModel modal,
        AgentPreferences agent,
        ShellPreferences preferences,
        AppUpdateViewModel appUpdate,
        ToastHostViewModel toasts,
        PerformanceHudViewModel hud,
        IApplicationLifetime lifetime)
        : base(modal)
    {
        _lifetime = lifetime;
        _cleanup = cleanup;
        Toasts = toasts;
        Hud = hud;
        Theme = theme;
        Theme.PropertyChanged += OnThemePropertyChanged;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        AppUpdate = appUpdate;

        var scanItem = new NavigationItem("Сканирование", PackIconLucideKind.HardDrive, scan);
        var syncItem = new NavigationItem("Синхронизация", PackIconLucideKind.FolderSync, sync);
        var overviewItem = new NavigationItem("Обзор", PackIconLucideKind.LayoutGrid, overview);
        var scheduleItem = new NavigationItem("Расписание", PackIconLucideKind.CalendarClock, schedule);
        var cleanupItem = new NavigationItem("Очистка", PackIconLucideKind.Trash2, cleanup);
        var logsItem = new NavigationItem("Логи", PackIconLucideKind.ScrollText, logs);
        var performanceItem = new NavigationItem("Диагностика", PackIconLucideKind.Gauge, performance);
        var aboutItem = new NavigationItem("О программе", PackIconLucideKind.Info, about);

        _chatItem = new("Чат", PackIconLucideKind.MessageCircle, chat);
        _logsItem = logsItem;

        Sections.Add(scanItem);
        Sections.Add(syncItem);
        Sections.Add(overviewItem);
        Sections.Add(scheduleItem);
        Sections.Add(cleanupItem);
        Sections.Add(logsItem);
        Sections.Add(performanceItem);
        Sections.Add(aboutItem);

        _sectionByKey = new(StringComparer.OrdinalIgnoreCase)
        {
            [SectionKey.Scan] = scanItem,
            [SectionKey.Sync] = syncItem,
            [SectionKey.Overview] = overviewItem,
            [SectionKey.Schedule] = scheduleItem,
            [SectionKey.Cleanup] = cleanupItem,
            [SectionKey.Chat] = _chatItem,
            [SectionKey.Logs] = logsItem,
            [SectionKey.Performance] = performanceItem,
            [SectionKey.About] = aboutItem,
        };

        _agent = agent;
        _agent.PropertyChanged += OnAgentPreferencesChanged;
        ApplyChatSection();

        overview.OpenInSyncRequested += (profile, comparison) =>
        {
            sync.ApplyProfile(profile, comparison);
            Selected = syncItem;
        };

        sync.ProfileRunCompleted += overview.ApplyProfileRun;

        scan.AskAgentRequested += question => OpenChatWith(chat, question);
        sync.AskAgentRequested += question => OpenChatWith(chat, question);

        chat.NavigationRequested += sectionKey => TryNavigate(sectionKey);

        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settingsPage);
        _sectionByKey[SectionKey.Settings] = _settingsItem;

        IsNavCollapsed = Preferences.NavCollapsed;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Selected = ResolveStartupSection();

        AppUpdate.Start();
    }

    public ToastHostViewModel Toasts { get; }

    public PerformanceHudViewModel Hud { get; }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Preferences { get; }

    public AppUpdateViewModel AppUpdate { get; }

    public bool IsElevated { get; } = AdminElevation.IsElevated;

    public string ElevationCaption => IsElevated ? "Администратор" : "Обычный режим";

    public override IPageHeader? EffectivePageHeader => Preferences.ShowPageHeader ? CurrentPageHeader : null;

    public string? CurrentSectionKey => _sectionByKey.FirstOrDefault(p => p.Value == Selected).Key;

    public ICommand? PageRefreshCommand => CurrentPageRefresh?.RefreshCommand;

    public bool TryNavigate(string sectionKey)
    {
        if (!_sectionByKey.TryGetValue(ResolveAlias(sectionKey), out var item))
        {
            return false;
        }

        Selected = item;
        return true;
    }

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        StatusText = value?.Title ?? "Готов";
        OnPropertyChanged(nameof(PageRefreshCommand));

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

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellPreferences.NavCollapsed))
        {
            IsNavCollapsed = Preferences.NavCollapsed;
        }
    }

    protected override void NavigateToSettings()
    {
        Selected = _settingsItem;
    }

    /// <summary>
    /// Страница чата видна только при включённой настройке: обнаружение CLI стоит запуска процесса,
    /// поэтому при старте оно не делается вовсе – страница сама проверяет наличие CLI при первом открытии.
    /// </summary>
    private void ApplyChatSection()
    {
        var visible = Sections.Contains(_chatItem);

        if (_agent.Enabled == visible)
        {
            return;
        }

        if (_agent.Enabled)
        {
            Sections.Insert(Sections.IndexOf(_logsItem), _chatItem);
            return;
        }

        if (Selected == _chatItem)
        {
            Selected = Sections[0];
        }

        Sections.Remove(_chatItem);
    }

    private void OpenChatWith(ChatViewModel chat, string question)
    {
        if (!Sections.Contains(_chatItem))
        {
            return;
        }

        Selected = _chatItem;
        chat.PrepareQuestion(question);
    }

    private void OnAgentPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentPreferences.Enabled))
        {
            ApplyChatSection();
        }
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
            _lifetime.Shutdown();
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

        // Последней страницей мог остаться выключенный с тех пор «Чат» – его в списке уже нет.
        return target is not null && Sections.Contains(target) ? target : Sections[0];
    }

    private NavigationItem? FindSectionByKey(string key)
    {
        return _sectionByKey.GetValueOrDefault(ResolveAlias(key));
    }

    private string ResolveAlias(string key)
    {
        if (!string.Equals(key, SectionKey.Docker, StringComparison.OrdinalIgnoreCase))
        {
            return key;
        }

        _cleanup.ActivateDocker();

        return SectionKey.Cleanup;
    }

    private NavigationItem? FindSectionByLastPage(string? lastPage)
    {
        if (string.IsNullOrEmpty(lastPage))
        {
            return null;
        }

        if (_sectionByKey.TryGetValue(ResolveAlias(lastPage), out var item))
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
