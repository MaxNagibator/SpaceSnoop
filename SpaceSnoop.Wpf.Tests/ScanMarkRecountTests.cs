using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanMarkRecountTests
{
    private readonly ObservableCollection<ScanNodeViewModel> _roots = [];
    private string _directory = null!;
    private ScanNodeFactory _factory = null!;
    private ScanMarksViewModel _marks = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SpaceSnoopMarkRecount", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        var settings = new MemorySettings();
        _factory = new(new(settings), new(settings), new FakeShellLauncher());
        _roots.Clear();
        _marks = new(
            null!,
            null!,
            null!,
            NullLogger.Instance,
            _factory,
            _roots,
            null!,
            null!,
            () => null,
            _ =>
            {
            },
            () =>
            {
            },
            () => false,
            () =>
            {
            });
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_directory, true);
    }

    [Test]
    public void Команда_снятого_дерева_не_меняет_счётчик_нового_результата()
    {
        var previous = AddTree("root");
        var oldNode = Node(previous.Files.Single(), previous);
        _roots.Clear();
        AddTree("fresh");

        oldNode.MarkDeletedCommand.Execute(null);

        CheckTotals(0, 0);
    }

    [Test]
    public void Ошибочный_узел_не_создаёт_пометку_и_не_теряет_пометки_детей()
    {
        var root = AddTree("root");
        root.Error();
        var rootNode = Node(root, root);
        rootNode.MarkDeletedCommand.Execute(null);
        CheckTotals(0, 0);
        Node(root.Files.Single(), root).MarkDeletedCommand.Execute(null);
        CheckTotals(1, 16);
        rootNode.MarkDeletedCommand.Execute(null);
        CheckTotals(1, 16);
    }

    [Test]
    public void Повторный_корень_в_коллекции_сохраняет_кратность_пересчёта()
    {
        var root = AddTree("root");
        _roots.Add(_roots.Single());
        _marks.RecountMarked();

        Node(root.Files.Single(), root).MarkDeletedCommand.Execute(null);
        CheckTotals(2, 32);
        Node(root, root).MarkDeletedCommand.Execute(null);
        CheckTotals(2, 416);
    }

    [Test]
    public void Пометка_каталога_поглощает_прежние_пометки_его_файлов()
    {
        var root = AddTree("root");
        var branch = root.SubDirectories.Single();
        Node(branch.Files[0], root).MarkDeletedCommand.Execute(null);
        Node(root.Files.Single(), root).MarkDeletedCommand.Execute(null);
        CheckTotals(2, 80);

        Node(branch, root).MarkDeletedCommand.Execute(null);

        CheckTotals(2, 208);
    }

    [Test]
    public void Пометка_файла_сохраняет_сводку_другого_корня()
    {
        var first = AddTree("first");
        var second = AddTree("second");
        var firstFile = Node(first.SubDirectories.Single().Files[0], first);
        var secondFile = Node(second.Files.Single(), second);

        firstFile.MarkDeletedCommand.Execute(null);
        CheckTotals(1, 64);
        secondFile.MarkDeletedCommand.Execute(null);
        CheckTotals(2, 80);
        firstFile.UnmarkDeletedCommand.Execute(null);
        CheckTotals(1, 16);
    }

    [Test]
    public void Помеченный_предок_учитывается_и_через_ошибочную_ветвь()
    {
        var root = AddTree("root");
        var branch = root.SubDirectories.Single();
        branch.Error();
        Node(root, root).MarkDeletedCommand.Execute(null);
        CheckTotals(1, 208);
        var file = Node(branch.Files[0], root);
        file.MarkDeletedCommand.Execute(null);
        CheckTotals(1, 208);
        file.UnmarkDeletedCommand.Execute(null);
        CheckTotals(1, 208);
        Node(root, root).UnmarkDeletedCommand.Execute(null);
        CheckTotals(0, 0);
    }

    [Test]
    public void Последовательность_команд_совпадает_с_полным_пересчётом_после_каждого_шага()
    {
        var first = AddTree("first");
        var second = AddTree("second");
        var branch = first.SubDirectories.Single();
        ScanNodeViewModel[] targets =
        [
            Node(first, first), Node(branch, first), Node(branch.Files[0], first),
            Node(branch.Files[1], first), Node(first.Files.Single(), first), Node(second, second),
            Node(second.SubDirectories.Single(), second), Node(second.Files.Single(), second),
        ];

        for (var index = 0; index < 128; index++)
        {
            var node = targets[((index * 17) + (index / 7)) % targets.Length];
            var command = (((index * 7) + (index / 5)) % 4) switch
            {
                0 => node.MarkDeletedCommand,
                1 => node.UnmarkDeletedCommand,
                2 => node.MarkContentsDeletedCommand,
                _ => node.UnmarkContentsCommand,
            };

            command.Execute(null);
            CheckAgainstTree();
        }
    }

    [Test]
    public void Снятие_пометки_потомка_раскрывает_пометки_соседей_помеченного_корня()
    {
        var root = AddTree("root");
        Node(root, root).MarkDeletedCommand.Execute(null);
        CheckTotals(1, 208);

        Node(root.SubDirectories.Single().Files[0], root).UnmarkDeletedCommand.Execute(null);

        CheckTotals(2, 144);
        Assert.That(root.IsDeleted, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Снятие_пометок_содержимого_сохраняет_пометки_вне_ветви(bool markRoot)
    {
        var root = AddTree("root");
        var rootNode = Node(root, root);

        if (markRoot)
        {
            rootNode.MarkDeletedCommand.Execute(null);
        }
        else
        {
            rootNode.MarkContentsDeletedCommand.Execute(null);
        }

        CheckTotals(markRoot ? 1 : 2, 208);
        Node(root.SubDirectories.Single(), root).UnmarkContentsCommand.Execute(null);
        CheckTotals(1, 16);
        rootNode.UnmarkContentsCommand.Execute(null);
        CheckTotals(0, 0);
    }

    private DirectorySpace AddTree(string name)
    {
        var root = new DirectorySpace(Path.Combine(_directory, name), null, DateTime.Now, DateTime.Now);
        var branch = new DirectorySpace("branch", root, DateTime.Now, DateTime.Now);
        root.Add(branch);
        AddFile(branch, "a.bin", 64);
        AddFile(branch, "b.bin", 128);
        AddFile(root, "outside.bin", 16);
        var node = Node(root, root);
        _roots.Add(node);
        _marks.RecountMarkedAfterScan(node, []);
        return root;
    }

    private ScanNodeViewModel Node(SpaceBase space, DirectorySpace root)
    {
        return _factory.Create(space, 1, 1, root, new());
    }

    private static void AddFile(DirectorySpace directory, string name, int bytes)
    {
        Directory.CreateDirectory(directory.AbsolutePath);
        var path = Path.Combine(directory.AbsolutePath, name);
        File.WriteAllBytes(path, new byte[bytes]);
        directory.AddFile(new(path));
    }

    private void CheckTotals(int count, long bytes)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(_marks.MarkedCount, Is.EqualTo(count));
            Assert.That(_marks.MarkedSizeText, Is.EqualTo(count > 0 ? SizeFormatter.Format(bytes) : string.Empty));
        }

        CheckAgainstTree();
    }

    private void CheckAgainstTree()
    {
        var actual = ScanTreeEditor.CollectMarked(_roots);
        var bytes = _roots.Sum(root => _factory.CaptureMarks(root.Space!, root.Space!).Bytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_marks.MarkedCount, Is.EqualTo(actual.Count));
            Assert.That(_marks.MarkedSizeText, Is.EqualTo(actual.Count > 0 ? SizeFormatter.Format(actual.Sum(static item => item.TotalSize)) : string.Empty));
            Assert.That(_factory.MarksPresent, Is.EqualTo(actual.Count > 0));
            Assert.That(bytes, Is.EqualTo(actual.Sum(static item => item.TotalSize)));
        }
    }
}
