using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.Views.Scan;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanTreeLayoutTests
{
    private string _directory = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private ScanViewModel _scan = null!;
    private ScanNodeFactory _factory = null!;
    private TreeView _tree = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        _directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopTreeLayout", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);
        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_directory, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _scan = _services.GetRequiredService<ScanViewModel>();
        _factory = _services.GetRequiredService<ScanNodeFactory>();
        var panel = new ScanStructurePanel
        {
            DataContext = _scan,
        };

        _host = VisualTestHost.Show(
            new()
            {
                Content = panel,
            },
            1000,
            700);

        _host.Settle();
        _tree = Descendants<TreeView>(panel).Single();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();
        Directory.Delete(_directory, true);
    }

    [SetUp]
    public void SetUp()
    {
        _scan.Roots.Clear();
        _scan.Marks.RecountMarked();
        _host.Settle();
    }

    [TestCase(1000)]
    [TestCase(50_000)]
    public void Вложенный_каталог_раскладывает_видимые_строки_и_сохраняет_выбор_при_прокрутке(int count)
    {
        var root = Build(count);
        var sort = new ScanSortState();
        var node = _factory.Create(root, root.TotalSize, root.TotalSize, root, sort);
        _scan.Roots.Add(node);
        node.IsExpanded = true;
        _host.Settle();
        var branch = node.Children.Single();
        Assert.That(Descendants<TreeViewItem>(_tree).Any(item => item.DataContext is ScanNodeViewModel file && file.Name.StartsWith("sample-", StringComparison.Ordinal)), Is.False);
        branch.IsExpanded = true;
        _host.Settle();
        Assert.That(branch.Children, Has.Count.EqualTo(count));
        Assert.That(branch.Children.Select(static child => child.Weight), Is.Ordered.Descending);

        var opened = Descendants<TreeViewItem>(_tree).ToHashSet();
        var containers = new HashSet<TreeViewItem>(opened);
        var scroll = Descendants<ScrollViewer>(_tree).First();
        var row = opened.First(item => ReferenceEquals(item.DataContext, branch.Children[0]));
        var height = row.ActualHeight;
        Assert.That(height, Is.GreaterThan(0));
        Assert.That(scroll.ViewportHeight, Is.GreaterThan(0).And.LessThan(scroll.ExtentHeight));
        Assert.That(VirtualizingPanel.GetScrollUnit(_tree), Is.EqualTo(ScrollUnit.Pixel));
        var budget = ((int)Math.Ceiling(scroll.ViewportHeight / height) * 5) + 16;

        for (var page = 1; page <= 5; page++)
        {
            var previous = scroll.VerticalOffset;
            scroll.ScrollToVerticalOffset(page * scroll.ViewportHeight);
            _host.Settle();
            Assert.That(scroll.VerticalOffset, Is.GreaterThan(previous));
            containers.UnionWith(Descendants<TreeViewItem>(_tree));
        }

        scroll.ScrollToEnd();
        _host.Settle();
        var tail = Descendants<TreeViewItem>(_tree).ToList();
        containers.UnionWith(tail);
        var last = branch.Children[^1];
        var selected = tail.Single(item => ReferenceEquals(item.DataContext, last));
        selected.IsSelected = true;
        last.MarkDeletedCommand.Execute(null);
        _host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Has.Count.LessThanOrEqualTo(budget));
            Assert.That(containers, Has.Count.LessThanOrEqualTo(budget));
            Assert.That(_tree.SelectedItem, Is.SameAs(last));
            Assert.That(_scan.SelectedNode, Is.SameAs(last));
            Assert.That(last.IsMarkedDeleted, Is.True);
            Assert.That(_scan.Marks.MarkedCount, Is.EqualTo(1));
        }

        scroll.ScrollToTop();
        _host.Settle();
        scroll.ScrollToEnd();
        _host.Settle();
        Assert.That(_tree.SelectedItem, Is.SameAs(last));
        Assert.That(Descendants<TreeViewItem>(_tree).Any(item => ReferenceEquals(item.DataContext, last)), Is.True);
        branch.IsExpanded = false;
        _host.Settle();
        branch.IsExpanded = true;
        _host.Settle();
        Assert.That(branch.Children.Single(child => ReferenceEquals(child.Space, last.Space)).IsMarkedDeleted, Is.True);

        sort.Field = ScanSortField.Name;
        sort.Invert = false;
        node.Resort();
        _host.Settle();
        Assert.That(branch.Children, Has.Count.EqualTo(count));
        Assert.That(branch.Children.Select(static child => child.Name), Is.Ordered);
        Assert.That(branch.Children.Single(child => ReferenceEquals(child.Space, last.Space)).IsMarkedDeleted, Is.True);
        TestContext.Out.WriteLine($"Детей {count}, контейнеров при открытии {opened.Count}, за прокрутку {containers.Count}, бюджет {budget}.");
    }

    [Test]
    public void Программный_переход_между_выделенными_узлами_не_возвращает_промежуточный_выбор_в_модель()
    {
        var root = Build(2);
        var node = _factory.Create(root, root.TotalSize, root.TotalSize, root, new());
        node.EnsureLoaded();
        var branch = node.Children.Single();
        branch.EnsureLoaded();
        var leaf = branch.Children[0];
        var notifications = 0;
        PropertyChangedEventHandler countNotifications = (_, args) =>
        {
            if (args.PropertyName == nameof(ScanNodeViewModel.IsSelected) && ++notifications > 12)
            {
                throw new InvalidOperationException("Выделение узлов рекурсивно изменяет само себя.");
            }
        };

        foreach (var candidate in new[]
        {
            node, leaf,
        })
        {
            candidate.PropertyChanged += countNotifications;
        }

        var selections = new List<ScanNodeViewModel?>();
        PropertyChangedEventHandler recordSelection = (_, args) =>
        {
            if (args.PropertyName == nameof(ScanViewModel.SelectedNode))
            {
                selections.Add(_scan.SelectedNode);
            }
        };

        try
        {
            _scan.HasResult = true;
            _scan.Roots.Add(node);
            node.IsExpanded = true;
            branch.IsExpanded = true;
            _host.Settle();
            Assert.That(Descendants<TreeViewItem>(_tree).Any(item => ReferenceEquals(item.DataContext, leaf)), Is.True);
            _scan.SelectedNode = node;
            _host.Settle();
            notifications = 0;
            _scan.PropertyChanged += recordSelection;

            _scan.SelectedNode = leaf;
            _host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_scan.SelectedNode, Is.SameAs(leaf));
                Assert.That(_tree.SelectedItem, Is.SameAs(leaf));
                Assert.That(node.IsSelected, Is.False);
                Assert.That(leaf.IsSelected, Is.True);
                Assert.That(
                    selections,
                    Is.EqualTo(
                        new[]
                        {
                            leaf,
                        }));

                Assert.That(notifications, Is.EqualTo(2));
            }

            Descendants<TreeViewItem>(_tree)
                .Single(item => ReferenceEquals(item.DataContext, node))
                .SetCurrentValue(TreeViewItem.IsSelectedProperty, true);

            _host.Settle();
            Assert.That(_scan.SelectedNode, Is.SameAs(node));
            Assert.That(leaf.IsSelected, Is.False);

            _scan.SelectedNode = null;
            _host.Settle();
            Assert.That(_tree.SelectedItem, Is.Null);
            Assert.That(node.IsSelected, Is.False);
        }
        finally
        {
            _scan.PropertyChanged -= recordSelection;
            node.PropertyChanged -= countNotifications;
            leaf.PropertyChanged -= countNotifications;
            _scan.SelectedNode = null;
        }
    }

    private DirectorySpace Build(int count)
    {
        var stamp = new DateTime(2026, 1, 1);
        var root = new DirectorySpace(_directory, null, stamp, stamp);
        var branch = new DirectorySpace("branch", root, stamp, stamp);
        var samples = Enumerable.Range(0, 16)
            .Select(index =>
            {
                var path = Path.Combine(_directory, $"sample-{index:D2}.bin");
                File.WriteAllBytes(path, new byte[index + 1]);
                return new FileInfo(path);
            })
            .ToArray();

        var files = Enumerable.Range(0, count).Select(index => samples[index % samples.Length]).ToArray();
        branch.AddFiles(files.AsSpan());
        root.Add(branch);
        return root;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
