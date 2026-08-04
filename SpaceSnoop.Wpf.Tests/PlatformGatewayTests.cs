using KeepShell.Bootstrap;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Schedule;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PlatformGatewayTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopGateway_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string _tempDir = null!;

    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void Узел_дерева_показывает_файл_выделенным_а_каталог_открывает(bool directory, bool revealFiles, bool expectReveal)
    {
        var settings = new MemorySettings();
        var preferences = new ScanPreferences(settings) { RevealFiles = revealFiles };
        var shell = new FakeShellLauncher();
        var factory = new ScanNodeFactory(preferences, new(settings), shell);

        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        space.AddFile(MakeFile("payload", 16));

        var root = factory.CreateRoot(space, new());
        root.EnsureLoaded();

        var node = directory ? root : root.Children.First(static child => !child.IsDirectory);
        node.OpenInExplorerCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell.Revealed, Is.EqualTo(expectReveal ? new[] { node.Space!.AbsolutePath } : []));
            Assert.That(shell.Opened, Is.EqualTo(expectReveal ? [] : new[] { node.Space!.AbsolutePath }));
        }
    }

    [TestCase(true, "Сводка скопирована в буфер обмена", StatusSeverity.Info)]
    [TestCase(false, "Не удалось скопировать сводку", StatusSeverity.Warning)]
    public void Сводка_производительности_докладывает_об_отказе_буфера(bool succeeds, string expected, StatusSeverity severity)
    {
        var settings = new MemorySettings();
        var shellPreferences = new ShellPreferences(settings);
        var toasts = new ToastHostViewModel();
        var clipboard = new FakeClipboard { Succeeds = succeeds };
        var monitor = new PerformanceMonitor(NullLogger<PerformanceMonitor>.Instance);
        var chart = new PerformanceChartViewModel(monitor, settings, new FakeUiDispatcher(), new FakeApplicationLifetime());

        var performance = new PerformanceViewModel(monitor, new(), chart, shellPreferences, new(toasts, shellPreferences), clipboard);

        performance.CopySummaryCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(clipboard.LastText, Is.Not.Null.And.Not.Empty);
            Assert.That(toasts.Toasts.Select(static toast => toast.Message), Is.EqualTo(new[] { expected }));
            Assert.That(toasts.Toasts[0].Severity, Is.EqualTo(severity));
        }
    }

    [Test]
    public void Панель_графика_держит_таймер_только_пока_страница_открыта()
    {
        var dispatcher = new FakeUiDispatcher();
        var chart = new PerformanceChartViewModel(new(NullLogger<PerformanceMonitor>.Instance),
            new MemorySettings(),
            dispatcher,
            new FakeApplicationLifetime());

        Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
        var timer = dispatcher.Timers[0];

        chart.SetActive(true);
        var started = timer.IsRunning;
        chart.SetActive(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(started, Is.True);
            Assert.That(timer.IsRunning, Is.False);
        }
    }

    [Test]
    public void Выбранное_окно_графика_переживает_перезапуск_панели()
    {
        var settings = new MemorySettings();

        Chart(settings).Window = PerformanceChartWindow.Seconds30;

        Assert.That(Chart(settings).Window, Is.EqualTo(PerformanceChartWindow.Seconds30));
    }

    [Test]
    public void Панель_подхватывает_окно_выбранное_у_соседней()
    {
        var settings = new MemorySettings();
        var page = Chart(settings);
        var panel = Chart(settings);

        page.Window = PerformanceChartWindow.Seconds60;
        panel.SetActive(true);

        Assert.That(panel.Window, Is.EqualTo(PerformanceChartWindow.Seconds60));
    }

    [TestCase("None")]
    [TestCase("Seconds45")]
    public void Непригодное_значение_в_настройках_не_оставляет_график_без_окна(string stored)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.PerformanceChartWindow, stored);

        Assert.That(Chart(settings).Window, Is.EqualTo(AppDefaults.PerformanceChartWindowDefault));
    }

    [TestCase(true, "Запущено в фоне – результат появится в истории.")]
    [TestCase(false, "Не удалось запустить")]
    public void Профиль_расписания_сообщает_о_судьбе_фонового_запуска(bool succeeds, string expected)
    {
        var settings = new MemorySettings();
        SyncProfileStore.Save(settings, [new() { Id = "one", Name = "Один", Left = @"C:\A", Right = @"C:\B" }]);

        var shell = new FakeShellLauncher { Succeeds = succeeds };
        var schedule = new ScheduleViewModel(settings, new NoopDialogs(), new FakeFilePicker(), shell, new FakeScheduleRunner(), NullLogger<ScheduleViewModel>.Instance);

        var profile = schedule.Profiles[0];
        profile.RunNowCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(shell.Started, Has.Count.EqualTo(1));
            Assert.That(shell.Started[0], Does.Contain(AppInfo.SyncArgument).And.EndWith("one"));
            Assert.That(profile.Message, Is.EqualTo(expected));
        }
    }

    private static PerformanceChartViewModel Chart(MemorySettings settings)
    {
        return new(new(NullLogger<PerformanceMonitor>.Instance), settings, new FakeUiDispatcher(), new FakeApplicationLifetime());
    }

    private FileInfo MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, $"{name}.bin");
        File.WriteAllBytes(path, new byte[size]);

        return new(path);
    }
}
