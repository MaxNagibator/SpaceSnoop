using MahApps.Metro.IconPacks;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ShellViewModel : ShellViewModelBase, IAppNavigator
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

    private readonly ScanViewModel _scan;

    private readonly DeferredPage<CleanupPageViewModel> _cleanup;

    private readonly DeferredPage<SyncViewModel> _sync;

    private readonly DeferredPage<ChatViewModel> _chat;

    private readonly NavigationItem _scanItem;

    private readonly NavigationItem _syncItem;

    public bool IsNavigationLocked => _scan.IsExporting;

    public ToastHostViewModel Toasts { get; }

    public PerformanceHudViewModel Hud { get; }

    public ThemeSelectionViewModel Theme { get; }

    public ShellPreferences Preferences { get; }

    public AppUpdateViewModel AppUpdate { get; }

    public bool IsElevated { get; } = AdminElevation.IsElevated;

    public string ElevationCaption => IsElevated ? "Администратор" : "Обычный режим";

    public override IPageHeader? EffectivePageHeader => Preferences.ShowPageHeader ? CurrentPageHeader : null;

    public string? CurrentSectionKey => Selected?.Key is { Length: > 0 } key ? key : null;

    public ShellViewModel(
        ThemeSelectionViewModel theme,
        ScanViewModel scan,
        DeferredPage<SyncViewModel> sync,
        DeferredPage<OverviewViewModel> overview,
        DeferredPage<ScheduleViewModel> schedule,
        DeferredPage<CleanupPageViewModel> cleanup,
        DeferredPage<ChatViewModel> chat,
        DeferredPage<LogsViewModel> logs,
        DeferredPage<DiagnosticsViewModel> diagnostics,
        DeferredPage<AboutViewModel> about,
        DeferredPage<SettingsViewModel> settingsPage,
        ModalHostViewModel modal,
        AgentPreferences agent,
        ShellPreferences preferences,
        AppUpdateViewModel appUpdate,
        ToastHostViewModel toasts,
        PerformanceHudViewModel hud,
        AppNavigator navigator)
        : base(modal)
    {
        _scan = scan;
        _cleanup = cleanup;
        _sync = sync;
        _chat = chat;
        Toasts = toasts;
        Hud = hud;
        Theme = theme;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesPropertyChanged;
        AppUpdate = appUpdate;

        var scanItem = new NavigationItem("Сканирование", PackIconLucideKind.HardDrive, scan, key: SectionKey.Scan);
        var syncItem = new NavigationItem("Синхронизация", PackIconLucideKind.FolderSync, sync, key: SectionKey.Sync)
        {
            KeepAlive = true,
        };

        var overviewItem = new NavigationItem("Все профили", PackIconLucideKind.LayoutGrid, overview, key: SectionKey.Overview);
        var scheduleItem = new NavigationItem("Расписание", PackIconLucideKind.CalendarClock, schedule, key: SectionKey.Schedule);
        var cleanupItem = new NavigationItem("Очистка", PackIconLucideKind.Trash2, cleanup, key: SectionKey.Cleanup);
        var logsItem = new NavigationItem("Логи", PackIconLucideKind.ScrollText, logs, key: SectionKey.Logs)
        {
            StartsGroup = true,
        };

        var performanceItem = new NavigationItem("Диагностика", PackIconLucideKind.Gauge, diagnostics, key: SectionKey.Performance);
        var aboutItem = new NavigationItem("О программе", PackIconLucideKind.Info, about, key: SectionKey.About);

        _chatItem = new("Чат", PackIconLucideKind.MessageCircle, chat, key: SectionKey.Chat);
        _settingsItem = new("Настройки", PackIconLucideKind.Settings, settingsPage, key: SectionKey.Settings);
        _logsItem = logsItem;
        _scanItem = scanItem;
        _syncItem = syncItem;
        _scan.PropertyChanged += OnScanPropertyChanged;

        Sections.Add(scanItem);
        Sections.Add(syncItem);
        Sections.Add(overviewItem);
        Sections.Add(scheduleItem);
        Sections.Add(cleanupItem);
        Sections.Add(logsItem);
        Sections.Add(performanceItem);
        Sections.Add(aboutItem);

        _sectionByKey = new[]
            {
                scanItem, syncItem, overviewItem, scheduleItem, cleanupItem, _chatItem, logsItem, performanceItem, aboutItem, _settingsItem,
            }
            .ToDictionary(static item => item.Key, StringComparer.OrdinalIgnoreCase);

        _agent = agent;
        _agent.PropertyChanged += OnAgentPreferencesChanged;
        ApplyChatSection();

        navigator.Attach(this);

        IsNavCollapsed = Preferences.NavCollapsed;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Selected = ResolveStartupSection();

        AppUpdate.Start();
    }

    public bool TryNavigate(string sectionKey)
    {
        if (IsNavigationLocked && !string.Equals(sectionKey, SectionKey.Scan, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!_sectionByKey.TryGetValue(ResolveAlias(sectionKey), out var item))
        {
            return false;
        }

        if (ReferenceEquals(item, _chatItem) && !Sections.Contains(_chatItem))
        {
            return false;
        }

        Selected = item;
        return true;
    }

    public void OpenSync(SyncProfile profile, ComparisonResult? comparison)
    {
        if (IsNavigationLocked)
        {
            return;
        }

        _sync.Value.ApplyProfile(profile, comparison);
        Selected = _syncItem;
    }

    public void AskAgent(string question)
    {
        if (IsNavigationLocked || !Sections.Contains(_chatItem))
        {
            return;
        }

        Selected = _chatItem;
        _chat.Value.PrepareQuestion(question);
    }

    protected override void OnSelectionChanged(NavigationItem? value)
    {
        if (IsNavigationLocked && !ReferenceEquals(value, _scanItem))
        {
            Selected = _scanItem;
            return;
        }

        if (value?.Content is DeferredPage deferred)
        {
            var page = deferred.Content;
            CurrentPageStatus = page as IPageStatus;
            CurrentPageHeader = page as IPageHeader;
            CurrentPageRefresh = page as IPageRefresh;
        }

        StatusText = "Готов";

        if (value?.Key is { Length: > 0 } key && Sections.Contains(value))
        {
            Preferences.LastPage = key;
        }
    }

    protected override void OnNavCollapsedChanged(bool value)
    {
        Preferences.NavCollapsed = value;
    }

    protected override void NavigateToSettings()
    {
        if (IsNavigationLocked)
        {
            return;
        }

        Selected = _settingsItem;
    }

    private void OnScanPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanViewModel.IsExporting))
        {
            OnPropertyChanged(nameof(IsNavigationLocked));
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellPreferences.NavCollapsed))
        {
            IsNavCollapsed = Preferences.NavCollapsed;
        }
    }

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

    [RelayCommand]
    private Task RestartAsAdmin()
    {
        return _scan.RestartPrompt.RunAsync();
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

        _cleanup.Value.ActivateDocker();

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
