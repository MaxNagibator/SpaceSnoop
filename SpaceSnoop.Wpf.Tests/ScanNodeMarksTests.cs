using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanNodeMarksTests
{
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

    private string _tempDir = null!;
    private ScanNodeFactory _factory = null!;

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

    private ScanNodeViewModel Arrange()
    {
        var space = new DirectorySpace(_tempDir, null, DateTime.Now, DateTime.Now);
        var branch = new DirectorySpace("branch", space, DateTime.Now, DateTime.Now);

        branch.AddFile(MakeFile("payload", 64));
        space.Add(branch);

        var root = _factory.CreateRoot(space, new());
        root.EnsureLoaded();

        ScanNodeViewModel[] roots = [root];
        _factory.MarksChanged += () => _factory.MarksPresent = ScanTreeEditor.CollectMarked(roots).Count > 0;

        return root;
    }

    private FileInfo MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, $"{name}.bin");
        File.WriteAllBytes(path, new byte[size]);

        return new(path);
    }
}
