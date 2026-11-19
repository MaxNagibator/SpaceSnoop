using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using KeepShell.Views.Controls;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Cleanup;
using SpaceSnoop.Wpf.Views.Dialogs;
using SpaceSnoop.Wpf.Views.Scan;
using SpaceSnoop.Wpf.Views.Settings;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public partial class BindingSmokeTests
{
    private const double SettingsCardsProbeHeight = 240;
    private const double SmallWindowWidth = 880;
    private const double SmallWindowHeight = 584;
    private const double FirstScreenWidth = 900;
    private const double FirstScreenHeight = 600;
    private const int RecentProbeCount = 6;
    private const int NarrowTextMinLength = 12;
    private const double NarrowTextWidth = 120;

    private BindingErrorSink _sink = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;
    private GalleryFixture _fixture = null!;

    public static IReadOnlyList<string> Pages => SectionKey.All;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        // Класс App брать нельзя: его OnStartup висит в очереди диспетчера и на первой же прокачке
        // запускает настоящее приложение – разбор в docs/ui-tests-window-focus.md.
        _ = TestApplication.Ensure(AppResources.Sources);

        _fixture = GalleryFixtures.Create();

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetBool(SettingsKeys.ScanDuplicatesEnabled, true);
        SyncProfileStore.Save(settings, GalleryFixtures.Profiles(_fixture));

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_fixture.Root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _shell = _services.GetRequiredService<ShellViewModel>();
        _window = _services.GetRequiredService<MainWindow>();

        _host = VisualTestHost.Show(_window);

        VisualTestHost.Run(() => GalleryHost.ArrangeAsync(_services, _fixture));

        _sink = BindingErrorSink.Attach();

        var probe = new TextBlock
        {
            DataContext = _shell,
        };

        probe.SetBinding(TextBlock.TextProperty, new Binding("НетТакогоСвойства"));

        Settle();

        Assert.That(_sink.Errors, Is.Not.Empty, "Сенсор ошибок биндинга молчит – остальные проверки этого набора ничего не значат.");
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _sink?.Dispose();
        _host?.Dispose();
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
    public void Возврат_на_настройки_сохраняет_фильтр_разделов()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var rail = Descendant<SettingsRail>(_window) ?? throw new InvalidOperationException("На странице настроек нет рейла разделов.");

        try
        {
            rail.SetCurrentValue(SettingsRail.SearchTextProperty, "шнырь");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась второй раз.");
            Settle();

            var reopened = Descendant<SettingsRail>(_window) ?? throw new InvalidOperationException("Вернувшаяся страница настроек осталась без рейла.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(reopened.SearchText, Is.EqualTo("шнырь"), "После возврата потерян поисковый запрос.");
                Assert.That(
                    reopened.Sections?.Items.Where(section => section.IsVisible).Select(section => section.Key),
                    Is.EqualTo(
                        new[]
                        {
                            "agent",
                        }));

                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            Descendant<SettingsRail>(_window)?.SetCurrentValue(SettingsRail.SearchTextProperty, string.Empty);
            Settle();
        }
    }

    [Test]
    public void Ползунок_в_панели_подсветки_меняет_интенсивность()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
        Settle();

        var page = _services.GetRequiredService<ScanViewModel>();
        var originalIntensity = page.Intensity;
        var toggle = (ToggleButton)ViewCapture.Find(_window, "HeatToggle")!;

        try
        {
            page.Intensity = 8;
            toggle.IsChecked = true;
            Settle();

            var slider = (Slider)toggle.FindName("HeatSlider");
            Assert.That(slider.Value, Is.EqualTo(8));

            slider.SetCurrentValue(RangeBase.ValueProperty, 12d);
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.Intensity, Is.EqualTo(12));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            toggle.IsChecked = false;
            page.Intensity = originalIntensity;
            Settle();
        }
    }

    [Test]
    public void Зеркало_в_меню_синхронизации_доступно_при_выбранной_стороне()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Sync), Is.True, "Страница «Синхронизация» не открылась.");
        Settle();

        var setup = _services.GetRequiredService<SyncViewModel>().Setup;
        var (mode, winner, wasMirror) = (setup.SelectedModeIndex, setup.SelectedWinnerIndex, setup.Mirror);
        var toggle = (ToggleButton)ViewCapture.Find(_window, "MoreToggle")!;

        try
        {
            setup.Mirror = false;
            setup.SelectedModeIndex = SyncProfile.IndexOfMode(SyncMode.Bidirectional);
            setup.SelectedWinnerIndex = SyncProfile.IndexOfWinner(SyncWinner.Newest);
            toggle.IsChecked = true;
            Settle();

            var mirror = (ToggleButton)toggle.FindName("MirrorButton");
            Assert.That(mirror.IsEnabled, Is.False);

            setup.SelectedWinnerIndex = SyncProfile.IndexOfWinner(SyncWinner.Left);
            Settle();
            Assert.That(mirror.IsEnabled, Is.True);
            Assert.That(mirror.IsChecked, Is.False);

            mirror.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(setup.Mirror, Is.True);
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            toggle.IsChecked = false;
            (setup.SelectedModeIndex, setup.SelectedWinnerIndex, setup.Mirror) = (mode, winner, wasMirror);
            Settle();
        }
    }

    [TestCase(false, TestName = "Выбор плитки тома через UI Automation меняет цель скана")]
    [TestCase(true, TestName = "Выбор недавнего каталога через UI Automation меняет цель скана")]
    public void Выбор_плитки_через_UI_Automation_меняет_цель_скана(bool recentTarget)
    {
        var page = _services.GetRequiredService<ScanViewModel>();
        var volume = page.Drives.Volumes[0].Path;
        var recent = _fixture.Root;
        var selected = page.SelectedDrive;

        Assume.That(page.Drives.HasDrive(recent), Is.False, "Каталог стенда уже в списке – убирать его после кейса нельзя.");

        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            page.Drives.AddDrive(recent);
            var (from, to) = recentTarget ? (volume, recent) : (recent, volume);
            page.SelectedDrive = from;
            page.IsPickerOpen = true;
            Settle();

            var tile = Descendants<CommandRadioButton>(_window)
                    .FirstOrDefault(button => button.DataContext is DriveItem item && string.Equals(item.Path, to, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Плитка {to} не найдена.");

            var peer = UIElementAutomationPeer.CreatePeerForElement(tile);

            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.SelectedDrive, Is.EqualTo(to).IgnoreCase);
                Assert.That(tile.IsChecked, Is.True);
            }
        }
        finally
        {
            page.IsPickerOpen = false;
            page.Drives.RemoveDrive(recent);
            page.SelectedDrive = selected;
            Settle();
        }
    }

    [Test]
    public void Выбор_цели_сканирования_не_теряет_биндинги()
    {
        var page = _services.GetRequiredService<ScanViewModel>();
        var recent = _fixture.Root;

        Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
        Settle();

        _sink.Clear();

        if (!page.Drives.HasDrive(recent))
        {
            page.Drives.AddDrive(recent);
        }

        page.IsPickerOpen = true;

        Settle();

        var targets = ViewCapture.Find(_window, "Targets") as ItemsControl;

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(page.ShowTargetPicker, Is.True, "Открытый чипом выбор цели не показан.");
                Assert.That(targets?.IsVisible, Is.True, "Плитки дисков не показаны.");
                Assert.That(targets?.Items.OfType<DriveItem>(), Is.Not.Empty, "Ни один том не попал в плитки.");
                Assert.That(page.Drives.RecentDirectories, Is.Not.Empty, "Каталог не попал в недавние.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            SaveFrame(ViewCapture.Find(_window, "PickerBody"), "scan-target-picker");
        }
        finally
        {
            page.IsPickerOpen = false;
            page.Drives.RemoveDrive(recent);
            Settle();
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, TestName = "Страница скана при крупном шрифте в окне 900 на 600 ужимает верхние блоки и показывает шесть строк дерева")]
    [TestCase(1280d, 800d, false, TestName = "Страница скана в обычном окне не ужимает верхние блоки")]
    public void Дерево_скана_держит_минимум_строк(double width, double height, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<ScanViewModel>();
        var (oldWidth, oldHeight, scale, collapsed) = (_window.Width, _window.Height, shell.FontScale, page.Inspector.IsInspectorCollapsed);
        var selected = page.SelectedNode;

        try
        {
            _window.Width = width;
            _window.Height = height;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            page.Tips.ShowForAutomation(0);
            page.Inspector.IsInspectorCollapsed = true;
            Settle();
            page.SelectedNode = page.Roots[0];
            page.Inspector.IsInspectorCollapsed = true;
            Settle();

            var view = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
            var content = (FrameworkElement)view.FindName("ContentArea");
            var rail = Descendants<TextBlock>(view).Single(static text => text.Text == "Детали" && text.LayoutTransform is RotateTransform);
            var railBottom = rail.TransformToAncestor(content).TransformBounds(new(rail.RenderSize)).Bottom;
            var tipText = Descendants<TextBlock>(view).Single(text => text.Text == page.Tips.Text);
            var tree = Descendants<TreeView>(view).Single(static tree => tree.IsVisible);
            var viewport = Descendant<ScrollContentPresenter>(tree) ?? throw new InvalidOperationException("У дерева нет области прокрутки.");
            var row = Descendants<TreeViewItem>(tree)
                .Select(static item => item.Template.FindName("SelectionBd", item))
                .OfType<FrameworkElement>()
                .First(static bd => bd.ActualHeight > 0);

            var title = Descendants<TextBlock>(view).Single(static text => text.Text == "Структура каталога");
            var structure = Descendant<ScanStructurePanel>(view) ?? throw new InvalidOperationException("Панель структуры не найдена.");
            var railPanel = Descendant<ScanInspectorRail>(view) ?? throw new InvalidOperationException("Свёрнутая колонка деталей не найдена.");
            var railActions = (FrameworkElement)railPanel.FindName("RailActions");
            var moreButton = (Button)railPanel.FindName("MoreButton");
            var moreMenu = (ContextMenu)railPanel.FindName("MoreMenu");
            var railButtons = Descendants<Button>(railPanel).Where(static button => button.IsVisible).ToList();
            var railBounds = railButtons.Select(button => button.TransformToAncestor(content).TransformBounds(new(button.RenderSize))).ToList();
            var actionCount = Descendants<Button>(railActions).Count(static button => button.Visibility == Visibility.Visible);
            SaveFrame(view, bigFont ? "scan-compact" : "scan-regular");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(page.ShowSummary && page.Tips.IsVisible, Is.True, "Над деревом нет итогов или совета – кейс ничего не проверяет.");
                Assert.That(view.IsCompact, Is.EqualTo(bigFont), "Верхние блоки ужаты не по месту.");
                Assert.That(Math.Floor(viewport.ActualHeight / row.ActualHeight), Is.GreaterThanOrEqualTo(ScanView.MinTreeRows), "В дереве видно меньше шести строк.");
                Assert.That(title.IsVisible, Is.EqualTo(!bigFont), "Заголовок панели в сжатом виде должен уходить.");
                Assert.That(tipText.IsVisible, Is.EqualTo(!bigFont), "Текст совета в сжатом виде должен прятаться до щелчка.");
                Assert.That(railBottom, Is.LessThanOrEqualTo(content.ActualHeight + 0.5), "Подпись свёрнутых деталей режется снизу.");
                Assert.That(tree.GetValue(AutomationProperties.NameProperty), Is.EqualTo("Структура каталога"), "Дерево без имени для экранного диктора.");
                Assert.That(
                    structure.TreeRowCapacity() * row.ActualHeight,
                    Is.EqualTo(viewport.ActualHeight).Within(1),
                    "Запас строк дерева посчитан не по его настоящей высоте.");

                Assert.That(railBounds.Where(rect => rect.Bottom > (content.ActualHeight + 0.5)), Is.Empty, "Кнопка свёрнутых деталей режется снизу.");
                Assert.That(railActions.IsVisible || moreButton.IsVisible, Is.True, "Действия выбранного узла недоступны из свёрнутых деталей.");

                if (moreButton.IsVisible)
                {
                    moreButton.RaiseEvent(new(ButtonBase.ClickEvent));
                    Settle();
                    Assert.That(moreMenu.IsOpen, Is.True, "Кнопка «Ещё» не открыла меню действий.");
                    Assert.That(
                        moreMenu.Items.OfType<MenuItem>().Count(static item => item.Visibility == Visibility.Visible),
                        Is.EqualTo(actionCount),
                        "В меню «Ещё» не все действия свёрнутых деталей.");

                    moreMenu.IsOpen = false;
                }
            }
        }
        finally
        {
            page.Tips.DismissCommand.Execute(null);
            page.SelectedNode = selected;
            page.Inspector.IsInspectorCollapsed = collapsed;
            shell.FontScale = scale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [Test]
    public void Каждая_сбрасываемая_настройка_имеет_кнопку_в_своей_строке()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var names = Descendants<Button>(_window)
            .Select(AutomationProperties.GetName)
            .ToHashSet(StringComparer.Ordinal);

        var missing = _services.GetRequiredService<SettingsViewModel>()
            .ResetFields.Values
            .Where(field => field.Key != SettingsKeys.AgentHistoryVisible)
            .Where(field => !names.Contains(field.AutomationName))
            .Select(field => field.Label)
            .ToArray();

        Assert.That(
            missing,
            Is.Empty,
            $"Настройка сбрасывается разделом, но кнопки в своей строке не получила: {string.Join(", ", missing)}. Заведите её в карточке или объявите исключением здесь.");
    }

    [Test]
    public void Каждый_подпункт_настроек_находит_свой_блок_в_карточке()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        var missing = new List<string>();

        foreach (var section in sections.Items.Where(section => section.Children.Count > 0))
        {
            sections.SelectCommand.Execute(section);
            Settle();

            var anchors = Descendants<FrameworkElement>(_window)
                .Where(element => element.IsVisible)
                .Select(SettingsBlock.GetKey)
                .Where(key => key is { Length: > 0 })
                .ToHashSet(StringComparer.Ordinal);

            missing.AddRange(
                section.Children
                    .Where(child => !anchors.Contains(child.Key))
                    .Select(child => $"{section.Title} – {child.Title}"));
        }

        sections.Restore(restore);
        Settle();

        Assert.That(
            missing,
            Is.Empty,
            $"Подпункт рейла не нашёл своего блока в карточке, поэтому нажатие на него никуда не прокрутит: {string.Join(", ", missing)}. Пометьте блок через SettingsBlock.Key или уберите подпункт.");
    }

    [Test]
    public void Кнопка_сброса_строки_настроек_видна_только_у_изменённого_значения()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var settings = _services.GetRequiredService<SettingsViewModel>();
        settings.Sections.SelectCommand.Execute(settings.Sections["scan"]);
        Settle();

        var scan = _services.GetRequiredService<ScanPreferences>();
        var button = RowResetButton("Сбросить: число параллельных потоков");

        Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden), "Нетронутая строка показывает кнопку сброса.");

        scan.MaxParallelism = 1;
        Settle();

        Assert.That(button.Visibility, Is.EqualTo(Visibility.Visible), "Изменённая строка осталась без кнопки сброса: биндинг ResetFields[…] не дошёл до команды.");

        button.Command.Execute(button.CommandParameter);
        Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(scan.MaxParallelism, Is.EqualTo(scan.DefaultParallelism), "Нажатие не вернуло настройку к заводскому значению.");
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden), "Вернувшаяся к умолчанию строка держит кнопку видимой.");
            Assert.That(button.ActualWidth, Is.GreaterThan(0), "Спрятанная кнопка отдала своё место – строка прыгает при возврате к умолчанию.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void Кнопка_скана_в_окне_900_на_600_видна_без_прокрутки(bool firstRun, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<ScanViewModel>();
        var recents = Directory.GetDirectories(_fixture.Root, "*", SearchOption.AllDirectories)
            .Prepend(_fixture.Root)
            .Where(path => !page.Drives.HasDrive(path))
            .Take(RecentProbeCount)
            .ToArray();

        var (width, height, scale, welcome, elevated) = (_window.Width, _window.Height, shell.FontScale, page.FirstRun.IsVisible, page.FirstRun.IsElevated);

        try
        {
            _window.Width = FirstScreenWidth;
            _window.Height = FirstScreenHeight;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");

            foreach (var path in recents)
            {
                page.Drives.AddDrive(path);
            }

            if (firstRun)
            {
                page.FirstRun.ShowForAutomation(isElevated: false);
            }

            page.IsPickerOpen = true;
            Settle();

            var view = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
            var start = (Button)(ViewCapture.Find(_window, "StartButton") ?? throw new InvalidOperationException("Кнопка «Сканировать» не найдена."));
            var scroll = (ScrollViewer)(ViewCapture.Find(_window, "PickerScroll") ?? throw new InvalidOperationException("Прокрутка выбора цели не найдена."));
            var button = start.TransformToAncestor(_window).TransformBounds(new(start.RenderSize));
            var area = view.TransformToAncestor(_window).TransformBounds(new(view.RenderSize));
            var scrollBounds = scroll.TransformToAncestor(_window).TransformBounds(new(scroll.RenderSize));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scroll.ScrollableHeight, Is.GreaterThan(0), "Выбор цели влез целиком – кейс ничего не проверяет.");
                Assert.That(area.Bottom, Is.LessThanOrEqualTo(_window.ActualHeight + 0.5), "Страница скана выше окна – край страницы не край видимой области.");
                Assert.That(start.IsVisible, Is.True, "Кнопка «Сканировать» не показана.");
                Assert.That(start.IsEnabled, Is.EqualTo(!firstRun), "При открытом первом запуске кнопка сканирования не должна быть активна.");
                Assert.That(button.Top, Is.GreaterThanOrEqualTo(area.Top), "Кнопка «Сканировать» выше страницы.");
                Assert.That(
                    button.Bottom,
                    Is.LessThanOrEqualTo(area.Bottom + 0.5),
                    $"Кнопка «Сканировать» уходит за нижний край страницы на {button.Bottom - area.Bottom:F0} px.");

                if (firstRun)
                {
                    var card = Descendant<FirstRunCard>(scroll) ?? throw new InvalidOperationException("Карточка первого запуска не найдена.");
                    var doneButton = Descendants<Button>(card).Single(static btn => Equals(btn.Content, "Готово"));
                    var doneBounds = doneButton.TransformToAncestor(_window).TransformBounds(new(doneButton.RenderSize));

                    Assert.That(doneButton.IsVisible, Is.True, "Кнопка «Готово» карточки первого запуска не показана.");
                    Assert.That(doneBounds.Top, Is.GreaterThanOrEqualTo(scrollBounds.Top - 0.5), "Кнопка «Готово» выше области прокрутки.");
                    Assert.That(
                        doneBounds.Bottom,
                        Is.LessThanOrEqualTo(scrollBounds.Bottom + 0.5),
                        $"Кнопка «Готово» не видна без прокрутки в окне 900×600 (нижний край {doneBounds.Bottom:F0} > {scrollBounds.Bottom:F0}).");
                }
            }
        }
        finally
        {
            page.IsPickerOpen = false;
            page.FirstRun.ShowForAutomation(elevated);
            page.FirstRun.IsVisible = welcome;

            foreach (var path in recents)
            {
                page.Drives.RemoveDrive(path);
            }

            shell.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            Settle();
        }
    }

    [Test]
    public void Кнопка_строки_настроек_сбрасывает_свою_настройку_а_не_соседнюю()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var buttons = RowResetButtons();

        Assert.That(buttons, Is.Not.Empty, "На странице настроек не нашлось ни одной кнопки сброса строки.");

        foreach (var button in buttons)
        {
            var box = RowCheckBox(button);

            if (box is null)
            {
                continue;
            }

            var before = buttons.ToDictionary(candidate => candidate, CanReset);

            box.SetCurrentValue(ToggleButton.IsCheckedProperty, !(box.IsChecked ?? false));
            Settle();

            var flipped = buttons.Where(candidate => CanReset(candidate) != before[candidate]).ToArray();

            box.SetCurrentValue(ToggleButton.IsCheckedProperty, !(box.IsChecked ?? false));
            Settle();

            Assert.That(
                flipped,
                Is.EqualTo(
                    new[]
                    {
                        button,
                    }),
                $"Правка строки «{box.Content}» отозвалась не на своей кнопке: {string.Join(", ", flipped.Select(AutomationProperties.GetName))}. Кнопка привязана к чужой настройке.");
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, TestName = "Вопрос про git-папки при крупном шрифте в узком окне ставит кнопки столбиком на всю ширину")]
    [TestCase(1280d, 800d, false, TestName = "Вопрос про git-папки в обычном окне держит кнопки одним рядом справа")]
    public void Кнопки_вопроса_про_git_папки(double width, double height, bool bigFont)
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var modals = _services.GetRequiredService<ModalHostViewModel>();
        var (oldWidth, oldHeight, scale) = (_window.Width, _window.Height, shell.FontScale);

        try
        {
            _window.Width = width;
            _window.Height = height;
            shell.FontScale = bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale;
            _ = modals.ShowAsync(new GitFolderPromptViewModel(3));
            Settle();

            var view = Descendant<GitFolderPromptView>(_window) ?? throw new InvalidOperationException("Вопрос про git-папки не показан.");
            var column = (FrameworkElement)view.FindName("ChoiceColumn");
            var row = (FrameworkElement)view.FindName("ChoiceRow");
            var buttons = Descendants<Button>(view).Where(static button => button.IsVisible).ToList();
            var bounds = buttons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToList();

            SaveFrame(view, bigFont ? "git-column" : "git-row");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    buttons.Select(static button => button.Content),
                    Is.EqualTo(
                        new[]
                        {
                            "Синхронизировать .git", "Пропустить .git",
                        }),
                    "Порядок фокуса исходов изменился: первым должен идти отказ.");

                Assert.That(column.IsVisible, Is.EqualTo(bigFont), "Показан не тот вариант ряда кнопок.");
                Assert.That(row.IsVisible, Is.EqualTo(!bigFont), "Ряд и столбик кнопок видны не взаимоисключающе.");
                Assert.That(
                    buttons.Where(static button => button.ActualWidth < (button.DesiredSize.Width - button.Margin.Left - button.Margin.Right - 0.5))
                        .Select(static button => button.Content),
                    Is.Empty,
                    "Подпись кнопки режется.");

                Assert.That(bounds.Where(rect => rect.Left < -0.5 || rect.Right > (view.ActualWidth + 0.5)), Is.Empty, "Кнопка выходит за край диалога.");
                Assert.That(buttons.Count(static button => button.IsDefault), Is.EqualTo(1), "Enter подтверждает не одну кнопку.");
                Assert.That(buttons.Count(static button => button.IsCancel), Is.EqualTo(1), "Escape отвечает не одной кнопкой.");

                if (bigFont)
                {
                    Assert.That(bounds[0].Top, Is.GreaterThan(bounds[1].Top), "В столбике отказ должен стоять внизу, действие – сверху.");
                    Assert.That(bounds.Select(static rect => Math.Round(rect.Width)).Distinct().Count(), Is.EqualTo(1), "Кнопки столбика не на всю ширину.");
                }
                else
                {
                    Assert.That(bounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count(), Is.EqualTo(1), "Кнопки в обычном окне не в один ряд.");
                    Assert.That(
                        buttons[1].TransformToAncestor(row).TransformBounds(new(buttons[1].RenderSize)).Right,
                        Is.EqualTo(row.ActualWidth).Within(1),
                        "Ряд кнопок не прижат вправо.");
                }
            }
        }
        finally
        {
            modals.RequestCancel();
            shell.FontScale = scale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [TestCase(FirstScreenWidth, FirstScreenHeight, true, false, TestName = "Запрос прав при крупном шрифте в узком окне ставит исходы столбиком и не режет кнопки (светлая тема)")]
    [TestCase(FirstScreenWidth, FirstScreenHeight, true, true, TestName = "Запрос прав при крупном шрифте в узком окне ставит исходы столбиком и не режет кнопки (тёмная тема)")]
    [TestCase(1280d, 800d, false, false, TestName = "Запрос прав в обычном окне держит исходы одним рядом справа (светлая тема)")]
    [TestCase(1280d, 800d, false, true, TestName = "Запрос прав в обычном окне держит исходы одним рядом справа (тёмная тема)")]
    public void Кнопки_запроса_прав(double width, double height, bool bigFont, bool darkTheme)
    {
        var (shell, settings, modals) = (_services.GetRequiredService<ShellPreferences>(), _services.GetRequiredService<SettingsViewModel>(), _services.GetRequiredService<ModalHostViewModel>());
        var (oldWidth, oldHeight, scale, oldMode, oldSection) = (_window.Width, _window.Height, shell.FontScale, shell.StartupAdmin, settings.Sections.SelectedPath);
        try
        {
            ThemeManager.Apply(darkTheme ? AppThemes.DarkKey : AppThemes.LightKey);
            (_window.Width, _window.Height, shell.FontScale) = (width, height, bigFont ? FontScaleManager.MaxScale : FontScaleManager.DefaultScale);
            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
            settings.Sections.SelectCommand.Execute(settings.Sections["startup"]);
            Settle();
            var startupCard = Descendant<SettingsStartupCard>(_window) ?? throw new InvalidOperationException("Карточка настроек запуска не показана.");
            foreach (var option in settings.StartupAdminOptions)
            {
                settings.SelectedStartupAdminOption = option;
                Settle();
                var cardOverflow = Descendants<Control>(startupCard).Where(c => c.IsVisible && c.TransformToAncestor(startupCard).TransformBounds(new(c.RenderSize)).Right > (startupCard.ActualWidth + 1));
                Assert.That(cardOverflow, Is.Empty, $"Элемент в карточке запуска выходит за край при выборе «{option.Label}».");
            }
            settings.SelectedStartupAdminOption = settings.StartupAdminOptions[0];
            _ = modals.ShowAsync(AdminStartupPrompt.BuildConfirm());
            Settle();
            var view = Descendant<ConfirmDialogView>(_window) ?? throw new InvalidOperationException("Диалог подтверждения не показан.");
            var (row, column) = ((FrameworkElement)view.FindName("ChoiceRow"), (FrameworkElement)view.FindName("ChoiceColumn"));
            var buttons = Descendants<Button>(view).Where(static button => button.IsVisible).ToList();
            var checkBoxes = Descendants<CheckBox>(view).Where(static box => box.IsVisible).ToList();
            var bounds = buttons.Select(button => button.TransformToAncestor(view).TransformBounds(new(button.RenderSize))).ToList();
            var checkBounds = checkBoxes[0].TransformToAncestor(view).TransformBounds(new(checkBoxes[0].RenderSize));
            SaveFrame(view, bigFont ? "confirm-admin-column" : "confirm-admin-row");
            using (Assert.EnterMultipleScope())
            {
                Assert.That(buttons.Select(static button => button.Content), Is.EqualTo(new[] { "Не перезапускать", "Перезапустить" }), "Порядок фокуса исходов изменился: первым должен идти отказ.");
                Assert.That(checkBoxes.Select(static box => box.Content), Is.EqualTo(new[] { "Больше не спрашивать" }), "Чекбокс запоминания выбора не показан.");
                Assert.That((column.IsVisible, row.IsVisible), Is.EqualTo((bigFont, !bigFont)), "Ряд и столбик исходов видны не взаимоисключающе.");
                Assert.That(buttons.Where(static button => button.ActualWidth < (button.DesiredSize.Width - 0.5)).Select(static button => button.Content), Is.Empty, "Подпись кнопки режется.");
                Assert.That(bounds.Where(rect => rect.Left < -0.5 || rect.Right > (view.ActualWidth + 0.5)), Is.Empty, "Кнопка выходит за край диалога.");
                Assert.That(checkBounds.Left >= -0.5 && checkBounds.Right <= (view.ActualWidth + 0.5), Is.True, "Чекбокс запоминания выходит за край диалога.");
                Assert.That(bigFont ? bounds[0].Top > bounds[1].Top : bounds.Select(static rect => Math.Round(rect.Top)).Distinct().Count() == 1, Is.True, "Нарушено расположение кнопок исходов.");
                Assert.That(bigFont ? checkBounds.Bottom <= (bounds[1].Top + 0.5) : checkBounds.Right <= (bounds[0].Left + 0.5), Is.True, "Нарушено положение чекбокса запоминания относительно кнопок.");
                Assert.That(!bigFont || bounds.Select(static rect => rect.Width).Distinct().Count() == 1, Is.True, "Кнопки столбика не на всю ширину.");
            }
        }
        finally
        {
            modals.RequestCancel();
            ThemeManager.Apply(AppThemes.LightKey);
            (shell.StartupAdmin, shell.FontScale, _window.Width, _window.Height) = (oldMode, scale, oldWidth, oldHeight);
            settings.Sections.Restore(oldSection);
            Settle();
        }
    }

    [TestCase(1100d, 900d, 1d)]
    [TestCase(880d, 800d, 1.6d)]
    public void Контекст_dotnet_clean_помещается_по_ширине_и_остаётся_безопасной_демонстрацией(double width, double height, double fontScale)
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);
        try
        {
            _window.Width = width;
            _window.Height = height;
            preferences.FontScale = fontScale;
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.Development.ShowForAutomation();
            page.Development.DotNetClean.ShowForAutomation();
            page.Development.DotNetClean.HasError = true;
            page.Development.DotNetClean.StatusText = string.Join(" ", Enumerable.Repeat("Не удалось прочитать проект. Проверьте путь и доступ к файлу.", 8));
            page.Development.SelectedAreaIndex = 3;
            page.SelectedSectionIndex = 2;
            Settle();
            var view = Descendant<DotNetCleanView>(_window)!;
            var status = Descendants<TextBlock>(view).Single(x => x.Text == page.Development.DotNetClean.StatusText);
            var statusBounds = status.TransformToAncestor(view).TransformBounds(new(status.RenderSize));
            Assert.That(statusBounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1));
            Assert.That(status.ActualHeight, Is.GreaterThan(status.FontSize * 2));
            var fields = Descendants<TextBox>(view).Where(x => x.IsReadOnly && x.IsVisible).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(page.Development.DotNetClean.IsTrusted, Is.False);
                Assert.That(page.Development.DotNetClean.CleanCommand.CanExecute(null), Is.False);
                Assert.That(fields.Any(x => x.Text.Contains("--disable-build-servers")), Is.True);
                Assert.That(fields.Any(x => x.Text.Contains(@"C:\workspace\sample\Sample.csproj")), Is.True);
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            foreach (var field in fields)
            {
                var bounds = field.TransformToAncestor(view).TransformBounds(new(field.RenderSize));
                Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1));
            }
        }
        finally
        {
            page.Development.SelectedAreaIndex = 0;
            page.SelectedSectionIndex = 0;
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
        }
    }

    [TestCase(1100d, 900d, 1d)]
    [TestCase(880d, 800d, 1.6d)]
    public void Контекст_workloads_помещается_по_ширине_без_запуска_команды(double width, double height, double fontScale)
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);
        try
        {
            _window.Width = width;
            _window.Height = height;
            preferences.FontScale = fontScale;
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.Development.ShowForAutomation();
            page.Development.Workloads.ShowForAutomation();
            page.Development.SelectedAreaIndex = 2;
            page.SelectedSectionIndex = 2;
            Settle();
            var view = Descendant<WorkloadCleanupView>(_window)!;
            var workloads = Descendants<Expander>(view).Single(x => Equals(x.Header, "Вывод CLI об установленных workloads"));
            workloads.IsExpanded = true;
            Settle();
            var fields = Descendants<TextBox>(view).Where(x => x.IsReadOnly && x.IsVisible).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(fields.Any(x => x.Text.Contains("maui-windows")), Is.True);
                Assert.That(page.Development.Workloads.CleanCommand.CanExecute(null), Is.False);
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });

            foreach (var field in fields)
            {
                Assert.That(field.Focusable && field.IsTabStop && field.IsEnabled, Is.True);
                var bounds = field.TransformToAncestor(view).TransformBounds(new(field.RenderSize));
                Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1));
            }
        }
        finally
        {
            page.Development.ShowForAutomation();
            page.SelectedSectionIndex = 0;
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
        }
    }

    [Test]
    public void Настройки_при_крупном_шрифте_в_маленьком_окне_читаются_и_не_уходят_за_край()
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var (width, height, scale, restore) = (_window.Width, _window.Height, shell.FontScale, sections.SelectedPath);

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            shell.FontScale = FontScaleManager.MaxScale;

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
            Settle();

            var cards = Descendants<ScrollViewer>(_window).First(viewer => viewer.Name == "Cards");
            var host = (FrameworkElement)cards.Content;
            var problems = new List<string>();

            foreach (var section in sections.Items)
            {
                sections.SelectCommand.Execute(section);
                Settle();

                foreach (var element in Descendants<FrameworkElement>(host).Where(element => element.IsVisible && element is TextBlock or Control))
                {
                    var bounds = element.TransformToAncestor(host).TransformBounds(new(element.RenderSize));

                    if (element is TextBlock { Text.Length: >= NarrowTextMinLength } text && text.ActualWidth < NarrowTextWidth)
                    {
                        problems.Add($"{section.Title}: текст «{text.Text}» сжат до {text.ActualWidth:F0} px");
                    }
                    else if (element is Control && bounds.Right > (host.ActualWidth + 1))
                    {
                        problems.Add(
                            $"{section.Title}: {element.GetType().Name} «{AutomationProperties.GetName(element)}» уходит за край на {bounds.Right - host.ActualWidth:F0} px");
                    }
                }
            }

            sections.SelectCommand.Execute(sections["appearance"]);
            Settle();

            var slider = Descendants<Slider>(cards).Single(candidate => candidate.IsVisible);
            var reset = RowResetButton("Сбросить: масштаб шрифта");
            reset.Command.Execute(reset.CommandParameter);
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(problems, Is.Empty, () => string.Join(Environment.NewLine, problems));
                Assert.That(slider.ActualWidth, Is.GreaterThan(0), "Ползунок масштаба шрифта не отрисован.");
                Assert.That(shell.FontScale, Is.EqualTo(FontScaleManager.DefaultScale), "Кнопка строки не вернула масштаб к 100 %.");
            }
        }
        finally
        {
            shell.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            sections.Restore(restore);
            Settle();
        }
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

            SaveFrame(panel, "scan-duplicates");
        }
        finally
        {
            page.ViewMode = ScanViewMode.Tree;
            page.Duplicates.Clear();
            Settle();
        }
    }

    [Test]
    public void Плашка_прав_в_тесном_заголовке_видна_целиком_или_иконкой_и_заголовок_не_обрезан()
    {
        var shell = _services.GetRequiredService<ShellPreferences>();
        var (width, height, scale) = (_window.Width, _window.Height, shell.FontScale);
        var title = Descendants<TitleBarView>(_window).Single();
        var pill = (FrameworkElement)title.FindName("ElevationPill")!;
        var icon = (FrameworkElement)title.FindName("ElevationIcon")!;
        var slot = (FrameworkElement)title.FindName("ElevationSlot")!;

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            shell.FontScale = FontScaleManager.MaxScale;
            Settle();

            var room = ((Grid)slot.Parent).ColumnDefinitions[Grid.GetColumn(slot)].ActualWidth - slot.Margin.Left;
            var fits = pill.DesiredSize.Width <= room;
            var shown = fits ? pill : icon;
            var right = shown.TransformToAncestor(title).Transform(new(shown.ActualWidth, 0)).X;
            var named = Descendants<FrameworkElement>(icon).Where(element => element.IsVisible && AutomationProperties.GetName(element).Length > 0).ToList();
            var titleSlot = (FrameworkElement)VisualTreeHelper.GetParent(title);

            title.Measure(new(title.ActualWidth, double.PositiveInfinity));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(pill.IsVisible, Is.EqualTo(fits), $"Плашка шириной {pill.DesiredSize.Width:F0} при месте {room:F0}: показан не тот вариант.");
                Assert.That(icon.IsVisible, Is.EqualTo(!fits), "Иконка и полная плашка видны не взаимоисключающе.");
                Assert.That(right, Is.LessThanOrEqualTo(room + slot.TransformToAncestor(title).Transform(default).X + 0.5), "Видимый вариант плашки прав обрезан справа.");
                Assert.That(fits || named.Count > 0, Is.True, "У иконки режима запуска нет имени для диктора.");
                Assert.That(titleSlot.ActualHeight, Is.GreaterThanOrEqualTo(title.DesiredSize.Height - 0.5), "Заголовок окна при крупном шрифте режется снизу.");
            }
        }
        finally
        {
            shell.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            Settle();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Пометка_игнорирования_видна_в_дереве_и_снимается_из_меню_без_ошибок_биндинга(bool map)
    {
        _shell.TryNavigate(SectionKey.Scan);
        var scan = _services.GetRequiredService<ScanViewModel>();
        var previousMode = scan.ViewMode;
        scan.ViewMode = ScanViewMode.Tree;
        Settle();

        var target = Descendants<TreeViewItem>(_window)
            .First(static item => item.DataContext is ScanNodeViewModel { IsRoot: false });

        var node = (ScanNodeViewModel)target.DataContext;
        var beforeOrder = scan.Roots[0].Children.ToArray();
        var wasMarked = node.IsMarkedDeleted;
        target.IsSelected = true;
        var menu = target.ContextMenu;
        Assert.That(menu, Is.Not.Null);

        try
        {
            node.MarkDeletedCommand.Execute(null);
            menu.PlacementTarget = target;
            menu.IsOpen = true;
            Settle();
            var hide = menu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Command, node.IgnoreCommand));
            hide.Command.Execute(null);
            menu.IsOpen = false;
            Settle();
            Assert.That(node.IsIgnored, Is.True);
            Assert.That(scan.Roots[0].Children, Is.EqualTo(beforeOrder));
            var ignoredRow = Descendants<TreeViewItem>(_window).Single(item => ReferenceEquals(item.DataContext, node));
            Assert.That(ignoredRow.IsSelected, Is.True);
            Assert.That(
                Descendants<PackIconLucide>(ignoredRow)
                    .Any(static icon => icon.Kind == PackIconLucideKind.EyeOff && icon.IsVisible),
                Is.True);

            Assert.That(scan.SelectedNode, Is.SameAs(node));
            scan.ViewMode = map ? ScanViewMode.Treemap : ScanViewMode.Tree;
            Settle();

            var restore = (Button)ViewCapture.Find(_window, "ResetIgnoredButton")!;
            Assert.That(restore.IsVisible, Is.True);
            var delete = Descendants<Button>(Descendant<ScanToolbar>(_window)!)
                .Single(button => ReferenceEquals(button.Command, scan.Marks.DeleteMarkedCommand));

            Assert.That(delete.IsVisible, Is.True);
            Assert.That(restore.TranslatePoint(default, _window).Y, Is.EqualTo(delete.TranslatePoint(default, _window).Y).Within(1));
            Assert.That(restore.ActualHeight, Is.EqualTo(delete.ActualHeight).Within(0.1));
            Assert.That(scan.HasIgnoredItems, Is.True);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));

            var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".kc", "scratch", "scan-hidden-items");
            Directory.CreateDirectory(directory);
            var frame = Path.GetFullPath(Path.Combine(directory, map ? "ignored-map.png" : "ignored-tree.png"));
            ViewCapture.Save(Descendant<ScanView>(_window)!, frame);
            TestContext.Out.WriteLine(frame);

            scan.ViewMode = ScanViewMode.Tree;
            Settle();
            menu.PlacementTarget = Descendants<TreeViewItem>(_window).Single(item => ReferenceEquals(item.DataContext, node));
            menu.IsOpen = true;
            Settle();
            var unignore = menu.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Command, node.UnignoreCommand));
            Assert.That(unignore.Visibility, Is.EqualTo(Visibility.Visible));
            unignore.Command.Execute(null);
            menu.IsOpen = false;
            Settle();
            Assert.That(scan.HasIgnoredItems, Is.False);
            Assert.That(restore.IsVisible, Is.False);
            Assert.That(node.IsIgnored, Is.False);
            Assert.That(scan.SelectedNode, Is.SameAs(node));
            node.IgnoreCommand.Execute(null);
            Settle();
            restore.Command.Execute(null);
            Settle();
            Assert.That(node.IsIgnored, Is.False);
            Assert.That(scan.HasIgnoredItems, Is.False);
        }
        finally
        {
            menu.IsOpen = false;
            scan.ResetIgnoredCommand.Execute(null);
            if (!wasMarked)
            {
                node.UnmarkDeletedCommand.Execute(null);
            }

            scan.ViewMode = previousMode;
        }
    }

    [Test]
    public void Правка_настройки_мимо_страницы_поднимает_кнопку_сброса_при_возврате()
    {
        var store = _services.GetRequiredService<ISettingsStore>();

        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var settings = _services.GetRequiredService<SettingsViewModel>();
        settings.Sections.SelectCommand.Execute(settings.Sections["sync"]);
        Settle();

        Assert.That(RowResetButton("Сбросить: git-папки").Visibility, Is.EqualTo(Visibility.Hidden), "Политика git-папок не на умолчании – проверять нечего.");

        try
        {
            store.SetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Skip);

            Assert.That(_shell.TryNavigate(SectionKey.Scan), Is.True, "Страница «Сканирование» не открылась.");
            Settle();

            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась второй раз.");
            Settle();

            Assert.That(
                RowResetButton("Сбросить: git-папки").Visibility,
                Is.EqualTo(Visibility.Visible),
                "Настройку правит и страница синхронизации, мимо холдеров; вернувшаяся страница настроек не пересчитала доступность сброса.");
        }
        finally
        {
            store.SetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault);
            (_window.DataContext as ShellViewModel)?.ToString();
        }
    }

    [Test]
    public void Проектные_артефакты_виртуализуют_группы_и_прокручиваются_до_последней_строки()
    {
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.Development.ShowForAutomation();
            var projects = page.Development.Projects;
            projects.ShowForAutomation();
            page.Development.SelectedAreaIndex = 1;
            page.SelectedSectionIndex = 2;
            projects.Artifacts.Clear();
            for (var index = 0; index < 1000; index++)
            {
                var project = $@"C:\Sources\Project{index / 10:D3}";
                projects.Artifacts.Add(new(ProjectArtifactKind.Bin, $@"{project}\bin{index:D4}", 10, 1, true, string.Empty, project, []));
            }

            projects.RowsView.Refresh();
            Settle();
            var view = Descendant<ProjectCleanupView>(_window)!;
            var list = Descendants<ListBox>(view).Single(x => x.Name == "ProjectArtifacts");
            Assert.That(Descendants<ListBoxItem>(list).Count(), Is.LessThan(100));
            var last = projects.RowsView.Cast<ProjectArtifactViewModel>().Last();
            list.ScrollIntoView(last);
            Settle();
            Assert.That(Descendants<ListBoxItem>(list).Any(x => ReferenceEquals(x.DataContext, last)), Is.True);
            Assert.That(Descendants<ListBoxItem>(list).Count(), Is.LessThan(100));
        }
        finally
        {
            page.Development.Projects.ResetForAutomation();
            page.Development.SelectedAreaIndex = 0;
            page.SelectedSectionIndex = 0;
            Settle();
        }
    }

    [TestCase(1100d, 900d, 1d)]
    [TestCase(880d, 800d, 1.6d)]
    public void Проектные_артефакты_показывают_пути_и_выбор_без_выхода_за_ширину(double width, double height, double fontScale)
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);
        try
        {
            _window.Width = width;
            _window.Height = height;
            preferences.FontScale = fontScale;
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.Development.ShowForAutomation();
            page.Development.Projects.ShowForAutomation();
            page.Development.SelectedAreaIndex = 1;
            page.SelectedSectionIndex = 2;
            Settle();
            var view = Descendant<ProjectCleanupView>(_window)!;
            Assert.That(view.IsVisible, Is.True);
            var projects = page.Development.Projects;
            Assert.That(
                ((PropertyGroupDescription)projects.RowsView.GroupDescriptions.Single()).PropertyName,
                Is.EqualTo(nameof(ProjectArtifactViewModel.ProjectDirectory)));

            Assert.That(projects.RowsView.Groups!.Count, Is.GreaterThan(1));
            var appRows = projects.Artifacts.Where(x => x.ProjectDirectory == @"C:\Sources\Projects\App").ToArray();
            Assert.That(appRows.Select(x => x.ProjectBytes), Is.All.EqualTo(appRows.Sum(x => x.Bytes)));
            Assert.That(projects.RowsView.SortDescriptions[0].PropertyName, Is.EqualTo(nameof(ProjectArtifactViewModel.ProjectBytes)));
            Assert.That(projects.RowsView.SortDescriptions[0].Direction, Is.EqualTo(ListSortDirection.Descending));
            projects.LargestFirst = false;
            Assert.That(projects.RowsView.SortDescriptions[0].Direction, Is.EqualTo(ListSortDirection.Ascending));
            projects.LargestFirst = true;
            var list = Descendants<ListBox>(view).Single(x => x.Name == "ProjectArtifacts");
            Assert.That(list.Items.Count, Is.EqualTo(projects.Artifacts.Count));
            foreach (var artifact in projects.Artifacts)
            {
                list.ScrollIntoView(artifact);
                Settle();
                var container = Descendants<ListBoxItem>(list).FirstOrDefault(x => ReferenceEquals(x.DataContext, artifact));
                Assert.That(container, Is.Not.Null);
                var path = Descendants<TextBox>(container!).Single(x => AutomationProperties.GetName(x) == "Путь проектных артефактов");
                Assert.That(path.Text, Is.EqualTo(artifact.Path));
                path.BringIntoView();
                Settle();
                Assert.That(path.IsVisible && path.IsEnabled && path.Focusable && path.IsTabStop, Is.True);
                var bounds = path.TransformToAncestor(view).TransformBounds(new(path.RenderSize));
                Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1));
            }

            var preview = Descendants<Expander>(list).First();
            preview.IsExpanded = true;
            Settle();
            var previewList = Descendants<ListBox>(preview).First();
            previewList.ScrollIntoView(previewList.Items[0]);
            Settle();
            var previewPath = Descendants<TextBox>(previewList)
                .FirstOrDefault(x => AutomationProperties.GetName(x) == "Путь файла в снимке артефактов"
                    && x.Text.Length > 0);

            Assert.That(previewPath, Is.Not.Null);
            previewPath!.BringIntoView();
            Settle();
            var previewBounds = previewPath.TransformToAncestor(view).TransformBounds(new(previewPath.RenderSize));
            Assert.That(previewBounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1));
            Assert.That(Descendants<TextBlock>(view).Any(x => x.Text.StartsWith("C:\\Sources\\Projects", StringComparison.Ordinal)), Is.True);
            projects.SearchText = "coverage";
            Assert.That(
                projects.RowsView.Cast<ProjectArtifactViewModel>().Select(x => x.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        ProjectArtifactKind.Coverage,
                    }));

            projects.SearchText = string.Empty;
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            page.Development.SelectedAreaIndex = 0;
            page.Development.Projects.ResetForAutomation();
            page.SelectedSectionIndex = 0;
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [Test]
    public void Прокрутка_настроек_держит_подсветку_на_краях_карточки()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        var cards = Descendants<ScrollViewer>(_window).FirstOrDefault(viewer => viewer.Name == "Cards");

        Assert.That(cards, Is.Not.Null, "Прокручиваемая карточка настроек не найдена.");

        try
        {
            cards.MaxHeight = SettingsCardsProbeHeight;

            var section = sections.Items.FirstOrDefault(item => item.Children.Count > 1 && Scrollable(sections, item, cards));

            Assert.That(section, Is.Not.Null, "Ни одна карточка с подпунктами не прокручивается на этом окне – сценарий краёв невыразим.");

            var last = section.Children[^1];
            sections.SelectChildCommand.Execute(last);
            Settle();

            Assert.That(
                sections.SelectedChild,
                Is.SameAs(last),
                $"Клик по последнему подпункту «{last.Title}» подсветил «{sections.SelectedChild?.Title}»: прокрутку зажало по ScrollableHeight.");

            cards.ScrollToTop();
            Settle();

            Assert.That(
                sections.SelectedChild,
                Is.SameAs(section.Children[0]),
                $"Прокрутка в самый верх оставила подсвеченным «{sections.SelectedChild?.Title}» вместо первого подпункта.");
        }
        finally
        {
            cards.MaxHeight = double.PositiveInfinity;
            sections.Restore(restore);
            Settle();
        }
    }

    [Test]
    public void Замер_Windows_не_блокирует_вкладки_и_остаётся_видимым_после_перехода()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        page.Development.ShowForAutomation();
        page.Windows.ShowBusyForAutomation([new(1200, 3, [], CleanupAvailability.Available)]);
        try
        {
            Settle();
            var view = Descendant<CleanupView>(_window)!;
            var sections = Descendants<ListBox>(view).Single(x => AutomationProperties.GetName(x) == "Раздел очистки");
            Assert.That(sections.IsEnabled, Is.True);
            sections.SelectedIndex = 2;
            Settle();
            var areas = Descendants<ListBox>(view).Single(x => AutomationProperties.GetName(x) == "Область разработки");
            Assert.That(areas.IsEnabled, Is.True);
            areas.SelectedIndex = 1;
            Settle();
            var operations = (ItemsControl)view.FindName("Operations");
            Assert.Multiple(() =>
            {
                Assert.That(page.IsDevelopmentActive, Is.True);
                Assert.That(page.Development.IsProjectsActive, Is.True);
                Assert.That(page.IsBusy, Is.True);
                Assert.That(page.StatusCaption, Is.EqualTo(page.Windows.StatusCaption));
                Assert.That(page.ProgressValue, Is.EqualTo(1));
                Assert.That(page.ProgressMax, Is.EqualTo(page.Windows.Targets.Count));
                Assert.That(Descendants<ProgressBar>(operations).Single(x => ReferenceEquals(x.DataContext, page.Windows)).IsIndeterminate, Is.False);
                Assert.That(Descendants<TextBlock>(operations).Any(x => x.IsVisible && x.Text == page.Windows.StatusCaption), Is.True);
                Assert.That(Descendants<Button>(operations).Any(x => x.IsVisible && ReferenceEquals(x.Command, page.CancelCommand)), Is.True);
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });
        }
        finally
        {
            page.Development.SelectedAreaIndex = 0;
            page.SelectedSectionIndex = 0;
            page.Windows.ResetForAutomation();
            Settle();
        }
    }

    [Test]
    public void Причина_недоступности_цели_переносится_в_узком_окне_с_крупным_шрифтом()
    {
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var (width, height, scale) = (_window.Width, _window.Height, preferences.FontScale);
        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.SelectedSectionIndex = 0;
            _window.Width = 900;
            _window.Height = 600;
            preferences.FontScale = 1.6;
            page.Windows.ShowBusyForAutomation([new(0, 0, [], CleanupAvailability.Unsafe)]);
            Settle();
            var view = Descendant<CleanupTargetList>(_window)!;
            var text = Descendants<TextBlock>(view).Single(x => x.IsVisible && x.Text == page.Windows.Targets[0].AvailabilityText);
            Assert.Multiple(() =>
            {
                Assert.That(text.TextWrapping, Is.EqualTo(TextWrapping.Wrap));
                Assert.That(text.ActualHeight, Is.GreaterThan(text.FontSize * 1.8));
                Assert.That(text.ActualHeight + text.Margin.Top + text.Margin.Bottom, Is.EqualTo(text.DesiredSize.Height).Within(1));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });
        }
        finally
        {
            page.Windows.ResetForAutomation();
            (_window.Width, _window.Height, preferences.FontScale) = (width, height, scale);
            Settle();
        }
    }

    [Test]
    public void Одиночный_замер_отсутствующего_каталога_сообщает_причину()
    {
        var vm = _services.GetRequiredService<CleanupViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        var row = new CleanupTargetViewModel(new()
        {
            Id = "ReviewMissing",
            Name = "Отсутствующий каталог",
            Description = "Проверка статуса отсутствующего каталога",
            Kind = CleanupTargetKind.Directory,
            Path = Path.Combine(_fixture.Root, "missing-review-" + Guid.NewGuid().ToString("N")),
        });
        try
        {
            VisualTestHost.Run(() => vm.MeasureOneCommand.ExecuteAsync(row));
            Assert.That(row.Availability, Is.EqualTo(CleanupAvailability.Missing));
            Assert.That(vm.StatusText, Does.Contain(row.AvailabilityText).And.Not.Contain("0 байт"));
        }
        finally
        {
            vm.ResetForAutomation();
        }
    }

    [Test]
    public void Подстановка_замера_сбрасывает_хвост_и_формирует_итог_после_пересчёта()
    {
        var vm = _services.GetRequiredService<CleanupViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        try
        {
            vm.ShowBusyForAutomation([.. vm.Targets.Select(_ => new CleanupMeasurement(100, 1, [], CleanupAvailability.Available))]);
            Assert.That(vm.StatusText, Does.Contain(vm.TotalText));
            vm.ShowBusyForAutomation([new(20, 1, [], CleanupAvailability.Available)]);
            Assert.Multiple(() =>
            {
                Assert.That(vm.TotalBytes, Is.EqualTo(20));
                Assert.That(vm.Targets.Skip(1).All(row => !row.IsMeasured), Is.True);
                Assert.That(vm.Targets.Count(row => row.IsMeasuring), Is.EqualTo(1));
            });
        }
        finally
        {
            vm.ResetForAutomation();
        }
    }

    [Test]
    public void Последовательные_сбросы_Очистки_не_запускают_замер_при_возврате_на_Windows()
    {
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        page.Development.ShowForAutomation();
        try
        {
            for (var iteration = 0; iteration < 3; iteration++)
            {
                page.Windows.ShowBusyForAutomation([new(1200, 3, [], CleanupAvailability.Available)]);
                page.SelectedSectionIndex = 2;
                GalleryStates.ResetPage(SectionKey.Cleanup, GalleryStates.Busy, _services);
                GalleryStates.ResetPage(SectionKey.Docker, GalleryStates.Busy, _services);
                Settle();
                Assert.Multiple(() =>
                {
                    Assert.That(page.IsWindowsActive, Is.True);
                    Assert.That(page.Windows.IsBusy, Is.False);
                    Assert.That(page.Windows.CancelCommand, Is.Null);
                    Assert.That(page.Windows.StatusText, Is.Null);
                    Assert.That(page.Windows.Targets.All(row => !row.IsMeasuring && !row.IsMeasured), Is.True);
                });
                Одиночный_замер_отсутствующего_каталога_сообщает_причину();
                Подстановка_замера_сбрасывает_хвост_и_формирует_итог_после_пересчёта();
            }
        }
        finally
        {
            page.SelectedSectionIndex = 0;
            page.Windows.ResetForAutomation();
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Подстановка_не_освобождает_источник_отмены_живого_замера(bool single)
    {
        var vm = _services.GetRequiredService<CleanupViewModel>();
        VisualTestHost.Run(() => GalleryStates.SettleCleanupAsync(_services));
        var row = new CleanupTargetViewModel(new()
        {
            Id = "ControlledMissing",
            Name = "Каталог фикстуры",
            Description = "Замер только пути фикстуры",
            Kind = CleanupTargetKind.Directory,
            Path = Path.Combine(_fixture.Root, "controlled-missing"),
        });
        vm.Targets.Clear();
        vm.Targets.Add(row);
        try
        {
            VisualTestHost.Run(async () =>
            {
                var run = single ? vm.MeasureOneCommand.ExecuteAsync(row) : vm.RefreshCommand.ExecuteAsync(null);
                try
                {
                    Assert.That(vm.IsBusy, Is.True);
                    Assert.Throws<InvalidOperationException>(() => vm.ShowBusyForAutomation([]));
                    Assert.That(vm.CancelCommand, Is.Not.Null);
                }
                finally
                {
                    vm.CancelCommand?.Execute(null);
                    await run;
                }
                Assert.That(vm.IsBusy, Is.False);
            });
        }
        finally
        {
            vm.ResetForAutomation();
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Сжатие_Docker_раскрывается_для_чтения_при_заблокированных_действиях(bool busy)
    {
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        Assert.That(_shell.TryNavigate(SectionKey.Docker), Is.True);
        VisualTestHost.Run(() => page.Docker.EnsureLoadedAsync());
        var wasAvailable = page.Docker.IsAvailable;
        Expander? details = null;
        try
        {
            if (busy)
            {
                page.Docker.ShowBusyForAutomation();
            }
            else
            {
                page.Docker.IsAvailable = false;
            }
            Settle();
            var view = Descendant<SpaceSnoop.Wpf.Views.Docker.DockerCleanupView>(_window)!;
            details = Descendants<Expander>(view).Single(x => Equals(x.Header, "Вернуть место на диск Windows"));
            Assert.That(details.IsEnabled, Is.True);
            details.IsExpanded = true;
            Settle();
            Assert.Multiple(() =>
            {
                Assert.That(Descendants<TextBlock>(details).Any(x => x.IsVisible && x.Text == page.Docker.Compact.DiskUsageText), Is.True);
                Assert.That(Descendants<Button>(view).Where(x => x.Command is not null).All(x => !x.IsEnabled), Is.True);
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });
        }
        finally
        {
            details?.SetCurrentValue(Expander.IsExpandedProperty, false);
            if (busy)
            {
                page.Docker.ClearBusyForAutomation();
            }
            page.Docker.IsAvailable = wasAvailable;
            page.SelectedSectionIndex = 0;
            Settle();
        }
    }

    [Test]
    public void Раздел_разработки_показывает_четыре_кэша_без_потери_биндингов()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
        var development = _services.GetRequiredService<DeveloperCleanupViewModel>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        development.ShowForAutomation();
        page.SelectedSectionIndex = 2;
        try
        {
            Settle();
            Assert.Multiple(() =>
            {
                Assert.That(page.IsDevelopmentActive, Is.True);
                Assert.That(page.Sections.Count, Is.EqualTo(3));
                Assert.That(development.Caches.Count, Is.EqualTo(4));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            });
        }
        finally
        {
            page.SelectedSectionIndex = 0;
        }
    }

    [TestCaseSource(nameof(Pages))]
    public void Разметка_страницы_не_теряет_биндинги(string page)
    {
        Assert.That(_shell.TryNavigate(page), Is.True, $"Страница «{page}» не открылась.");

        Settle();

        Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
    }

    [TestCase("scan", "Сколько ставить")]
    [TestCase("archive", "Когда сжатие не помогает")]
    [TestCase("sync", "Что настройка не меняет")]
    public void Раскрытие_подробностей_не_сдвигает_контрол_строки(string section, string header)
    {
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;
        var restore = sections.SelectedPath;
        Expander? details = null;

        try
        {
            Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
            sections.SelectCommand.Execute(sections[section]);
            Settle();

            details = Descendants<Expander>(_window).Single(expander => expander.IsVisible && Equals(expander.Header, header));
            var row = Ancestor<SettingRowPanel>(details) ?? throw new InvalidOperationException($"Раскрытие «{header}» стоит не в строке формы.");
            var before = Tops(row);

            details.IsExpanded = true;
            Settle();

            Assert.That(Tops(row), Is.EqualTo(before), $"Раскрытие «{header}» сдвинуло контрол строки.");
        }
        finally
        {
            details?.SetCurrentValue(Expander.IsExpandedProperty, false);
            sections.Restore(restore);
            Settle();
        }

        static double[] Tops(SettingRowPanel row)
        {
            return [.. row.Children.Cast<FrameworkElement>().Skip(1).Select(child => Math.Round(child.TransformToAncestor(row).Transform(default).Y, 1))];
        }
    }

    [TestCase(1100d, 900d, 1d)]
    [TestCase(880d, 800d, 1.6d)]
    public void Раскрытые_пути_кэшей_доступны_для_выделения_и_помещаются_по_ширине(double width, double height, double fontScale)
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var page = _services.GetRequiredService<CleanupPageViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);
        try
        {
            _window.Width = width;
            _window.Height = height;
            preferences.FontScale = fontScale;
            Assert.That(_shell.TryNavigate(SectionKey.Cleanup), Is.True);
            page.Development.ShowForAutomation();
            page.SelectedSectionIndex = 2;
            Settle();
            var view = Descendant<DeveloperCleanupView>(_window)!;
            var details = Descendants<Expander>(view).Where(x => x.IsVisible).ToArray();
            Assert.That(details, Has.Length.EqualTo(4));
            foreach (var detail in details)
            {
                detail.IsExpanded = true;
            }

            Settle();
            var paths = Descendants<TextBox>(view).Where(x => x.IsReadOnly && x.IsVisible).ToArray();
            Assert.That(paths.Select(x => x.Text), Is.EquivalentTo(page.Development.Caches.Select(x => x.Path)));
            foreach (var path in paths)
            {
                path.BringIntoView();
                Settle();
                Assert.That(
                    path.Focusable && path.IsTabStop && path.IsEnabled && path.IsVisible,
                    Is.True,
                    "Полный путь не допускает фокуса и выделения текста.");

                var bounds = path.TransformToAncestor(view).TransformBounds(new(path.RenderSize));
                Assert.That(bounds.Left, Is.GreaterThanOrEqualTo(0));
                Assert.That(bounds.Right, Is.LessThanOrEqualTo(view.ActualWidth + 1), "Путь выходит за ширину страницы.");
            }

            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            page.SelectedSectionIndex = 0;
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
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

    private static void SaveFrame(FrameworkElement? panel, string name)
    {
        if (panel is null)
        {
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-frames"));
        var file = Path.Combine(directory.FullName, $"{name}.png");
        ViewCapture.Save(panel, file);
        TestContext.Out.WriteLine($"Кадр «{name}»: {file}");
    }

    private static T? Ancestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T found)
            {
                return found;
            }
        }

        return null;
    }

    private bool Scrollable(SettingsSectionList sections, SettingsSection section, ScrollViewer cards)
    {
        sections.SelectCommand.Execute(section);
        Settle();

        return cards.ScrollableHeight > 0;
    }

    private static bool CanReset(Button button)
    {
        return button.Command?.CanExecute(null) ?? false;
    }

    private static CheckBox? RowCheckBox(Button button)
    {
        return VisualTreeHelper.GetParent(button) is DependencyObject row
            ? Descendants<CheckBox>(row).FirstOrDefault()
            : null;
    }

    private Button[] RowResetButtons()
    {
        return [.. Descendants<Button>(_window).Where(button => AutomationProperties.GetName(button).StartsWith("Сбросить: ", StringComparison.Ordinal))];
    }

    private Button RowResetButton(string automationName)
    {
        return Descendants<Button>(_window).FirstOrDefault(button => AutomationProperties.GetName(button) == automationName)
            ?? throw new InvalidOperationException($"Кнопка сброса строки «{automationName}» не найдена на странице настроек.");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var index = 0; index < count; index++)
        {
            foreach (var found in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return found;
            }
        }
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

    private void Settle()
    {
        _host.Settle();
    }
}
