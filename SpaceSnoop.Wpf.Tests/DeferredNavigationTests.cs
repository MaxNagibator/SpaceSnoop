using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Chat;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class DeferredNavigationTests
{
    private DirectoryInfo _root = null!;
    private ISettingsStore _settings = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register(typeof(App).Assembly);
        _root = Directory.CreateTempSubdirectory("spacesnoop-navigation-");
        _settings = new MemorySettings();
        _settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        _logging = KeepShellLogging.Bootstrap(new() { LogsDirectory = _root.FullName, FileNamePrefix = AppInfo.LogFilePrefix });
        _services = App.ConfigureServices(_settings, _logging);
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _logging.Dispose();
        _root.Delete(true);
    }

    [TestCase(SectionKey.Scan)]
    [TestCase(SectionKey.Overview)]
    public void Последняя_страница_определяет_когда_создаётся_обзор(string lastPage)
    {
        _settings.SetEnum(SettingsKeys.StartupPage, StartupPage.LastUsed);
        _settings.SetValue(SettingsKeys.LastPage, lastPage);
        var tracked = new TrackingProvider(_services);
        var shell = ActivatorUtilities.CreateInstance<ShellViewModel>(_services, new DeferredPage<OverviewViewModel>(tracked));
        var item = shell.Sections.Single(x => x.Key == SectionKey.Overview);

        Assert.That(shell.CurrentSectionKey, Is.EqualTo(lastPage));
        Assert.That(tracked.Resolutions, Is.EqualTo(lastPage == SectionKey.Overview ? 1 : 0));

        shell.SelectCommand.Execute(item);
        var overview = _services.GetRequiredService<OverviewViewModel>();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell.Selected, Is.SameAs(item));
            Assert.That(shell.CurrentPageHeader, Is.SameAs(overview));
            Assert.That(shell.CurrentPageRefresh, Is.SameAs(overview));
            Assert.That(shell.CurrentPageStatus, Is.SameAs(overview));
        }
    }

    [Test]
    public void Первый_переход_в_обзор_показывает_ранее_завершённое_сравнение()
    {
        var profile = new SyncProfile
        {
            Id = "one",
            Name = "Первый",
            Left = _root.CreateSubdirectory("left").FullName,
            Right = _root.CreateSubdirectory("right").FullName,
        };
        SyncProfileStore.Save(_settings, [profile]);
        var shell = _services.GetRequiredService<ShellViewModel>();
        var sync = _services.GetRequiredService<SyncViewModel>();
        sync.ApplyProfile(profile);
        VisualTestHost.Run(() => sync.Operations.CompareCommand.ExecuteAsync(null));

        Assert.That(sync.Operations.Result, Is.Not.Null);
        Assert.That(shell.TryNavigate(SectionKey.Overview), Is.True);
        var overview = _services.GetRequiredService<OverviewViewModel>();
        Assert.That(overview.Rows.Rows.Single().Comparison, Is.SameAs(sync.Operations.Result));
    }

    [Test]
    public void Настройки_и_чат_запускают_сервер_только_при_включении_MCP()
    {
        var server = _services.GetRequiredService<McpServerController>();
        server.Apply();
        var settings = _services.GetRequiredService<SettingsViewModel>();
        var chat = _services.GetRequiredService<ChatViewModel>();
        var preferences = _services.GetRequiredService<McpPreferences>();
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            preferences.Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        preferences.RegenerateToken();
        Assert.That(settings.McpServer, Is.SameAs(chat.Gates.McpServer));
        Assert.That(settings.McpServer, Is.SameAs(server));
        Assert.That(server.CreatedHost, Is.Null);
        Assert.That(_services.GetRequiredService<Lazy<McpBridge>>().IsValueCreated, Is.False);

        VisualTestHost.Run(async () =>
        {
            await ApplyAndWaitAsync(() => preferences.Enabled = true, () => server.IsRunning);
            Assert.That(server.Endpoint, Is.EqualTo($"http://127.0.0.1:{preferences.Port}{AppDefaults.McpEndpointPath}"));
            var host = server.CreatedHost;
            await ApplyAndWaitAsync(() => preferences.Token = string.Empty, () => server.LastError is not null);
            Assert.That(server.IsRunning, Is.False);
            await ApplyAndWaitAsync(preferences.RegenerateToken, () => server.IsRunning && server.LastError is null);
            await ApplyAndWaitAsync(() => preferences.Enabled = false, () => server.Endpoint is null);
            Assert.That(server.CreatedHost, Is.SameAs(host));
        });

        async Task ApplyAndWaitAsync(Action change, Func<bool> reached)
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnChanged(object? sender, PropertyChangedEventArgs args)
            {
                if (reached())
                {
                    completed.TrySetResult();
                }
            }

            server.PropertyChanged += OnChanged;
            try
            {
                change();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                server.PropertyChanged -= OnChanged;
            }
        }
    }

    private sealed class TrackingProvider(IServiceProvider services) : IServiceProvider
    {
        public int Resolutions { get; private set; }

        public object? GetService(Type serviceType)
        {
            Resolutions++;
            return services.GetService(serviceType);
        }
    }
}
