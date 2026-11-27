using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Settings;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class ThemeUpdateStartupTests
{
    [Test]
    public void Потребитель_не_содержит_ссылок_на_конкретные_пакеты_тем()
    {
        var repoRoot = FindRepoRoot();
        var wpfDir = Path.Combine(repoRoot, "SpaceSnoop.Wpf");
        var packagePattern = new Regex(@"KeepShell\.Themes\.[A-Za-z0-9_]+", RegexOptions.CultureInvariant);

        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(wpfDir, "*.*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(wpfDir, file);
            if (rel.StartsWith("bin", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Path.GetExtension(file) is not (".cs" or ".xaml" or ".csproj")
                || file.EndsWith("_wpftmp.csproj", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var content = File.ReadAllText(file);
            if (packagePattern.IsMatch(content))
            {
                hits.Add(rel);
            }
        }

        Assert.That(hits, Is.Empty, "SpaceSnoop.Wpf должен оставаться полностью theme-agnostic.");
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public void Контейнер_DI_собирает_сервис_обновления_тем_без_циклов()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register(typeof(App).Assembly);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        var tempDir = CreateTempDir();
        try
        {
            using var logging = KeepShellLogging.Bootstrap(new()
            {
                LogsDirectory = Path.Combine(tempDir, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });
            using var provider = App.ConfigureServices(settings, logging);

            var updates = provider.GetRequiredService<ThemePackUpdateService>();
            var packs = provider.GetRequiredService<ThemePacksViewModel>();
            var startup = provider.GetRequiredService<ThemeUpdateStartup>();
            var shell = provider.GetRequiredService<ShellViewModel>();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(updates, Is.Not.Null);
                Assert.That(packs.HasUpdateService, Is.True);
                Assert.That(packs.CanRestartApp, Is.True);
                Assert.That(startup, Is.Not.Null);
                Assert.That(shell, Is.Not.Null);
            }
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Test]
    public async Task Старт_с_пустой_папкой_тем_не_делает_сетевых_запросов_и_не_показывает_тост()
    {
        var tempDir = CreateTempDir();
        try
        {
            ISettingsStore settings = new MemorySettings();
            var loader = new ThemePackLoader(tempDir, static (_, _) => { });
            var catalog = new ThemePackCatalog(new Uri("https://127.0.0.1:1/unreachable/themes.json"));
            var updates = new ThemePackUpdateService(loader, catalog, settings, NullLogger<ThemePackUpdateService>.Instance);

            var toasts = new ToastHostViewModel();
            var notifier = new ToastNotifier(toasts, new ShellPreferences(settings));
            var catalogOpened = 0;
            var restartCalls = 0;

            using var startup = new ThemeUpdateStartup(
                updates.CheckOnStartupAsync,
                notifier,
                () => catalogOpened++,
                () =>
                {
                    restartCalls++;
                    return Task.FromResult(true);
                },
                NullLogger<ThemeUpdateStartup>.Instance);

            await startup.CheckAndNotifyAsync();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(toasts.Toasts, Is.Empty);
                Assert.That(settings.GetStringValue(ThemePackUpdateService.LastCheckedUtcKey), Is.Null);
                Assert.That(catalogOpened, Is.Zero);
                Assert.That(restartCalls, Is.Zero);
            }
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Test]
    public async Task Найденные_обновления_в_ручном_режиме_показывают_тост_с_открытием_каталога()
    {
        ISettingsStore settings = new MemorySettings();
        var toasts = new ToastHostViewModel();
        var notifier = new ToastNotifier(toasts, new ShellPreferences(settings));
        var catalogOpened = 0;

        var update = new ThemePackUpdateInfo(
            "sample.pack",
            "Sample Pack",
            new Version(0, 1, 306),
            new Version(0, 1, 325));
        var result = new ThemePackUpdateCheckResult([update], [], RestartRequired: false, FromCache: false);

        using var startup = new ThemeUpdateStartup(
            _ => Task.FromResult(result),
            notifier,
            () => catalogOpened++,
            () => Task.FromResult(true),
            NullLogger<ThemeUpdateStartup>.Instance);

        await startup.CheckAndNotifyAsync();

        Assert.That(toasts.Toasts, Has.Count.EqualTo(1));
        var toast = toasts.Toasts[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(toast.Message, Is.EqualTo("Доступно обновление тем (1)"));
            Assert.That(toast.ActionText, Is.EqualTo("Открыть каталог"));
            Assert.That(toast.ActionCommand, Is.Not.Null);
        }

        toast.ActionCommand!.Execute(null);
        Assert.That(catalogOpened, Is.EqualTo(1));
    }

    [Test]
    public async Task Автообновление_с_перезапуском_показывает_тост_с_кнопкой_перезапуска()
    {
        ISettingsStore settings = new MemorySettings();
        var toasts = new ToastHostViewModel();
        var notifier = new ToastNotifier(toasts, new ShellPreferences(settings));
        var restartCalls = 0;

        var updated = new ThemePackUpdateInfo(
            "sample.pack",
            "Sample Pack",
            new Version(0, 1, 306),
            new Version(0, 1, 325));
        var result = new ThemePackUpdateCheckResult([], [updated], RestartRequired: true, FromCache: false);

        using var startup = new ThemeUpdateStartup(
            _ => Task.FromResult(result),
            notifier,
            () => { },
            () =>
            {
                restartCalls++;
                return Task.FromResult(true);
            },
            NullLogger<ThemeUpdateStartup>.Instance);

        await startup.CheckAndNotifyAsync();

        Assert.That(toasts.Toasts, Has.Count.EqualTo(1));
        var toast = toasts.Toasts[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(toast.Message, Is.EqualTo("Обновлена тема «Sample Pack» – применится после перезапуска"));
            Assert.That(toast.ActionText, Is.EqualTo("Перезапустить"));
            Assert.That(toast.ActionCommand, Is.Not.Null);
        }

        toast.ActionCommand!.Execute(null);
        Assert.That(restartCalls, Is.EqualTo(1));
    }

    [Test]
    public async Task Безопасный_перезапуск_уважает_отказ_RequestClose_и_запускает_новый_процесс_при_разрешении()
    {
        ISettingsStore settings = new MemorySettings();
        var shell = new FakeShellLauncher();
        var dialogs = new NoopDialogs();
        var lifetime = new FakeApplicationLifetime();

        var declined = await ThemeUpdateStartup.RestartApplicationAsync(
            () => Task.FromResult(false),
            settings,
            shell,
            dialogs,
            lifetime,
            () => @"C:\Program Files\SpaceSnoop\SpaceSnoop.exe");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(declined, Is.False);
            Assert.That(shell.Started, Is.Empty);
            Assert.That(lifetime.ShutdownCalls, Is.Zero);
        }

        var restarted = await ThemeUpdateStartup.RestartApplicationAsync(
            () => Task.FromResult(true),
            settings,
            shell,
            dialogs,
            lifetime,
            () => @"C:\Program Files\SpaceSnoop\SpaceSnoop.exe");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted, Is.True);
            Assert.That(shell.Started, Is.EqualTo(new[] { @"C:\Program Files\SpaceSnoop\SpaceSnoop.exe" }));
            Assert.That(lifetime.ShutdownCalls, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task Неудавшийся_перезапуск_сохраняет_автосохранение_и_закрытие_настроек()
    {
        var tempDir = CreateTempDir();
        try
        {
            var settingsPath = Path.Combine(tempDir, TomlSettingsFile.PrimaryFileName);
            using var store = new SettingsStore(settingsPath);
            var events = new ConcurrentQueue<string>();
            var spy = new SpySettingsStore(store, events);
            var shell = new TrackingShellLauncher(events) { Succeeds = false };
            var dialogs = new NoopDialogs();
            var lifetime = new FakeApplicationLifetime();

            ISettingsStore settings = spy;
            settings.SetInt(SettingsKeys.WindowLeft, 111);

            var restarted = await ThemeUpdateStartup.RestartApplicationAsync(
                () => Task.FromResult(true),
                spy,
                shell,
                dialogs,
                lifetime,
                () => @"C:\Program Files\SpaceSnoop\SpaceSnoop.exe");

            var afterFailedRestart = await File.ReadAllTextAsync(settingsPath);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(restarted, Is.False);
                Assert.That(dialogs.LastErrorMessage, Is.Not.Null);
                Assert.That(lifetime.ShutdownCalls, Is.Zero);
                Assert.That(spy.FlushCalls, Is.EqualTo(1));
                Assert.That(spy.CloseCalls, Is.Zero);
                Assert.That(events.ToArray(), Is.EqualTo(new[] { "flush", "start" }));
                Assert.That(afterFailedRestart, Does.Contain("111"));
            }

            settings.SetInt(SettingsKeys.WindowTop, 222);

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            var afterDebounce = await ReadTextSafeAsync(settingsPath);
            while (DateTime.UtcNow < deadline && !afterDebounce.Contains("222", StringComparison.Ordinal))
            {
                await Task.Delay(25);
                afterDebounce = await ReadTextSafeAsync(settingsPath);
            }

            Assert.That(afterDebounce, Does.Contain("222"));

            settings.SetInt(SettingsKeys.WindowWidth, 333);
            store.Close();

            Assert.That(await File.ReadAllTextAsync(settingsPath), Does.Contain("333"));
        }
        finally
        {
            TryDeleteDir(tempDir);
        }
    }

    [Test]
    public async Task Успешный_перезапуск_закрывает_хранилище_после_старта_процесса()
    {
        var events = new ConcurrentQueue<string>();
        var spy = new SpySettingsStore(new MemorySettings(), events);
        var shell = new TrackingShellLauncher(events) { Succeeds = true };
        var dialogs = new NoopDialogs();
        var lifetime = new FakeApplicationLifetime();

        var restarted = await ThemeUpdateStartup.RestartApplicationAsync(
            () => Task.FromResult(true),
            spy,
            shell,
            dialogs,
            lifetime,
            () => @"C:\Program Files\SpaceSnoop\SpaceSnoop.exe");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted, Is.True);
            Assert.That(dialogs.LastErrorMessage, Is.Null);
            Assert.That(lifetime.ShutdownCalls, Is.EqualTo(1));
            Assert.That(spy.FlushCalls, Is.EqualTo(1));
            Assert.That(spy.CloseCalls, Is.EqualTo(1));
            Assert.That(events.ToArray(), Is.EqualTo(new[] { "flush", "start", "close" }));
        }
    }

    private static async Task<string> ReadTextSafeAsync(string path)
    {
        try
        {
            return File.Exists(path) ? await File.ReadAllTextAsync(path) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private sealed class SpySettingsStore(ISettingsStore inner, ConcurrentQueue<string> events) : ISettingsStore
    {
        private int _flushCalls;
        private int _closeCalls;

        public int FlushCalls => Volatile.Read(ref _flushCalls);

        public int CloseCalls => Volatile.Read(ref _closeCalls);

        public event EventHandler<string>? Changed
        {
            add => inner.Changed += value;
            remove => inner.Changed -= value;
        }

        public event EventHandler<SettingsWriteFailedEventArgs>? WriteFailed
        {
            add => inner.WriteFailed += value;
            remove => inner.WriteFailed -= value;
        }

        public string FilePath => inner.FilePath;

        public string? GetStringValue(string key) => inner.GetStringValue(key);

        public void SetValue(string key, string value) => inner.SetValue(key, value);

        public void Flush()
        {
            Interlocked.Increment(ref _flushCalls);
            events.Enqueue("flush");
            inner.Flush();
        }

        public void Close()
        {
            Interlocked.Increment(ref _closeCalls);
            events.Enqueue("close");
            inner.Close();
        }
    }

    private sealed class TrackingShellLauncher(ConcurrentQueue<string> events) : IShellLauncher
    {
        public bool Succeeds { get; init; } = true;

        public bool Open(string pathOrUrl) => Succeeds;

        public bool Reveal(string path) => Succeeds;

        public bool Start(string executable, params string[] arguments)
        {
            events.Enqueue("start");
            return Succeeds;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SpaceSnoop.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Не найден корень репозитория SpaceSnoop.");
    }

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "spacesnoop-theme-updates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TestContext.Progress.WriteLine(ex.Message);
        }
    }
}
