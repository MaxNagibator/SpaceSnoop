using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BindingSmokeTests
{
    private const double OffScreen = -32000;
    private const double WindowWidth = 1440;
    private const double WindowHeight = 900;

    private readonly BindingErrorSink _sink = new();

    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;
    private GalleryFixture _fixture = null!;

    public static IReadOnlyList<string> Pages => SectionKey.All;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current is null)
        {
            new App().InitializeComponent();
        }

        _fixture = GalleryFixtures.Create();

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetBool(SettingsKeys.ScanDuplicatesEnabled, true);
        SyncProfileStore.Save(settings, GalleryFixtures.Profiles(_fixture));

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_fixture.Root, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        _services = App.ConfigureServices(settings, _logging);
        _shell = _services.GetRequiredService<ShellViewModel>();
        _window = _services.GetRequiredService<MainWindow>();

        _window.WindowStartupLocation = WindowStartupLocation.Manual;
        _window.Left = OffScreen;
        _window.Top = OffScreen;
        _window.Width = WindowWidth;
        _window.Height = WindowHeight;
        _window.ShowInTaskbar = false;
        _window.ShowActivated = false;
        _window.Show();
        HideFromScreen(_window);

        Run(() => GalleryRun.ArrangeAsync(_services, _fixture));

        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(_sink);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        PresentationTraceSources.DataBindingSource.Listeners.Remove(_sink);

        _window?.Close();
        _services?.Dispose();
        _logging?.Dispose();

        try
        {
            if (Directory.Exists(_fixture.Root))
            {
                Directory.Delete(_fixture.Root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог галереи {_fixture.Root}: {exception.Message}");
        }
    }

    [SetUp]
    public void SetUp()
    {
        _sink.Clear();
    }

    [TestCaseSource(nameof(Pages))]
    public void Разметка_страницы_не_теряет_биндинги(string page)
    {
        Assert.That(_shell.TryNavigate(page), Is.True, $"Страница «{page}» не открылась.");

        Settle();

        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [Test]
    public void Алиас_docker_открывает_Очистку_на_секции_Docker()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True, "Страница «Очистка» не открылась.");
        _services.GetRequiredService<CleanupPageViewModel>().IsDockerActive = false;

        Assert.That(_shell.TryNavigate(SectionKey.Docker), Is.True, "Алиас «docker» не открыл страницу.");

        Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_shell.CurrentSectionKey, Is.EqualTo(SectionKey.Cleanup));
            Assert.That(_services.GetRequiredService<CleanupPageViewModel>().IsDockerActive, Is.True);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Всплывающая_настройка_подсветки_не_теряет_биндинги()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

        Settle();

        var toggle = ViewCapture.Find(_window, "HeatToggle") as ToggleButton;
        Assert.That(toggle, Is.Not.Null, "В разметке сканирования нет кнопки подсветки HeatToggle.");

        _sink.Clear();
        toggle.IsChecked = true;
        Settle();

        var popup = toggle.FindName("HeatPopup") as Popup;
        Assert.That(popup?.Child, Is.Not.Null, "Всплывающая панель подсветки не построила содержимое.");

        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        Settle();

        var slider = (Slider)toggle.FindName("HeatSlider");
        var intensity = BindingOperations.GetBindingExpression(slider, RangeBase.ValueProperty);

        toggle.IsChecked = false;

        Assert.Multiple(() =>
        {
            Assert.That(content.DataContext, Is.InstanceOf<ScanViewModel>(), "Всплывающая панель не унаследовала контекст страницы.");
            Assert.That(intensity?.Status, Is.EqualTo(BindingStatus.Active), "Ползунок подсветки не привязался к странице.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        });
    }

    [Test]
    public void Всплывающая_панель_синхронизации_не_теряет_биндинги()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Sync), Is.True, "Страница «Синхронизация» не открылась.");

        Settle();

        var toggle = ViewCapture.Find(_window, "MoreToggle") as ToggleButton;
        Assert.That(toggle, Is.Not.Null, "В тулбаре синхронизации нет кнопки «Ещё».");

        _sink.Clear();
        toggle.IsChecked = true;
        Settle();

        var popup = toggle.FindName("MorePopup") as Popup;
        Assert.That(popup?.Child, Is.Not.Null, "Всплывающая панель «Ещё» не построила содержимое.");

        var content = (FrameworkElement)popup.Child;
        content.UpdateLayout();
        Settle();

        var mirror = (ToggleButton)toggle.FindName("MirrorButton");
        var checkedBinding = BindingOperations.GetBindingExpression(mirror, ToggleButton.IsCheckedProperty);
        var enabledBinding = BindingOperations.GetBindingExpression(mirror, UIElement.IsEnabledProperty);

        toggle.IsChecked = false;

        Assert.Multiple(() =>
        {
            Assert.That(content.DataContext, Is.InstanceOf<SyncViewModel>(), "Всплывающая панель не унаследовала контекст страницы.");
            Assert.That(checkedBinding?.Status, Is.EqualTo(BindingStatus.Active), "Тумблер зеркала не привязался к настройке.");
            Assert.That(enabledBinding?.Status, Is.EqualTo(BindingStatus.Active), "Доступность тумблера зеркала не привязалась к странице.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        });
    }

    [Test]
    public void Панель_дубликатов_не_теряет_биндинги()
    {
        var report = FindDuplicates();
        var page = _services.GetRequiredService<ScanViewModel>();

        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
        Settle();

        _sink.Clear();
        page.Duplicates.Apply(report);
        page.ViewMode = ScanViewMode.Duplicates;
        Settle();

        var panel = ViewCapture.Find(_window, "Duplicates");

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(report.Groups, Has.Count.EqualTo(2), "Дерево-образец не дало двух групп дубликатов.");
                Assert.That(panel?.IsVisible, Is.True, "Панель дубликатов не показана в третьем режиме.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            SaveFrame(panel);
        }
        finally
        {
            page.ViewMode = ScanViewMode.Tree;
            page.Duplicates.Clear();
            Settle();
        }
    }

    private static DuplicateReport FindDuplicates()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-duplicates", Guid.NewGuid().ToString("N")));
        var nested = root.CreateSubdirectory("Загрузки");

        var photo = new byte[96 * 1024];
        var archive = new byte[640 * 1024];
        Random.Shared.NextBytes(photo);
        Random.Shared.NextBytes(archive);

        File.WriteAllBytes(Path.Combine(root.FullName, "отпуск.jpg"), photo);
        File.WriteAllBytes(Path.Combine(root.FullName, "отпуск (1).jpg"), photo);
        File.WriteAllBytes(Path.Combine(nested.FullName, "отпуск - копия.jpg"), photo);
        File.WriteAllBytes(Path.Combine(root.FullName, "дистрибутив.zip"), archive);
        File.WriteAllBytes(Path.Combine(nested.FullName, "дистрибутив.zip"), archive);

        var tree = new DiskSpaceCalculator().Calculate(root);
        tree.FixAbsolutePath(root);

        return new DuplicateFinder().Find(tree, DuplicateOptions.Default, null, CancellationToken.None);
    }

    private static void SaveFrame(FrameworkElement? panel)
    {
        if (panel is null)
        {
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-frames"));
        var file = Path.Combine(directory.FullName, "scan-duplicates.png");
        ViewCapture.Save(panel, file, AppDefaults.ViewCaptureScaleDefault);
        TestContext.Out.WriteLine($"Кадр панели дубликатов: {file}");
    }

    [Test]
    public void Потерянный_путь_биндинга_виден_тесту()
    {
        var probe = new TextBlock { DataContext = _shell };
        probe.SetBinding(TextBlock.TextProperty, new Binding("НетТакогоСвойства"));

        Settle();

        Assert.That(_sink.Errors, Is.Not.Empty, "Сенсор ошибок биндинга молчит – остальные проверки этого набора ничего не значат.");
    }

    private static void Run(Func<Task> action)
    {
        var frame = new DispatcherFrame();
        ExceptionDispatchInfo? failure = null;

        _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                frame.Continue = false;
            }
        });

        Dispatcher.PushFrame(frame);
        failure?.Throw();
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private void Settle()
    {
        Pump();
        _window.UpdateLayout();
        Pump();
    }

    private static void HideFromScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;

        if (handle != IntPtr.Zero)
        {
            _ = NativeMethods.SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SwpHideWindow | NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate);
        }
    }

    private static class NativeMethods
    {
        public const uint SwpNoSize = 0x0001;
        public const uint SwpNoMove = 0x0002;
        public const uint SwpNoZOrder = 0x0004;
        public const uint SwpNoActivate = 0x0010;
        public const uint SwpHideWindow = 0x0080;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    }
}
