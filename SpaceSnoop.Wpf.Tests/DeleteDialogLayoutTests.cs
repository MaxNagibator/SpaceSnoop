using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.Views;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            var (list, scroll, panel) = Parts();

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

    [Test]
    public void Список_догоняет_текущую_строку_и_уступает_ручной_прокрутке()
    {
        const int Followed = 3000;
        const int Later = 4000;
        const int AfterManual = 100;

        var dialog = _services.GetRequiredService<DeleteProgressDialogFactory>().Create(BuildItems(), permanent: false);

        _ = _modals.ShowAsync(dialog);
        _host.Settle();

        try
        {
            var (list, scroll, panel) = Parts();
            var viewport = scroll.ViewportHeight;

            Assert.That(scroll.VerticalOffset, Is.Zero, "Список открылся не с начала – движение за работой не от чего считать.");

            dialog.FollowIndex = Followed;
            _host.Settle();

            var followed = scroll.VerticalOffset;

            TestContext.Out.WriteLine(
                $"Строка {Followed} из {Rows}: видимая область {viewport:N1}, смещение стало {followed:N1}.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    followed,
                    Is.LessThanOrEqualTo(Followed).And.GreaterThan(Followed - viewport),
                    $"Строка {Followed} осталась вне видимой области [{followed:N1}; {followed + viewport:N1}) – список не следует за работой.");

                Assert.That(
                    Realized(panel).Any(child => ReferenceEquals((child as FrameworkElement)?.DataContext, dialog.Items[Followed])),
                    Is.True,
                    $"Строка {Followed} не реализована после прокрутки – смещение уехало не туда.");
            }

            dialog.FollowIndex = Later;
            _host.Settle();

            var later = scroll.VerticalOffset;

            Assert.That(
                later,
                Is.LessThanOrEqualTo(Later).And.GreaterThan(Later - viewport),
                $"Вторая пачка ушла на строку {Later}, а список остался на [{later:N1}; {later + viewport:N1}) – "
                + "следование приняло собственную прокрутку за руку человека и выключило себя.");

            scroll.ScrollToVerticalOffset(0);
            _host.Settle();

            var manual = scroll.VerticalOffset;

            dialog.FollowIndex = AfterManual;
            _host.Settle();

            Assert.That(
                scroll.VerticalOffset,
                Is.EqualTo(manual),
                $"После ручной прокрутки на {manual:N1} список уехал на {scroll.VerticalOffset:N1} – следование выдёргивает его из-под человека.");

            Assert.That(list.Items, Has.Count.EqualTo(Rows));
        }
        finally
        {
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    [Test]
    public void Идущее_удаление_не_теряет_биндингов()
    {
        using var sink = BindingErrorSink.Attach();

        var dialog = _services.GetRequiredService<DeleteProgressDialogFactory>().Create(BuildItems(), permanent: false);

        _ = _modals.ShowAsync(dialog);
        _host.Settle();

        try
        {
            dialog.ShowRunningForAutomation(2);
            _host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dialog.BatchCaption, Is.Not.Null, "Подпись пачки не подставилась – проверять её разметку не на чем.");
                Assert.That(dialog.ChunkElapsedText, Is.Not.Null, "Время текущей операции не подставилось – строка секунд в кадр не попадёт.");
                Assert.That(dialog.IsChunkStalled, Is.True, "Затянувшаяся пачка не включила пульс – полоса на кадре останется в процентах.");
                Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
            }
        }
        finally
        {
            dialog.ClearForAutomation();
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    [Test]
    public void Клавиатура_и_колесо_берут_экран_а_не_три_строки()
    {
        var dialog = _services.GetRequiredService<DeleteProgressDialogFactory>().Create(BuildItems(), permanent: false);

        _ = _modals.ShowAsync(dialog);
        _host.Settle();

        try
        {
            var (list, scroll, panel) = Parts();
            var viewport = scroll.ViewportHeight;

            list.Focus();

            Assert.That(
                Keyboard.FocusedElement,
                Is.SameAs(list),
                $"Клавиатурный фокус держит {Describe(Keyboard.FocusedElement)}, а не список – "
                + "PageDown и End до него не дойдут никогда.");

            Press(list, System.Windows.Input.Key.PageDown);
            var page = scroll.VerticalOffset;

            Press(list, System.Windows.Input.Key.End);
            var end = scroll.VerticalOffset;

            Press(list, System.Windows.Input.Key.Home);
            var home = scroll.VerticalOffset;

            Wheel(panel, -Mouse.MouseWheelDeltaForOneLine);
            var wheel = scroll.VerticalOffset;

            TestContext.Out.WriteLine(
                $"Видимая область {viewport:N1}: PageDown → {page:N1}, End → {end:N1}, Home → {home:N1}, щелчок колеса → {wheel:N1}.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    page,
                    Is.GreaterThan(viewport - 2).And.LessThanOrEqualTo(viewport),
                    $"PageDown сдвинул список на {page:N1} при видимой области {viewport:N1} – это не экран.");

                Assert.That(
                    end,
                    Is.EqualTo(scroll.ScrollableHeight),
                    $"End остановился на {end:N1} вместо конца списка {scroll.ScrollableHeight:N1}.");

                Assert.That(home, Is.Zero, $"Home остановился на {home:N1} вместо начала списка.");

                Assert.That(
                    wheel,
                    Is.GreaterThan(viewport - 2).And.LessThanOrEqualTo(viewport),
                    $"Щелчок колеса дал {wheel:N1} строк при видимой области {viewport:N1} – шаг снова считанные строки.");
            }
        }
        finally
        {
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    [Test]
    public void Значок_идущей_строки_крутится_только_при_разрешённых_анимациях()
    {
        var dialog = _services.GetRequiredService<DeleteProgressDialogFactory>().Create(BuildItems(), permanent: false);

        _ = _modals.ShowAsync(dialog);
        _host.Settle();

        try
        {
            var (_, _, panel) = Parts();
            var row = dialog.Items[0];

            row.State = DeleteRowState.Deleting;
            _host.Settle();

            var rotation = Rotation(panel, row);

            Assert.That(rotation, Is.Not.Null, "У значка идущей строки нет поворота – крутить нечего.");

            var animated = SystemParameters.ClientAreaAnimation;

            TestContext.Out.WriteLine(
                $"Анимации клиентской области: {animated}; поворот значка анимирован: {rotation!.HasAnimatedProperties}.");

            Assert.That(
                rotation.HasAnimatedProperties,
                Is.EqualTo(animated),
                animated
                    ? "Значок идущей строки не крутится, хотя анимации в системе разрешены."
                    : "Анимации в системе выключены, а значок идущей строки всё равно крутится – настройку никто не спросил.");
        }
        finally
        {
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    private static string Describe(IInputElement? element)
    {
        return element is null ? "ничего" : element.GetType().Name;
    }

    private static RotateTransform? Rotation(Panel panel, object row)
    {
        var container = panel.Children
            .Cast<UIElement>()
            .FirstOrDefault(child => ReferenceEquals((child as FrameworkElement)?.DataContext, row));

        return container is null ? null : Transform(container);
    }

    private static RotateTransform? Transform(DependencyObject root)
    {
        if ((root as UIElement)?.RenderTransform is RotateTransform rotate)
        {
            return rotate;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            if (Transform(VisualTreeHelper.GetChild(root, index)) is { } nested)
            {
                return nested;
            }
        }

        return null;
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

    private (ItemsControl List, ScrollViewer Scroll, VirtualizingStackPanel Panel) Parts()
    {
        var list = ViewCapture.Find(_window, "MarkedItems") as ItemsControl;
        Assert.That(list, Is.Not.Null, "В диалоге удаления нет списка MarkedItems – проверять нечего.");

        var scroll = Descendant<ScrollViewer>(list);
        var panel = Descendant<VirtualizingStackPanel>(list);

        Assert.That(scroll, Is.Not.Null, "У списка нет своего ScrollViewer – прокручивает его кто-то снаружи.");
        Assert.That(panel, Is.Not.Null, "Список построен не виртуализирующей панелью.");

        return (list, scroll!, panel!);
    }

    private void Press(UIElement target, Key key)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(_window), 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        });

        _host.Settle();
    }

    private void Wheel(UIElement target, int delta)
    {
        var preview = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
        {
            RoutedEvent = UIElement.PreviewMouseWheelEvent,
        };

        target.RaiseEvent(preview);

        if (!preview.Handled)
        {
            target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta)
            {
                RoutedEvent = UIElement.MouseWheelEvent,
            });
        }

        _host.Settle();
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
