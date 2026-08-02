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
    private static readonly AgentBackendKind[] AgentBackendOrder = [AgentBackendKind.Claude, AgentBackendKind.Codex, AgentBackendKind.OpenCode];

    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly AgentBackends _agentBackends;
    private readonly IClipboardService _clipboard;
    private readonly IShellLauncher _shell;
    private readonly IApplicationLifetime _lifetime;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(McpConnectSnippet))]
    private int _selectedMcpFormatIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchTextEmpty))]
    private string _searchText = string.Empty;

    public SettingsViewModel(
        ThemeViewModel theme,
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
        _logger = logger;

        Theme.PropertyChanged += OnThemePropertyChanged;
        Mcp.PropertyChanged += OnMcpPropertyChanged;

        Sections.Restore(settings.GetStringValue(SettingsKeys.SettingsSection));
        Sections.PropertyChanged += OnSectionsPropertyChanged;
    }

    public SettingsSectionList Sections { get; } = new(
        new SettingsSection("appearance", "Внешний вид", PackIconLucideKind.Palette, "тема оформления светлая тёмная tarkov масштаб шрифта размер текста заголовок страницы уведомления тосты производительность отклик память диагностика"),
        new SettingsSection("startup", "Запуск", PackIconLucideKind.Power, "стартовая страница навигационный рейл свернуть права администратора предупреждение"),
        new SettingsSection("scan", "Сканирование", PackIconLucideKind.HardDrive, "многопоточный обход потоки параллелизм тепловая подсветка интенсивность проводник открытие файлов"),
        new SettingsSection("sync", "Синхронизация", PackIconLucideKind.FolderSync, "исключения glob паттерны автодополнение путей git репозиторий группировка служебных каталогов плоский вид"),
        new SettingsSection("delete", "Удаление", PackIconLucideKind.Trash2, "корзина безвозвратно подтверждение помеченные элементы"),
        new SettingsSection("archive", "Архивация", PackIconLucideKind.FileArchive, "zip сжатие уровень упаковать оригинал корзина"),
        new SettingsSection("update", "Обновления", PackIconLucideKind.Download, "github релизы репозиторий версия проверка скачивание изменения changelog"),
        new SettingsSection("storage", "Файлы и хранение", PackIconLucideKind.Folder, "расположение данных appdata portable settings.toml путь логи журналы"),
        new SettingsSection("mcp", "MCP-сервер", PackIconLucideKind.Plug, "порт токен подключение json cli адрес изменяющие операции агент"),
        new SettingsSection("agent", "Агент-чат", PackIconLucideKind.MessageCircle, "шнырь claude codex opencode cli модель глубина рассуждений транскрипт согласие"));

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public ThemeViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public OperationPreferences Operations { get; }

    public ScanPreferences Scan { get; }

    public UpdatePreferences Update { get; }

    public AppUpdateViewModel Updater { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

    public AgentPreferences Agent { get; }

    public AgentModelSelector AgentModel { get; }

    public IReadOnlyList<SegmentOption> AgentBackendOptions { get; } =
    [
        new(PackIconLucideKind.Bot, "Claude Code", "CLI claude – встроенные инструменты отключаются целиком, у агента только инструменты приложения"),
        new(PackIconLucideKind.SquareTerminal, "Codex", "CLI codex – помимо инструментов приложения агент получает оболочку системы, отключить её нечем"),
        new(PackIconLucideKind.SquareCode, "OpenCode", "CLI opencode – работает по локально настроенной авторизации, встроенные инструменты отключены, у агента только инструменты приложения"),
    ];

    public int SelectedAgentBackendIndex
    {
        get => Array.IndexOf(AgentBackendOrder, Agent.Backend);
        set
        {
            if (value < 0 || value >= AgentBackendOrder.Length || AgentBackendOrder[value] == Agent.Backend)
            {
                return;
            }

            Agent.Backend = AgentBackendOrder[value];
            OnPropertyChanged();
            OnPropertyChanged(nameof(AgentCliPathLabel));
            OnPropertyChanged(nameof(AgentShellWarning));
            OnPropertyChanged(nameof(AgentModelHint));
        }
    }

    public string AgentCliPathLabel => $"Путь к {_agentBackends.Current.CliName}.exe";

    public string AgentModelHint => _agentBackends.Current.ModelHint;

    public bool AgentShellWarning => _agentBackends.Current.HasBuiltInShell;

    public bool McpElevatedWarning => Mcp.Enabled && AdminElevation.IsElevated;

    public IReadOnlyList<SegmentOption> McpConnectFormats { get; } =
    [
        new(PackIconLucideKind.Braces, "JSON", "Фрагмент конфигурации MCP-клиента (mcpServers)"),
        new(PackIconLucideKind.Terminal, "CLI", "Команда Claude Code для добавления сервера"),
        new(PackIconLucideKind.Link, "Адрес", "Адрес и заголовок для клиента, который спрашивает их формой"),
    ];

    public string McpEndpointUrl => $"http://127.0.0.1:{Mcp.Port}{AppDefaults.McpEndpointPath}";

    public string McpConnectSnippet => SelectedMcpFormatIndex switch
    {
        1 => $"claude mcp add --transport http spacesnoop {McpEndpointUrl} --header \"Authorization: Bearer {Mcp.Token}\"",
        2 => $"Адрес: {McpEndpointUrl}{Environment.NewLine}Транспорт: Streamable HTTP{Environment.NewLine}Заголовок: Authorization: Bearer {Mcp.Token}",
        _ => BuildMcpJson(),
    };

    public IReadOnlyList<string> UpdateRepositoryPresets { get; } =
    [
        "MaxNagibator/SpaceSnoop",
        "TheVSAKeeper/SpaceSnoop",
    ];

    public string PageTitle => "Настройки";

    public string PageDescription => "Параметры приложения. Изменения сохраняются автоматически.";

    public string SettingsFilePath => _settings.FilePath;

    public string DataDirectory => AppStorage.DataDirectory;

    public bool StoreInAppData
    {
        get => AppStorage.UseAppData;
        set
        {
            if (value != AppStorage.UseAppData)
            {
                _ = ChangeStorageLocationAsync(value)
                    .ContinueWith(task => _logger.StorageLocationChangeFailed(task.Exception!, AppStorage.DirectoryFor(value)),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
            }

            OnPropertyChanged();
        }
    }

    public IReadOnlyList<EnumOption<AppTheme>> ThemeOptions { get; } =
    [
        new(AppTheme.Light, "Светлая"),
        new(AppTheme.Dark, "Тёмная"),
        new(AppTheme.Tarkov, "Tarkov"),
    ];

    public IReadOnlyList<EnumOption<StartupPage>> StartupOptions { get; } =
    [
        new(StartupPage.LastUsed, "Последняя активная"),
        new(StartupPage.Scan, "Сканирование"),
        new(StartupPage.Sync, "Синхронизация"),
        new(StartupPage.Logs, "Логи"),
    ];

    public IReadOnlyList<EnumOption<DeleteMode>> DeleteModeOptions { get; } =
    [
        new(DeleteMode.RecycleBin, "В корзину"),
        new(DeleteMode.Permanent, "Безвозвратно"),
    ];

    public IReadOnlyList<EnumOption<CompressionLevel>> CompressionOptions { get; } =
    [
        new(CompressionLevel.Optimal, "Оптимальное"),
        new(CompressionLevel.SmallestSize, "Максимальное (медленно)"),
        new(CompressionLevel.Fastest, "Быстрое"),
        new(CompressionLevel.NoCompression, "Без сжатия (только упаковка)"),
    ];

    public IReadOnlyList<EnumOption<GitFolderPromptChoice>> GitFolderOptions { get; } =
    [
        new(GitFolderPromptChoice.Ask, "Спрашивать"),
        new(GitFolderPromptChoice.Skip, "Всегда пропускать"),
        new(GitFolderPromptChoice.Keep, "Синхронизировать"),
    ];

    public EnumOption<AppTheme> SelectedThemeOption
    {
        get => ThemeOptions.First(o => o.Value == Theme.Current);
        set
        {
            if (value.Value != Theme.Current)
            {
                Theme.ApplyCommand.Execute(value.Value);
            }
        }
    }

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
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        Sections.Filter(value);
    }

    private void OnSectionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsSectionList.Selected) && Sections.Selected is not null)
        {
            _settings.SetValue(SettingsKeys.SettingsSection, Sections.Selected.Key);
        }
    }

    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
    }

    private void OnMcpPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
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
        if (e.PropertyName == nameof(ThemeViewModel.Current))
        {
            OnPropertyChanged(nameof(SelectedThemeOption));
        }
    }

    private async Task ChangeStorageLocationAsync(bool useAppData)
    {
        var source = AppStorage.DataDirectory;
        var destination = AppStorage.DirectoryFor(useAppData);
        var place = useAppData ? "в папке AppData" : "рядом с программой";

        var migrate = new ConfirmChoice("Перенести файлы", ConfirmChoiceKind.Primary);

        var confirm = new ConfirmDialogViewModel(
            "Расположение данных",
            PackIconLucideKind.HardDrive,
            [
                new ConfirmTextLine($"Файлы приложения будут храниться {place}:"),
                new ConfirmTextLine(destination, ConfirmTextTone.Muted),
                new ConfirmGapLine(),
                new ConfirmTextLine("Текущие настройки, логи и журналы можно перенести туда или оставить на старом месте."),
                new ConfirmTextLine("После смены приложение перезапустится."),
            ],
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Оставить файлы на месте", ConfirmChoiceKind.Secondary),
                migrate,
            ]);

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        try
        {
            if (ReferenceEquals(confirm.Chosen, migrate))
            {
                _settings.Flush();
                var left = AppStorage.Migrate(source, destination);

                if (left > 0)
                {
                    _logger.StorageSourceFilesLeft(left);
                }
            }

            AppStorage.SetUseAppData(useAppData);
            _logger.StorageLocationChanged(destination);
        }
        catch (Exception exception)
        {
            _logger.StorageLocationChangeFailed(exception, destination);
            _dialogs.Error("Расположение данных", $"Не удалось изменить расположение данных.{Environment.NewLine}{Environment.NewLine}{exception.Message}");

            return;
        }

        var executable = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(executable) && !_shell.Start(executable))
        {
            _dialogs.Error("Расположение данных", "Данные перенесены, но перезапустить программу не удалось – закройте и откройте её сами.");

            return;
        }

        _lifetime.Shutdown();
    }

    [RelayCommand]
    private void SetUpdateRepository(string repository)
    {
        Update.Repository = repository;
    }

    [RelayCommand]
    private void OpenSettingsFile()
    {
        try
        {
            _settings.Flush();
        }
        catch (Exception exception)
        {
            _logger.SettingsFlushFailed(exception, SettingsFilePath);
        }

        _shell.Reveal(SettingsFilePath);
    }

    [RelayCommand]
    private void CopyMcpConnection()
    {
        _clipboard.TrySetText(McpConnectSnippet);
    }

    private string BuildMcpJson()
    {
        return $$"""
                 {
                   "mcpServers": {
                     "spacesnoop": {
                       "type": "http",
                       "url": "{{McpEndpointUrl}}",
                       "headers": {
                         "Authorization": "Bearer {{Mcp.Token}}"
                       }
                     }
                   }
                 }
                 """;
    }

    [RelayCommand]
    private void CopySettingsPath()
    {
        _clipboard.TrySetText(SettingsFilePath);
    }
}
