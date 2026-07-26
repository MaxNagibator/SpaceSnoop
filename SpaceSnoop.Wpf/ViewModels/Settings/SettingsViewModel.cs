using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Diagnostics;
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
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(McpConnectSnippet))]
    private int _selectedMcpFormatIndex;

    public SettingsViewModel(
        ThemeViewModel theme,
        ShellPreferences shell,
        OperationPreferences operations,
        ScanPreferences scan,
        UpdatePreferences update,
        AppUpdateViewModel updater,
        McpPreferences mcp,
        McpServerHost mcpServer,
        ISettingsStore settings,
        ILogger<SettingsViewModel> logger)
    {
        Theme = theme;
        Shell = shell;
        Operations = operations;
        Scan = scan;
        Update = update;
        Updater = updater;
        Mcp = mcp;
        McpServer = mcpServer;
        _settings = settings;
        _logger = logger;

        Theme.PropertyChanged += OnThemePropertyChanged;
        Mcp.PropertyChanged += OnMcpPropertyChanged;
    }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public OperationPreferences Operations { get; }

    public ScanPreferences Scan { get; }

    public UpdatePreferences Update { get; }

    public AppUpdateViewModel Updater { get; }

    public McpPreferences Mcp { get; }

    public McpServerHost McpServer { get; }

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
                ChangeStorageLocation(value);
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

    private void ChangeStorageLocation(bool useAppData)
    {
        var source = AppStorage.DataDirectory;
        var destination = AppStorage.DirectoryFor(useAppData);
        var place = useAppData ? "в папке AppData" : "рядом с программой";

        var choice = StyledMessageBox.Show($"""
                                            Хранить файлы приложения {place}:
                                            {destination}

                                            Перенести туда текущие настройки, логи и журналы (Нет – оставить их на старом месте)?
                                            После смены приложение будет перезапущено.
                                            """,
            "Расположение данных",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
        {
            return;
        }

        try
        {
            if (choice == MessageBoxResult.Yes)
            {
                _settings.Flush();
                AppStorage.Migrate(source, destination);
            }

            AppStorage.SetUseAppData(useAppData);
            _logger.StorageLocationChanged(destination);
        }
        catch (Exception exception)
        {
            _logger.StorageLocationChangeFailed(exception, destination);
            StyledMessageBox.Show($"Не удалось изменить расположение данных.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Расположение данных",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return;
        }

        var executable = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(executable))
        {
            Process.Start(executable);
        }

        Application.Current.Shutdown();
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

            Process.Start(new ProcessStartInfo
            {
                FileName = SystemExecutable.Explorer,
                Arguments = $"/select,\"{SettingsFilePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            _logger.ShowSettingsFileFailed(exception, SettingsFilePath);
        }
    }

    [RelayCommand]
    private void CopyMcpConnection()
    {
        try
        {
            Clipboard.SetText(McpConnectSnippet);
        }
        catch (Exception exception)
        {
            _logger.CopySettingsPathFailed(exception);
        }
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
        try
        {
            Clipboard.SetText(SettingsFilePath);
        }
        catch (Exception exception)
        {
            _logger.CopySettingsPathFailed(exception);
        }
    }
}
