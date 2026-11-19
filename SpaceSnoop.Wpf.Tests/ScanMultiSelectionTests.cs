using KeepShell.Bootstrap;
using KeepShell.Services.Platform;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Scan;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanMultiSelectionTests
{
    private string _directory = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private BindingErrorSink _sink = null!;
    private MainWindow _window = null!;
    private ShellViewModel _shell = null!;
    private ScanViewModel _scan = null!;
    private ScanNodeFactory _factory = null!;
    private ToastHostViewModel _toasts = null!;
    private ModalHostViewModel _modals = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        _directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopMultiSelection", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        settings.SetBool(SettingsKeys.WelcomePending, false);
        settings.SetValue(SettingsKeys.EnableToastNotifications, "true");

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_directory, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _shell = _services.GetRequiredService<ShellViewModel>();
        _scan = _services.GetRequiredService<ScanViewModel>();
        _factory = _services.GetRequiredService<ScanNodeFactory>();
        _toasts = _services.GetRequiredService<ToastHostViewModel>();
        _modals = _services.GetRequiredService<ModalHostViewModel>();
        _window = _services.GetRequiredService<MainWindow>();
        _window.Width = 1280;
        _window.Height = 800;
        _host = VisualTestHost.Show(_window);
        _ = _shell.TryNavigate(SectionKey.Scan);
        _host.Settle();
        _sink = BindingErrorSink.Attach();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _sink?.Dispose();
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    [SetUp]
    public void SetUp()
    {
        ThemeManager.Apply(AppThemes.LightKey);
        _scan.Roots.Clear();
        _scan.SelectSingle(null);
        _factory.Visibility.Clear();
        _scan.Marks.RecountMarked();
        _toasts.Toasts.Clear();
        _scan.HasResult = true;
        _scan.IsPickerOpen = false;
        _scan.SelectedViewIndex = 0;
        _scan.Inspector.IsInspectorCollapsed = false;
        _host.Settle();
        _sink.Clear();
    }

    [TestCase(false, TestName = "Визуальные состояния множественного выбора в светлой теме")]
    [TestCase(true, TestName = "Визуальные состояния множественного выбора в тёмной теме")]
    public void Визуальные_состояния_дерева_меню_и_инспектора(bool darkTheme)
    {
        var themeName = darkTheme ? "dark" : "light";
        ThemeManager.Apply(darkTheme ? AppThemes.DarkKey : AppThemes.LightKey);
        var (_, rootNode, branchNode, firstFile, secondFile, outsideFile) = PopulateTree();

        _scan.SelectSingle(branchNode);
        _scan.ToggleSelection(firstFile);
        _scan.ToggleSelection(outsideFile);
        _host.Settle();

        var tree = Tree(Panel());
        var accentSoft = (Brush)_window.FindResource("Accent.Soft");
        var branchItem = TreeItem(tree, branchNode);
        var firstItem = TreeItem(tree, firstFile);
        var secondItem = TreeItem(tree, secondFile);
        var outsideItem = TreeItem(tree, outsideFile);
        var rootItem = TreeItem(tree, rootNode);

        var menu = outsideItem.ContextMenu ?? throw new InvalidOperationException("Контекстное меню узла не задано.");
        menu.PlacementTarget = outsideItem;
        menu.DataContext = outsideFile;
        menu.IsOpen = true;
        _host.Settle();

        var visibleMenuTexts = menu.Items
            .OfType<MenuItem>()
            .Where(static item => item.Visibility == Visibility.Visible)
            .Select(static item => item.Header is DependencyObject header
                ? string.Join(" ", Descendants<TextBlock>(header).Select(static t => t.Text))
                : item.Header?.ToString() ?? string.Empty)
            .ToArray();

        SaveFrame(menu, $"scan-multi-context-menu-{themeName}");
        menu.IsOpen = false;
        _host.Settle();

        var view = Descendant<ScanView>(_window) ?? throw new InvalidOperationException("Страница скана не найдена.");
        SaveFrame(view, $"scan-multi-selection-{themeName}");
        _scan.Inspector.IsInspectorCollapsed = true;
        _host.Settle();
        SaveFrame(view, $"scan-multi-rail-{themeName}");
        _scan.Inspector.IsInspectorCollapsed = false;
        _host.Settle();

        outsideFile.MarkDeletedCommand.Execute(null);
        _host.Settle();
        SaveFrame(_window, $"scan-multi-toast-undo-{themeName}");
        var undo = UndoToast();
        undo.ActionCommand!.Execute(undo);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(SelectionBackground(branchItem), Is.SameAs(accentSoft));
            Assert.That(SelectionBackground(firstItem), Is.SameAs(accentSoft));
            Assert.That(SelectionBackground(outsideItem), Is.SameAs(accentSoft));
            Assert.That(SelectionBackground(secondItem), Is.SameAs(Brushes.Transparent));
            Assert.That(SelectionBackground(rootItem), Is.SameAs(Brushes.Transparent));
            Assert.That(outsideItem.IsSelected, Is.True);
            Assert.That(branchItem.IsSelected, Is.False);
            Assert.That(firstItem.IsSelected, Is.False);
            Assert.That(
                visibleMenuTexts,
                Is.EqualTo(
                    new[]
                    {
                        $"Выбрано: 3 объекта · {SizeFormatter.Format(208)}", "Копировать пути", "Игнорировать", "Пометить на удаление",
                    }));

            Assert.That(_scan.Inspector.IsMultiSelection, Is.True);
            Assert.That(_scan.Inspector.Title, Is.EqualTo("3 объекта"));
            Assert.That(_scan.Inspector.TotalSizeText, Is.EqualTo(SizeFormatter.Format(208)));
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Выделение_мышью_и_клавиатурой_в_дереве_сканирования()
    {
        var (_, rootNode, branchNode, firstFile, secondFile, outsideFile) = PopulateTree();
        var panel = Panel();
        var tree = Tree(panel);

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, firstFile), 1, ModifierKeys.None), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _scan.SelectedNodes,
                Is.EqualTo(
                    new[]
                    {
                        firstFile,
                    }));

            Assert.That(_scan.HasMultiSelection, Is.False);
            Assert.That(firstFile.IsSelected, Is.True);
            Assert.That(firstFile.IsPrimarySelected, Is.True);
            Assert.That(TreeItem(tree, firstFile).IsSelected, Is.True);
        }

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, outsideFile), 1, ModifierKeys.Control), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _scan.SelectedNodes,
                Is.EquivalentTo(
                    new[]
                    {
                        firstFile, outsideFile,
                    }));

            Assert.That(_scan.HasMultiSelection, Is.True);
            Assert.That(_scan.SelectedNode, Is.SameAs(outsideFile));
            Assert.That(firstFile.IsSelected, Is.True);
            Assert.That(firstFile.IsPrimarySelected, Is.False);
            Assert.That(outsideFile.IsSelected, Is.True);
            Assert.That(outsideFile.IsPrimarySelected, Is.True);
        }

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, outsideFile), 1, ModifierKeys.Control), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _scan.SelectedNodes,
                Is.EqualTo(
                    new[]
                    {
                        firstFile,
                    }));

            Assert.That(_scan.SelectedNode, Is.SameAs(firstFile));
            Assert.That(firstFile.IsPrimarySelected, Is.True);
            Assert.That(TreeItem(tree, firstFile).IsSelected, Is.True);
            Assert.That(outsideFile.IsSelected, Is.False);
        }

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, firstFile), 1, ModifierKeys.Control), Is.True);
        _host.Settle();
        Assert.That(_scan.SelectedNodes, Is.Empty);

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, firstFile), 1, ModifierKeys.None), Is.True);
        _host.Settle();
        Assert.That(
            _scan.SelectedNodes,
            Is.EqualTo(
                new[]
                {
                    firstFile,
                }));

        branchNode.IsExpanded = false;
        _host.Settle();
        _scan.SelectSingle(firstFile);
        _scan.SelectRange(branchNode);
        Assert.That(
            _scan.SelectedNodes,
            Is.EqualTo(
                new[]
                {
                    branchNode,
                }));

        branchNode.IsExpanded = true;
        _host.Settle();
        _scan.SelectSingle(secondFile);
        _host.Settle();

        Assert.That(panel.HandleTreeLeftClick(TreeItem(tree, outsideFile), 1, ModifierKeys.Shift), Is.True);
        _host.Settle();
        Assert.That(
            _scan.SelectedNodes,
            Is.EquivalentTo(
                new[]
                {
                    secondFile, firstFile, outsideFile,
                }));

        _scan.SelectSingle(null);
        Assert.That(panel.HandleTreeKey(Key.A, ModifierKeys.Control), Is.True);
        _host.Settle();
        Assert.That(
            _scan.SelectedNodes,
            Is.EquivalentTo(
                new[]
                {
                    rootNode, branchNode, firstFile, secondFile, outsideFile,
                }));

        Assert.That(panel.HandleTreeKey(Key.Up, ModifierKeys.Shift), Is.True);
        _host.Settle();
        Assert.That(
            _scan.SelectedNodes,
            Is.EquivalentTo(
                new[]
                {
                    rootNode, branchNode, secondFile, firstFile,
                }));

        Assert.That(panel.HandleTreeKey(Key.Down, ModifierKeys.Shift), Is.True);
        _host.Settle();
        Assert.That(_scan.SelectedNodes, Has.Count.EqualTo(5));

        panel.HandleTreeRightClick(TreeItem(tree, branchNode));
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.SelectedNodes, Has.Count.EqualTo(5));
            Assert.That(_scan.SelectedNode, Is.SameAs(branchNode));
            Assert.That(branchNode.IsPrimarySelected, Is.True);
        }

        _scan.ToggleSelection(outsideFile);
        _host.Settle();
        Assert.That(outsideFile.IsSelected, Is.False);

        panel.HandleTreeRightClick(TreeItem(tree, outsideFile));
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                _scan.SelectedNodes,
                Is.EqualTo(
                    new[]
                    {
                        outsideFile,
                    }));

            Assert.That(_scan.SelectedNode, Is.SameAs(outsideFile));
            Assert.That(_scan.HasMultiSelection, Is.False);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Групповая_пометка_фильтрует_вложенные_дубли_пересчитывает_разом_и_откатывается_тостом()
    {
        var (_, _, branchNode, firstFile, secondFile, outsideFile) = PopulateTree();

        firstFile.MarkDeletedCommand.Execute(null);
        _host.Settle();
        Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));

        _scan.SelectSingle(branchNode);
        _scan.ToggleSelection(firstFile);
        _scan.ToggleSelection(secondFile);
        _scan.ToggleSelection(outsideFile);
        _host.Settle();

        var countUpdates = new List<int>();

        void OnMarksChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ScanMarksViewModel.MarkedCount))
            {
                countUpdates.Add(_scan.Marks.MarkedCount);
            }
        }

        _scan.Marks.PropertyChanged += OnMarksChanged;

        try
        {
            branchNode.MarkDeletedCommand.Execute(null);
            _host.Settle();

            var undoButtons = Descendants<Button>(_window)
                .Where(static button => Equals(button.Content, "Отменить") && button.Visibility == Visibility.Visible)
                .ToList();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(countUpdates, Is.EqualTo(new[] { 2 }));
                Assert.That(branchNode.IsMarkedDeleted, Is.True);
                Assert.That(outsideFile.IsMarkedDeleted, Is.True);
                Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(2));
                Assert.That(_scan.Marks.MarkedSizeText, Is.EqualTo(SizeFormatter.Format(208)));
                Assert.That(undoButtons, Has.Count.EqualTo(1));
            }

            countUpdates.Clear();
            undoButtons[0].Command.Execute(null);
            _host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(countUpdates, Is.EqualTo(new[] { 1 }));
                Assert.That(branchNode.IsMarkedDeleted, Is.False);
                Assert.That(outsideFile.IsMarkedDeleted, Is.False);
                Assert.That(firstFile.IsMarkedDeleted, Is.True);
                Assert.That(secondFile.IsMarkedDeleted, Is.False);
                Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));
                Assert.That(_scan.Marks.MarkedSizeText, Is.EqualTo(SizeFormatter.Format(64)));
                Assert.That(_toasts.Toasts, Is.Empty);
            }

            _scan.UnmarkSelectedDeleted();
            _host.Settle();
            var undoUnmark = UndoToast();
            _scan.SelectSingle(secondFile);
            secondFile.MarkDeletedCommand.Execute(null);
            _host.Settle();
            _scan.Marks.ApplyDeletionResult([firstFile.Space!]);
            undoUnmark.ActionCommand!.Execute(undoUnmark);
            _host.Settle();
            Assert.That(secondFile.IsMarkedDeleted, Is.True);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));

            _scan.SelectSingle(outsideFile);
            _scan.MarkSelectedDeleted();
            _host.Settle();
            var dismissed = UndoToast();
            _toasts.DismissCommand.Execute(dismissed);
            dismissed.ActionCommand!.Execute(dismissed);
            _host.Settle();
            Assert.That(outsideFile.IsMarkedDeleted, Is.True);
            Assert.That(_toasts.Toasts.Any(static toast => toast.HasAction), Is.False);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
        finally
        {
            _scan.Marks.PropertyChanged -= OnMarksChanged;
        }
    }

    private ToastViewModel UndoToast()
    {
        return _toasts.Toasts.Single(static toast => toast.HasAction && toast.ActionText == "Отменить");
    }

    [Test]
    public void Групповое_игнорирование_и_копирование_путей_исключают_потомков_выбранной_папки()
    {
        var (_, _, branchNode, firstFile, _, outsideFile) = PopulateTree();

        var clipboard = new FakeClipboard();
        var inspector = new ScanInspectorViewModel(new MemorySettings(), clipboard);
        inspector.Show([branchNode, firstFile, outsideFile], branchNode);
        inspector.CopyPathCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(inspector.IsMultiSelection, Is.True);
            Assert.That(inspector.IsSingleSelection, Is.False);
            Assert.That(inspector.ShowCounts, Is.True);
            Assert.That(inspector.ShowDates, Is.False);
            Assert.That(inspector.Title, Is.EqualTo("3 объекта"));
            Assert.That(inspector.KindText, Is.EqualTo("Папки и файлы"));
            Assert.That(inspector.TotalSizeText, Is.EqualTo(SizeFormatter.Format(208)));
            Assert.That(
                clipboard.LastText,
                Is.EqualTo(string.Join(Environment.NewLine, branchNode.AbsolutePath, outsideFile.AbsolutePath)));
        }

        _scan.SelectSingle(branchNode);
        _scan.ToggleSelection(firstFile);
        _scan.ToggleSelection(outsideFile);
        _host.Settle();

        branchNode.IgnoreCommand.Execute(null);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(branchNode.IsIgnored, Is.True);
            Assert.That(outsideFile.IsIgnored, Is.True);
            Assert.That(_factory.Visibility.Count, Is.EqualTo(2));
            Assert.That(_scan.HasIgnoredItems, Is.True);
        }

        branchNode.UnignoreCommand.Execute(null);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(branchNode.IsIgnored, Is.False);
            Assert.That(outsideFile.IsIgnored, Is.False);
            Assert.That(_factory.Visibility.Count, Is.EqualTo(0));
            Assert.That(_scan.HasIgnoredItems, Is.False);
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Клавиши_Delete_и_Space_переключают_пометки_выделенных_узлов_без_диалога()
    {
        var (_, _, _, firstFile, secondFile, outsideFile) = PopulateTree();
        var panel = Panel();

        _scan.SelectSingle(firstFile);
        _scan.ToggleSelection(outsideFile);
        _host.Settle();

        Assert.That(panel.HandleTreeKey(Key.Delete, ModifierKeys.None), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_modals.HasActive, Is.False);
            Assert.That(firstFile.IsMarkedDeleted, Is.True);
            Assert.That(outsideFile.IsMarkedDeleted, Is.True);
            Assert.That(secondFile.IsMarkedDeleted, Is.False);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(2));
        }

        Assert.That(panel.HandleTreeKey(Key.Space, ModifierKeys.None), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstFile.IsMarkedDeleted, Is.False);
            Assert.That(outsideFile.IsMarkedDeleted, Is.False);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(0));
        }

        Assert.That(panel.HandleTreeKey(Key.Space, ModifierKeys.None), Is.True);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstFile.IsMarkedDeleted, Is.True);
            Assert.That(outsideFile.IsMarkedDeleted, Is.True);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(2));
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    [Test]
    public void Щелчок_по_крупнейшему_в_инспекторе_выбирает_ребёнка_в_дереве_и_на_карте()
    {
        var (_, rootNode, branchNode, _, secondFile, _) = PopulateTree();
        var treemap = Descendant<TreemapView>(Panel())!;

        rootNode.IsExpanded = false;
        _scan.SelectSingle(rootNode);
        _host.Settle();

        var details = Descendant<ScanInspectorDetails>(_window)!;
        var branchChild = _scan.Inspector.TopChildren.First(static child => child.Name == "branch");
        var branchButton = Descendants<Button>(details).Single(button => ReferenceEquals(button.DataContext, branchChild));
        var branchGrid = Descendant<Grid>(branchButton)!;
        var branchRowWidth = branchGrid.ActualWidth;
        var inspectorWidth = details.ActualWidth;
        branchButton.Command.Execute(branchButton.CommandParameter);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(AutomationProperties.GetName(treemap), Is.EqualTo("Карта диска"));
            Assert.That(branchRowWidth, Is.EqualTo(inspectorWidth).Within(1));
            Assert.That(rootNode.IsExpanded, Is.True);
            Assert.That(_scan.SelectedNode, Is.SameAs(branchNode));
            Assert.That(_scan.Inspector.Node, Is.SameAs(branchNode));
        }

        _scan.ViewMode = ScanViewMode.Treemap;
        _scan.Treemap.SetRoot(rootNode);
        _scan.SelectSingle(branchNode);
        _host.Settle();

        var fileChild = _scan.Inspector.TopChildren.First(static child => child.Name == "b.bin");
        _scan.Inspector.SelectChildCommand.Execute(fileChild);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.Treemap.TreemapRoot, Is.SameAs(branchNode));
            Assert.That(_scan.SelectedNode, Is.SameAs(secondFile));
            Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
        }
    }

    private (DirectorySpace RootSpace, ScanNodeViewModel RootNode, ScanNodeViewModel BranchNode, ScanNodeViewModel FirstFile, ScanNodeViewModel SecondFile, ScanNodeViewModel
        OutsideFile) PopulateTree()
    {
        var stamp = new DateTime(2026, 2, 1);
        var caseDir = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caseDir);
        var root = new DirectorySpace(caseDir, null, stamp, stamp);
        var branch = new DirectorySpace("branch", root, stamp, stamp);
        root.Add(branch);
        AddFile(branch, "a.bin", 64);
        AddFile(branch, "b.bin", 128);
        AddFile(root, "outside.bin", 16);

        var rootNode = _factory.Create(root, root.TotalSize, root.TotalSize, root, new());
        _scan.Roots.Add(rootNode);
        rootNode.IsExpanded = true;
        _host.Settle();
        var branchNode = rootNode.Children.Single(static child => child.IsDirectory);
        branchNode.IsExpanded = true;
        _host.Settle();
        var files = branchNode.Children.Where(static child => !child.IsDirectory).ToList();
        return (root, rootNode, branchNode, files.Single(static c => c.Name == "a.bin"), files.Single(static c => c.Name == "b.bin"),
            rootNode.Children.Single(static c => !c.IsDirectory));
    }

    private static void AddFile(DirectorySpace directory, string name, int bytes)
    {
        Directory.CreateDirectory(directory.AbsolutePath);
        var path = Path.Combine(directory.AbsolutePath, name);
        File.WriteAllBytes(path, new byte[bytes]);
        directory.AddFile(new(path));
    }

    private ScanStructurePanel Panel()
    {
        return Descendant<ScanStructurePanel>(_window)!;
    }

    private static TreeView Tree(ScanStructurePanel panel)
    {
        return Descendant<TreeView>(panel)!;
    }

    private static TreeViewItem TreeItem(TreeView tree, ScanNodeViewModel node)
    {
        return Descendants<TreeViewItem>(tree).Single(item => ReferenceEquals(item.DataContext, node));
    }

    private static Brush? SelectionBackground(TreeViewItem item)
    {
        return ((Border)item.Template.FindName("SelectionBd", item)).Background;
    }

    private static void SaveFrame(FrameworkElement element, string name)
    {
        var artifacts = Path.Combine(RepositoryRoot(), ".kc", "artifacts", "261108-scan-tree-multi-selection");
        Directory.CreateDirectory(artifacts);
        ViewCapture.Save(element, Path.Combine(artifacts, $"{name}.png"));
    }

    private static string RepositoryRoot([CallerFilePath] string caller = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(caller)!, ".."));
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        if (root is T match)
        {
            yield return match;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
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
        return Descendants<T>(root).FirstOrDefault(item => !ReferenceEquals(item, root));
    }
}
