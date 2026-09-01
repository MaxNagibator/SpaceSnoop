using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.Views.Controls;
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
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class BindingSmokeTests
{
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

        _logging = KeepShellLogging.Bootstrap(new()
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
    public void Возврат_на_настройки_сохраняет_фильтр_разделов()
    {
        Assert.That(_shell.TryNavigate(SectionKey.Settings), Is.True, "Страница «Настройки» не открылась.");
        Settle();

        var rail = Descendant<SettingsRail>(_window) ?? throw new InvalidOperationException("На странице настроек нет рейла разделов.");
        var sections = _services.GetRequiredService<SettingsViewModel>().Sections;

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
                Assert.That(reopened, Is.Not.SameAs(rail), "Страница вернулась тем же рейлом – регрессию проверять не на чем.");
                Assert.That(reopened.Sections, Is.SameAs(sections), "Разделы у страницы новые – терять фильтр было негде.");
                Assert.That(reopened.SearchText, Is.EqualTo("шнырь"), "Новый рейл затёр запрос, который держал список разделов.");
                Assert.That(sections.Items.Where(section => section.IsVisible).Select(section => section.Key), Is.EqualTo(new[] { "agent" }));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            (Descendant<SettingsRail>(_window))?.SetCurrentValue(SettingsRail.SearchTextProperty, string.Empty);
            Settle();
        }
    }

    [Test]
    public void Потерянный_путь_биндинга_виден_тесту()
    {
        var probe = new TextBlock { DataContext = _shell };
        probe.SetBinding(TextBlock.TextProperty, new Binding("НетТакогоСвойства"));

        Settle();

        Assert.That(_sink.Errors, Is.Not.Empty, "Сенсор ошибок биндинга молчит – остальные проверки этого набора ничего не значат.");
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
            Assert.That(scan.MaxParallelism, Is.EqualTo(scan.ParallelismCeiling), "Нажатие не вернуло настройку к заводскому значению.");
            Assert.That(button.Visibility, Is.EqualTo(Visibility.Hidden), "Вернувшаяся к умолчанию строка держит кнопку видимой.");
            Assert.That(button.ActualWidth, Is.GreaterThan(0), "Спрятанная кнопка отдала своё место – строка прыгает при возврате к умолчанию.");
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
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

        var missing = _services.GetRequiredService<SettingsViewModel>().ResetFields.Values
            .Where(field => field.Key != SettingsKeys.AgentHistoryVisible)
            .Where(field => !names.Contains(field.AutomationName))
            .Select(field => field.Label)
            .ToArray();

        Assert.That(missing, Is.Empty, $"Настройка сбрасывается разделом, но кнопки в своей строке не получила: {string.Join(", ", missing)}. Заведите её в карточке или объявите исключением здесь.");
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

            Assert.That(flipped, Is.EqualTo(new[] { button }),
                $"Правка строки «{box.Content}» отозвалась не на своей кнопке: {string.Join(", ", flipped.Select(AutomationProperties.GetName))}. Кнопка привязана к чужой настройке.");
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

            Assert.That(RowResetButton("Сбросить: git-папки").Visibility, Is.EqualTo(Visibility.Visible),
                "Настройку правит и страница синхронизации, мимо холдеров; вернувшаяся страница настроек не пересчитала доступность сброса.");
        }
        finally
        {
            store.SetEnum(SettingsKeys.SyncGitFolders, AppDefaults.SyncGitFoldersDefault);
            (_window.DataContext as ShellViewModel)?.ToString();
        }
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
