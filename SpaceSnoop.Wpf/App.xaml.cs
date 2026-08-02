using KeepShell.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf;

public partial class App : Application
{
    private ServiceProvider? _services;
    private KeepShellLogging? _logging;

    protected override void OnStartup(StartupEventArgs e)
    {
        var startedAt = Stopwatch.GetTimestamp();

        base.OnStartup(e);

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(AppStorage.DataDirectory, AppStorage.LogsFolderName),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        var syncIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, AppInfo.SyncArgument, StringComparison.OrdinalIgnoreCase));

        if (syncIndex >= 0)
        {
            var profileId = syncIndex + 1 < e.Args.Length ? e.Args[syncIndex + 1] : null;
            RunHeadlessSync(profileId);
            return;
        }

        var galleryIndex = Array.FindIndex(e.Args, static arg => string.Equals(arg, AppInfo.GalleryArgument, StringComparison.OrdinalIgnoreCase));

        if (galleryIndex >= 0)
        {
            RunGallery(e.Args.Skip(galleryIndex + 1));
            return;
        }

        AttachExceptionHandlers();

        StyledMessageBox.DefaultTitle = AppInfo.Name;

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        StartupSplash? splash = null;

        try
        {
            var settingsPath = Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName);
            ISettingsStore settings = new SettingsStore(settingsPath);
            AppThemes.Register();
            var themeKey = settings.GetStringValue(SettingsKeys.Theme);
            ThemeManager.Apply(string.IsNullOrWhiteSpace(themeKey) ? AppThemes.LightKey : themeKey);
            FontScaleManager.Initialize(settings.GetDouble(SettingsKeys.FontScale, FontScaleManager.DefaultScale));

            ViewLocator.InstallIntoApplication();

            Log.Information("{Marker}...", AppInfo.SessionStartMarker);

            if (TryRestartAsAdministrator(settings))
            {
                Shutdown();
                return;
            }

            splash = new(AppInfo.Name, AppInfo.Version, $"Запуск {AppInfo.Name}", 2, _logging.CreateLogger<StartupSplash>());

            using (splash.StartSpan("Подготовка сервисов..."))
            {
                _services = ConfigureServices(settings, _logging);
            }

            using (splash.StartSpan("Открытие главного окна..."))
            {
                var window = _services.GetRequiredService<MainWindow>();
                MainWindow = window;
                window.Show();

                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }

            var monitor = _services.GetRequiredService<PerformanceMonitor>();
            monitor.ReportStartup(Stopwatch.GetElapsedTime(startedAt));

            _services.GetRequiredService<McpBridge>().Attach(_services.GetRequiredService<ShellViewModel>());
            _services.GetRequiredService<McpServerHost>().Apply();
            monitor.Start();

            _ = Task.Run(() => ScheduleReconciler.Reconcile(settings, _logging.CreateLogger<ScheduleViewModel>()));

            splash.Dispose();
            splash = null;
        }
        catch (Exception ex)
        {
            splash?.Dispose();
            Log.Fatal(ex, $"{AppInfo.Name}.Wpf не смог запуститься");
            StyledMessageBox.Show(ex.ToString(), $"{AppInfo.Name} – ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.GetService<ISettingsStore>()?.Flush();

        _services?.Dispose();
        _logging?.Dispose();
        base.OnExit(e);
    }

    private static bool TryRestartAsAdministrator(ISettingsStore settings)
    {
        if (AdminElevation.IsElevated)
        {
            Log.Information("Приложение запущено от имени администратора");
            return false;
        }

        Log.Information("Приложение запущено без прав администратора");

        if (!settings.GetBool(SettingsKeys.WarnIfNotAdmin, AppDefaults.WarnIfNotAdminDefault))
        {
            Log.Information("Предупреждение о запуске без прав администратора отключено в настройках");
            return false;
        }

        var result = StyledMessageBox.Show("""
                                           Программа запущена не от имени администратора, из-за чего могут отображаться не все директории.
                                           Рекомендуется запустить её от имени администратора.

                                           Перезапустить от имени администратора?
                                           """,
            "Предупреждение",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        return result == MessageBoxResult.Yes && AdminElevation.TryRestartAsAdmin();
    }

    internal static ServiceProvider ConfigureServices(ISettingsStore settings, KeepShellLogging logging)
    {
        var services = new ServiceCollection();

        services.AddSingleton(settings);
        services.AddKeepShellLogging(logging);

        services.AddSingleton<DiskSpaceCalculator>();
        services.AddSingleton<DockerService>();
        services.AddSingleton<ArchiveService>();
        services.AddSingleton<CompareDirectoriesUseCase>();
        services.AddSingleton<ExecuteSyncUseCase>();

        services.AddKeepShell();
        services.AddKeepShellToasts();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<ShellPreferences>();
        services.AddSingleton<OperationPreferences>();
        services.AddSingleton<ScanPreferences>();
        services.AddSingleton<UpdatePreferences>();
        services.AddSingleton<ThemeViewModel>();

        services.AddSingleton(new ErrorReportOptions
        {
            IssueRepo = AppInfo.RepoSlug,
            LogFileGlobs = [AppInfo.LogFileGlob],
            SessionStartMarker = AppInfo.SessionStartMarker,
        });

        services.AddSingleton<ErrorReportService>();

        services.AddSingleton<PerformanceMonitor>();
        services.AddSingleton<PerformanceHudViewModel>();

        services.AddSingleton<ScanInspectorViewModel>();
        services.AddSingleton<ScanNodeFactory>();
        services.AddSingleton<DeleteProgressDialogFactory>();
        services.AddSingleton<ArchiveProgressDialogFactory>();

        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<SyncViewModel>();
        services.AddSingleton<OverviewViewModel>();
        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<DockerViewModel>();
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<PerformanceChartViewModel>();
        services.AddSingleton<ILogsPanel>(static provider => provider.GetRequiredService<PerformanceChartViewModel>());
        services.AddSingleton<PerformanceViewModel>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<AboutViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddSingleton<AppUpdateViewModel>();
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<AgentPreferences>();
        services.AddSingleton<AgentModelSelector>();
        services.AddSingleton(provider => new ChatHistoryStore(provider.GetRequiredService<ILogger<ChatHistoryStore>>()));
        services.AddSingleton(provider => new AgentTranscriptStore(
            provider.GetRequiredService<AgentPreferences>(),
            provider.GetRequiredService<ILogger<AgentTranscriptStore>>()));
        services.AddSingleton<ClaudeAgentBackend>();
        services.AddSingleton<CodexAgentBackend>();
        services.AddSingleton<OpenCodeAgentBackend>();
        services.AddSingleton<AgentBackends>();

        services.AddSingleton<McpPreferences>();
        services.AddSingleton<McpBridge>();
        services.AddSingleton<McpServerHost>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    private void RunGallery(IEnumerable<string> args)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var directory = Path.Combine(AppStorage.DataDirectory, GalleryRun.FolderName);
        var options = GalleryOptions.Parse(args, directory);

        _ = Dispatcher.InvokeAsync(async () =>
        {
            var exitCode = 1;

            try
            {
                var fixture = GalleryFixtures.Create();
                var settingsPath = Path.Combine(fixture.Root, TomlSettingsFile.PrimaryFileName);
                ISettingsStore settings = new SettingsStore(settingsPath);
                SyncProfileStore.Save(settings, GalleryFixtures.Profiles(fixture));

                AppThemes.Register();
                ThemeManager.Apply(AppThemes.LightKey);
                ViewLocator.InstallIntoApplication();

                _services = ConfigureServices(settings, _logging!);

                exitCode = await GalleryRun.RenderAsync(options, _services, fixture, _logging!.CreateLogger<App>());
            }
            catch (Exception ex)
            {
                Log.Fatal(ex.Unwrap(), "Галерея не отрисована");
            }

            Shutdown(exitCode);
        });
    }

    private void RunHeadlessSync(string? profileId)
    {
        var exitCode = 1;

        try
        {
            var settingsPath = Path.Combine(AppStorage.DataDirectory, TomlSettingsFile.PrimaryFileName);
            ISettingsStore settings = new SettingsStore(settingsPath);
            exitCode = HeadlessSync.Run(settings, _logging!, profileId);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Автосинхронизация не смогла запуститься");
        }

        Shutdown(exitCode);
    }

    private void AttachExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Fatal(args.ExceptionObject as Exception, "Необработанное исключение домена");

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error(args.Exception, "Необработанное исключение UI-потока");
            StyledMessageBox.Show($"Произошла непредвиденная ошибка, она записана в журнал.{Environment.NewLine}{Environment.NewLine}{args.Exception.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            args.Handled = true;
        };
    }
}
