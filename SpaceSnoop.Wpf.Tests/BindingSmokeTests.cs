using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
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
        _window.Show();

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
        catch (IOException)
        {
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
}
