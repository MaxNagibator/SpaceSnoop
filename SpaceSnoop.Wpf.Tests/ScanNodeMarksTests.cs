using KeepShell.Testing;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanNodeMarksTests
{
    private string _tempDir = null!;
    private ScanNodeFactory _factory = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopMarks_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var settings = new MemorySettings();
        _factory = new(new(settings), new(settings), new FakeShellLauncher());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Без_пометок_снятие_содержимого_недоступно()
    {
        var root = Arrange();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.CanMarkContentsDeleted, Is.True);
            Assert.That(root.CanUnmarkContents, Is.False);
        }
    }

    [Test]
    public void Пометка_содержимого_сразу_разрешает_снятие()
    {
        var root = Arrange();
        bool? seenOnNotify = null;

        root.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanNodeViewModel.CanUnmarkContents))
            {
                seenOnNotify ??= root.CanUnmarkContents;
            }
        };

        root.MarkContentsDeletedCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.CanUnmarkContents, Is.True);
            Assert.That(root.CanMarkContentsDeleted, Is.False);
            Assert.That(seenOnNotify, Is.True);
        }
    }

    [Test]
    public void Снятие_пометки_возвращает_узел_в_исходное_состояние()
    {
        var root = Arrange();
        root.MarkContentsDeletedCommand.Execute(null);

        root.UnmarkContentsCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.CanMarkContentsDeleted, Is.True);
            Assert.That(root.CanUnmarkContents, Is.False);
        }
    }

    [Test]
    public void Пустая_папка_не_предлагает_массовых_действий_с_содержимым()
    {
        var emptySpace = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        var root = _factory.CreateRoot(emptySpace, new());
        root.EnsureLoaded();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.CanMarkContentsDeleted, Is.False);
            Assert.That(root.CanUnmarkContents, Is.False);
        }
    }

    [Test]
    public void Частичная_пометка_на_глубине_оставляет_доступными_допометку_и_снятие()
    {
        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        var branch = new DirectorySpace("branch", space, DateTime.Now, DateTime.Now);
        var nested = new DirectorySpace("nested", branch, DateTime.Now, DateTime.Now);
        var firstFile = nested.AddFile(MakeFile("first", 32));
        var secondFile = nested.AddFile(MakeFile("second", 48));
        var rootSibling = space.AddFile(MakeFile("root-sibling", 16));
        branch.Add(nested);
        space.Add(branch);

        var root = _factory.CreateRoot(space, new());
        ScanNodeViewModel[] roots = [root];
        _factory.MarksChanged += _ => _factory.MarksPresent = ScanTreeEditor.CollectMarked(roots).Count > 0;

        root.EnsureLoaded();
        var branchNode = root.Children.Single(static c => c.Space is DirectorySpace);
        branchNode.EnsureLoaded();
        var nestedNode = branchNode.Children.Single();
        nestedNode.EnsureLoaded();
        var firstFileNode = nestedNode.Children.Single(c => ReferenceEquals(c.Space, firstFile));

        var rootNotified = false;
        var branchNotified = false;
        root.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanNodeViewModel.CanUnmarkContents))
            {
                rootNotified = true;
            }
        };
        branchNode.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanNodeViewModel.CanUnmarkContents))
            {
                branchNotified = true;
            }
        };

        firstFileNode.MarkDeletedCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rootNotified, Is.True);
            Assert.That(branchNotified, Is.True);
            Assert.That(nestedNode.CanMarkContentsDeleted, Is.True);
            Assert.That(nestedNode.CanUnmarkContents, Is.True);
            Assert.That(branchNode.CanMarkContentsDeleted, Is.True);
            Assert.That(branchNode.CanUnmarkContents, Is.True);
            Assert.That(root.CanMarkContentsDeleted, Is.True);
            Assert.That(root.CanUnmarkContents, Is.True);
        }

        root.MarkContentsDeletedCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondFile.IsDeleted, Is.True);
            Assert.That(rootSibling.IsDeleted, Is.True);
            Assert.That(root.CanMarkContentsDeleted, Is.False);
            Assert.That(root.CanUnmarkContents, Is.True);
        }
    }

    [Test]
    public void Узлы_ошибки_и_игнорирования_учитываются_в_массовой_пометке_содержимого()
    {
        var errorOnlySpace = new DirectorySpace(Path.Combine(_tempDir, "err-only"), null, DateTime.Now, DateTime.Now);
        var failedSub = new DirectorySpace("denied", errorOnlySpace, DateTime.Now, DateTime.Now);
        failedSub.Error();
        errorOnlySpace.Add(failedSub);

        var errorOnlyRoot = _factory.CreateRoot(errorOnlySpace, new());

        var mixedSpace = new DirectorySpace(Path.Combine(_tempDir, "mixed"), null, DateTime.Now, DateTime.Now);
        var mixedError = new DirectorySpace("broken", mixedSpace, DateTime.Now, DateTime.Now);
        mixedError.Error();
        var ignoredSub = new DirectorySpace("ignored", mixedSpace, DateTime.Now, DateTime.Now);
        ignoredSub.AddFile(MakeFile("ignored-payload", 24));
        mixedSpace.Add(mixedError);
        mixedSpace.Add(ignoredSub);
        _factory.Visibility.Ignore(ignoredSub);

        var mixedRoot = _factory.CreateRoot(mixedSpace, new());
        ScanNodeViewModel[] roots = [mixedRoot];
        _factory.MarksChanged += _ => _factory.MarksPresent = ScanTreeEditor.CollectMarked(roots).Count > 0;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(errorOnlyRoot.CanMarkContentsDeleted, Is.False);
            Assert.That(errorOnlyRoot.CanUnmarkContents, Is.False);
            Assert.That(mixedRoot.CanMarkContentsDeleted, Is.True);
            Assert.That(mixedRoot.CanUnmarkContents, Is.False);
        }

        mixedRoot.MarkContentsDeletedCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mixedError.IsDeleted, Is.False);
            Assert.That(ignoredSub.IsDeleted, Is.True);
            Assert.That(mixedRoot.CanMarkContentsDeleted, Is.False);
            Assert.That(mixedRoot.CanUnmarkContents, Is.True);
        }
    }

    private ScanNodeViewModel Arrange()
    {
        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        var branch = new DirectorySpace("branch", space, DateTime.Now, DateTime.Now);

        branch.AddFile(MakeFile("payload", 64));
        space.Add(branch);

        var root = _factory.CreateRoot(space, new());
        root.EnsureLoaded();

        ScanNodeViewModel[] roots = [root];
        _factory.MarksChanged += _ => _factory.MarksPresent = ScanTreeEditor.CollectMarked(roots).Count > 0;

        return root;
    }

    private FileInfo MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, $"{name}.bin");
        File.WriteAllBytes(path, new byte[size]);

        return new(path);
    }
}
