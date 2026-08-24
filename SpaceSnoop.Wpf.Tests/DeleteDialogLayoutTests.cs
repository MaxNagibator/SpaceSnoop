using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class DeleteDialogLayoutTests
{
    private const int Rows = 4314;
    private const int Pages = 12;
    private const double WindowWidth = 1440;
    private const double WindowHeight = 900;

    private string _root = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ModalHostViewModel _modals = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        if (Application.Current is null)
        {
            new App().InitializeComponent();
        }

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-delete-layout", Guid.NewGuid().ToString("N"));

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(new()
        {
            LogsDirectory = Path.Combine(_root, "logs"),
            FileNamePrefix = AppInfo.LogFilePrefix,
        });

        _services = App.ConfigureServices(settings, _logging);
        _window = _services.GetRequiredService<MainWindow>();
        _modals = _services.GetRequiredService<ModalHostViewModel>();

        _host = VisualTestHost.Show(_window, WindowWidth, WindowHeight);
        _host.Settle();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог стенда {_root}: {exception.Message}");
        }
    }

    [Test]
    public void Список_диалога_удаления_раскладывает_только_видимое_и_переиспользует_строки()
    {
        var dialog = _services.GetRequiredService<DeleteProgressDialogFactory>().Create(BuildItems(), permanent: false);

        _ = _modals.ShowAsync(dialog);
        _host.Settle();

        try
        {
            var list = ViewCapture.Find(_window, "MarkedItems") as ItemsControl;
            Assert.That(list, Is.Not.Null, "В диалоге удаления нет списка MarkedItems – проверять нечего.");

            var scroll = Descendant<ScrollViewer>(list);
            var panel = Descendant<VirtualizingStackPanel>(list);

            Assert.That(scroll, Is.Not.Null, "У списка нет своего ScrollViewer – прокручивает его кто-то снаружи.");
            Assert.That(panel, Is.Not.Null, "Список построен не виртуализирующей панелью.");

            Assert.That(list.Items, Has.Count.EqualTo(Rows), "Список получил не все строки – мерить нечего.");

            Assert.That(
                VirtualizingPanel.GetScrollUnit(list),
                Is.EqualTo(ScrollUnit.Item),
                "Прокрутка перешла в пиксели: видимая область больше не считается строками, и бюджет ниже теряет смысл.");

            var viewport = scroll.ViewportHeight;

            Assert.That(
                viewport,
                Is.GreaterThan(0).And.LessThan(Rows),
                $"Видимая область списка – {viewport:N1} строк из {Rows}: высота пришла бесконечной, виртуализация выключена.");

            var budget = ((int)Math.Ceiling(viewport) * 5) + 16;
            var realized = new HashSet<UIElement>();
            var opened = Realized(panel);
            realized.UnionWith(opened);

            for (var page = 1; page <= Pages; page++)
            {
                var before = scroll.VerticalOffset;

                scroll.ScrollToVerticalOffset(page * viewport);
                _host.Settle();

                Assert.That(
                    scroll.VerticalOffset,
                    Is.GreaterThan(before),
                    $"Прокрутка встала на {before:N1} на {page}-м экране – дальше проверка переиспользования ничего не значит.");

                realized.UnionWith(Realized(panel));
            }

            TestContext.Out.WriteLine(
                $"Строк {Rows}, видимая область {viewport:N1}, при открытии реализовано {opened.Count}, "
                + $"за {Pages} экранов прокрутки создано {realized.Count} контейнеров при бюджете {budget}.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    opened, Has.Count.LessThanOrEqualTo(budget),
                    $"При открытии реализовано {opened.Count} строк из {Rows} – список раскладывается целиком.");

                Assert.That(
                    realized, Has.Count.LessThanOrEqualTo(budget),
                    $"За {Pages} экранов прокрутки создано {realized.Count} контейнеров при бюджете {budget} – "
                    + "строки не переиспользуются, режим Recycling не работает.");
            }
        }
        finally
        {
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    private static IReadOnlyList<SpaceBase> BuildItems()
    {
        var root = new DirectorySpace(@"C:\Стенд", null, DateTime.Now, DateTime.Now);
        var items = new List<SpaceBase>(Rows);

        for (var index = 0; index < Rows; index++)
        {
            items.Add(new DirectorySpace($"объект-{index:D5}", root, DateTime.Now, DateTime.Now));
        }

        return items;
    }

    private static IReadOnlyCollection<UIElement> Realized(Panel panel)
    {
        return [.. panel.Children.Cast<UIElement>()];
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T found)
            {
                return found;
            }

            if (Descendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
