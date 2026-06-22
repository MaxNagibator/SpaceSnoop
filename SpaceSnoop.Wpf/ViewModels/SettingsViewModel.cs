using KeepShell.Services;
using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Wpf.ViewModels;

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

    public SettingsViewModel(
        ThemeViewModel theme,
        ShellPreferences shell,
        OperationPreferences operations,
        ScanPreferences scan,
        ISettingsStore settings,
        ILogger<SettingsViewModel> logger)
    {
        Theme = theme;
        Shell = shell;
        Operations = operations;
        Scan = scan;
        _settings = settings;
        _logger = logger;

        Theme.PropertyChanged += OnThemePropertyChanged;
    }

    public ThemeViewModel Theme { get; }

    public ShellPreferences Shell { get; }

    public OperationPreferences Operations { get; }

    public ScanPreferences Scan { get; }

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

    public IReadOnlyList<EnumOption<BackdropKind>> BackdropOptions { get; } =
    [
        new(BackdropKind.None, "Нет"),
        new(BackdropKind.Mica, "Mica"),
        new(BackdropKind.Acrylic, "Acrylic"),
        new(BackdropKind.MicaAlt, "Mica Alt"),
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

    public EnumOption<BackdropKind> SelectedBackdropOption
    {
        get => BackdropOptions.First(o => o.Value == Shell.Backdrop);
        set
        {
            if (value.Value != Shell.Backdrop)
            {
                Shell.Backdrop = value.Value;
                OnPropertyChanged();
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

                                            Скопировать туда текущие настройки, логи и журналы?
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
    private void OpenSettingsFile()
    {
        try
        {
            _settings.Flush();

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
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
