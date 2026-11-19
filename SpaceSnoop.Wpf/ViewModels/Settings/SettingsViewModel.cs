using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed record EnumOption<T>(T Value, string Label) where T : struct, Enum
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed partial class SettingsViewModel : ObservableObject, IPageHeader
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AgentBackends _agentBackends;
    private readonly IClipboardService _clipboard;
    private readonly IShellLauncher _shell;
    private readonly IApplicationLifetime _lifetime;
    private readonly ToastNotifier _notifier;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(McpConnectSnippet))]
    private int _selectedMcpFormatIndex;

    public IReadOnlyDictionary<string, SettingsResetPlan> ResetPlans { get; }

    public IReadOnlyDictionary<string, SettingsResetField> ResetFields { get; }

    public SettingsSectionList Sections { get; } = CreateSections();

    public ThemeSelectionViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public OperationPreferences Operations { get; }

    public ScanPreferences Scan { get; }

    public UpdatePreferences Update { get; }

    public AppUpdateViewModel Updater { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public AgentPreferences Agent { get; }

    public AgentModelSelector AgentModel { get; }

    public IReadOnlyList<SegmentOption> AgentBackendOptions => SettingsOptions.AgentBackends;

    public int SelectedAgentBackendIndex
    {
        get => AgentBackendChoice.Order.IndexOf(Agent.Backend);
        set
        {
            if (value < 0 || value >= AgentBackendChoice.Order.Length || AgentBackendChoice.Order[value] == Agent.Backend)
            {
                return;
            }

            Agent.Backend = AgentBackendChoice.Order[value];
            NotifyAgentBackendChanged();
        }
    }

    public string AgentCliPathLabel => $"Путь к {_agentBackends.Current.CliName}.exe";

    public string AgentConsentLabel => $"Согласие на отправку данных в {_agentBackends.Current.DisplayName} дано";

    public string AgentConsentHint => $"Согласие даётся каждому CLI отдельно. Снимите галку, чтобы отозвать его для {_agentBackends.Current.DisplayName} – чат спросит согласие перед следующим сообщением.";

    public string AgentModelHint => _agentBackends.Current.ModelHint;

    public bool McpElevatedWarning => Mcp.Enabled && AdminElevation.IsElevated;

    public IReadOnlyList<SegmentOption> McpConnectFormats { get; } =
    [
        new(PackIconLucideKind.Braces, "JSON", "Фрагмент конфигурации MCP-клиента (mcpServers)"),
        new(PackIconLucideKind.Bot, "Claude Code", "Команда claude mcp add"),
        new(PackIconLucideKind.SquareTerminal, "Codex", "Блок для ~/.codex/config.toml"),
        new(PackIconLucideKind.SquareCode, "OpenCode", "Блок для opencode.json"),
        new(PackIconLucideKind.Link, "Адрес", "Адрес и заголовок для клиента, который спрашивает их формой"),
    ];

    public string McpEndpointUrl => $"http://127.0.0.1:{Mcp.Port}{AppDefaults.McpEndpointPath}";

    public string McpConnectSnippet => McpConnectSnippets.For(SelectedMcpFormatIndex, McpEndpointUrl, Mcp.Token);

    public IReadOnlyList<string> UpdateRepositoryPresets { get; } =
    [
        "MaxNagibator/SpaceSnoop",
        "TheVSAKeeper/SpaceSnoop",
    ];

    public string PageTitle => "Настройки";

    public string PageDescription => "Параметры приложения. Изменения сохраняются автоматически.";

    public IReadOnlyList<EnumOption<StartupPage>> StartupOptions => SettingsOptions.StartupPages;

    public IReadOnlyList<EnumOption<StartupAdminMode>> StartupAdminOptions => SettingsOptions.StartupAdminModes;

    public IReadOnlyList<EnumOption<DeleteMode>> DeleteModeOptions => SettingsOptions.DeleteModes;

    public IReadOnlyList<EnumOption<CompressionLevel>> CompressionOptions => SettingsOptions.CompressionLevels;

    public IReadOnlyList<EnumOption<GitFolderPromptChoice>> GitFolderOptions => SettingsOptions.GitFolders;

    public EnumOption<StartupPage> SelectedStartupOption
    {
        get => StartupOptions.First(o => o.Value == Shell.StartupPage);
        set
        {
            if (value.Value != Shell.StartupPage)
            {
                Shell.StartupPage = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public EnumOption<StartupAdminMode> SelectedStartupAdminOption
    {
        get => StartupAdminOptions.FirstOrDefault(o => o.Value == Shell.StartupAdmin) ?? StartupAdminOptions[0];
        set
        {
            if (value.Value != Shell.StartupAdmin)
            {
                Shell.StartupAdmin = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public EnumOption<DeleteMode> SelectedDeleteModeOption
    {
        get => DeleteModeOptions.First(o => o.Value == Operations.DeleteMode);
        set
        {
            if (value.Value != Operations.DeleteMode)
            {
                Operations.DeleteMode = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public EnumOption<CompressionLevel> SelectedCompressionOption
    {
        get => CompressionOptions.First(o => o.Value == Operations.ArchiveCompression);
        set
        {
            if (value.Value != Operations.ArchiveCompression)
            {
                Operations.ArchiveCompression = value.Value;
                OnPropertyChanged();
            }
        }
    }

    public EnumOption<GitFolderPromptChoice> SelectedGitFolderOption
    {
        get => GitFolderOptions.First(o => o.Value == _settings.GetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Ask));
        set
        {
            _settings.SetEnum(SettingsKeys.SyncGitFolders, value.Value);
            OnPropertyChanged();
            RefreshResetAvailability();
        }
    }

    public SettingsViewModel(
        ThemeSelectionViewModel theme,
        ShellPreferences shell,
        OperationPreferences operations,
        ScanPreferences scan,
        UpdatePreferences update,
        AppUpdateViewModel updater,
        McpPreferences mcp,
        McpServerHost mcpServer,
        AgentPreferences agent,
        AgentModelSelector agentModels,
        AgentBackends agentBackends,
        ISettingsStore settings,
        IDialogService dialogs,
        IClipboardService clipboard,
        IShellLauncher shellLauncher,
        IApplicationLifetime lifetime,
        ToastNotifier notifier,
        ILogger<SettingsViewModel> logger)
    {
        Agent = agent;
        AgentModel = agentModels;
        _agentBackends = agentBackends;
        Theme = theme;
        Shell = shell;
        Operations = operations;
        Scan = scan;
        Update = update;
        Updater = updater;
        Mcp = mcp;
        McpServer = mcpServer;
        _settings = settings;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _shell = shellLauncher;
        _lifetime = lifetime;
        _notifier = notifier;
        _logger = logger;

        ResetPlans = SettingsResetCatalog.Build(
            theme,
            shell,
            scan,
            operations,
            update,
            mcp,
            agent,
            settings,
            ResetField);

        SettingsResetCatalog.EnsureExhaustive(Sections, ResetPlans);

        ResetFields = ResetPlans.Values
            .SelectMany(plan => plan.Fields)
            .ToDictionary(field => field.Key, StringComparer.Ordinal);

        Theme.PropertyChanged += OnThemePropertyChanged;
        Mcp.PropertyChanged += OnMcpPropertyChanged;
        Shell.PropertyChanged += OnPreferencesPropertyChanged;
        Scan.PropertyChanged += OnPreferencesPropertyChanged;
        Operations.PropertyChanged += OnPreferencesPropertyChanged;
        Update.PropertyChanged += OnPreferencesPropertyChanged;
        Agent.PropertyChanged += OnPreferencesPropertyChanged;

        Sections.Restore(settings.GetStringValue(SettingsKeys.SettingsSection));
        Sections.PropertyChanged += OnSectionsPropertyChanged;
    }

    public static SettingsSectionList CreateSections()
    {
        return new(
            new SettingsSection(
                "appearance",
                "Внешний вид",
                PackIconLucideKind.Palette,
                "тема оформления светлая тёмная tarkov как в системе windows масштаб шрифта размер текста показывать заголовок страницы показывать всплывающие уведомления тосты показывать производительность в строке состояния отклик память диагностика"),
            new SettingsSection(
                "startup",
                "Запуск",
                PackIconLucideKind.Power,
                "стартовая страница свёрнутое боковое меню навигация рейл свернуть права администратора при запуске предупреждение"),
            new SettingsSection(
                "scan",
                "Сканирование",
                PackIconLucideKind.HardDrive,
                "многопоточный обход число параллельных потоков потоки параллелизм учитывать учёт тип типа носителя ssd hdd читать чтение таблицу таблицы ntfs вместо обхода каталогов mft жёсткие ссылки движок администратор подкаталог только при сканировании диска целиком диск целиком освобождать освобождение прежний прежнего результат результата перед повторным сканом освободить предупреждать предупреждение перед повторным сканом повторный скан спрашивать возвращать возврат память памяти системе после скана вернуть замирание интенсивность тепловой подсветки тепловая подсветка выделять файлы в проводнике не запускать открытие файлов",
                [
                    new("parallel", "Многопоточность"),
                    new("mft", "Таблица NTFS"),
                    new("memory", "Память"),
                    new("view", "Подсветка и проводник"),
                ]),
            new SettingsSection(
                "sync",
                "Синхронизация",
                PackIconLucideKind.FolderSync,
                "автодополнение путей затираемый файл в корзину корзина перезапись git-папки .git git репозиторий исключения по умолчанию glob паттерны группировать группируемые каталоги плоский вид группировка служебных каталогов",
                [
                    new("paths", "Ввод путей"),
                    new("recycle", "Корзина"),
                    new("exclusions", "Исключения и группировка"),
                ]),
            new SettingsSection(
                "delete",
                "Удаление",
                PackIconLucideKind.Trash2,
                "спрашивать подтверждение перед удалением куда удалять помеченные элементы корзина безвозвратно"),
            new SettingsSection(
                "archive",
                "Архивация",
                PackIconLucideKind.FileArchive,
                "уровень сжатия .zip zip сжатие упаковать после упаковки убирать оригинал в корзину"),
            new SettingsSection(
                "update",
                "Обновления",
                PackIconLucideKind.Download,
                "репозиторий обновлений проверять обновления при запуске проверка автоматически автоматическое скачивать новую версию рядом с программой скачивание изменения github релизы changelog"),
            new SettingsSection(
                "storage",
                "Файлы и хранение",
                PackIconLucideKind.Folder,
                "расположение данных хранить файлы приложения в папке appdata файл настроек portable settings.toml путь логи журналы"),
            new SettingsSection(
                "mcp",
                "Доступ для агентов",
                PackIconLucideKind.Plug,
                "включить сервер разрешить изменяющие операции порт токен доступа подключение mcp json cli адрес агент"),
            new SettingsSection(
                "agent",
                "Агент-чат",
                PackIconLucideKind.MessageCircle,
                "показывать страницу страница чата видимость истории согласие на отправку данных в claude code codex opencode antigravity agy дано записывать запись транскрипт транскрипта хода выбранный cli модель свой слаг модели глубина рассуждений путь к claude.exe codex.exe opencode.exe agy.exe шнырь"));
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SettingsSectionList.SelectedPath) && Sections.SelectedPath is { Length: > 0 } path)
        {
            _settings.SetValue(SettingsKeys.SettingsSection, path);
        }
    }

    private void OnMcpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshResetAvailability();

        if (e.PropertyName is nameof(McpPreferences.Port) or nameof(McpPreferences.Token))
        {
            OnPropertyChanged(nameof(McpEndpointUrl));
            OnPropertyChanged(nameof(McpConnectSnippet));
        }

        if (e.PropertyName == nameof(McpPreferences.Enabled))
        {
            OnPropertyChanged(nameof(McpElevatedWarning));
        }
    }

    private void OnThemePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshResetAvailability();
    }

    private void NotifyAgentBackendChanged()
    {
        OnPropertyChanged(nameof(SelectedAgentBackendIndex));
        OnPropertyChanged(nameof(AgentCliPathLabel));
        OnPropertyChanged(nameof(AgentModelHint));
        OnPropertyChanged(nameof(AgentConsentLabel));
        OnPropertyChanged(nameof(AgentConsentHint));
    }

    [RelayCommand]
    private void SetUpdateRepository(string repository)
    {
        Update.Repository = repository;
    }

    [RelayCommand]
    private void CopyMcpConnection()
    {
        _clipboard.TrySetText(McpConnectSnippet);
    }
}
