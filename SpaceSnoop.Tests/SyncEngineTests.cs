using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Tests;

[TestFixture]
public class SyncEngineTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTest_{Guid.NewGuid():N}");
        _leftDir = Path.Combine(_tempDir, "left");
        _rightDir = Path.Combine(_tempDir, "right");
        Directory.CreateDirectory(_leftDir);
        Directory.CreateDirectory(_rightDir);
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
    private string _leftDir = null!;
    private string _rightDir = null!;

    [Test]
    public void CopyToRight_CopiesFileFromLeftToRight()
    {
        var content = "hello world";
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), content);

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.CopyToRight,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_rightDir, "a.txt")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_rightDir, "a.txt")), Is.EqualTo(content));
            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.Errors, Is.Empty);
        }
    }

    [Test]
    public void CopyToLeft_CopiesFileFromRightToLeft()
    {
        const string Content = "hello world";
        File.WriteAllText(Path.Combine(_rightDir, "b.txt"), Content);

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("b.txt", "b.txt")
        {
            Status = ComparisonStatus.RightOnly,
            Action = SyncAction.CopyToLeft,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_leftDir, "b.txt")), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_leftDir, "b.txt")), Is.EqualTo(Content));
            Assert.That(report.SuccessCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void CopyToRight_CreatesSubdirectory()
    {
        var subLeft = Path.Combine(_leftDir, "sub");
        Directory.CreateDirectory(subLeft);
        File.WriteAllText(Path.Combine(subLeft, "file.txt"), "data");

        var root = new DirectoryComparison("root", "");
        var sub = new DirectoryComparison("sub", "sub") { Status = ComparisonStatus.LeftOnly };
        sub.Files.Add(new("file.txt", "sub\\file.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.CopyToRight,
        });

        root.SubDirectories.Add(sub);

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        engine.Execute(result, CancellationToken.None);

        Assert.That(File.Exists(Path.Combine(_rightDir, "sub", "file.txt")), Is.True);
    }

    [Test]
    public void CopyToRight_CreatesEmptyOneSidedDirectory()
    {
        Directory.CreateDirectory(Path.Combine(_leftDir, "empty"));

        var root = new DirectoryComparison("root", "");
        root.SubDirectories.Add(new("empty", "empty")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.CopyToRight,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(_rightDir, "empty")), Is.True);
            Assert.That(report.CopiedCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void ApplyMode_AssignsActionsToOneSidedDirectories_AndCountsThem()
    {
        var root = new DirectoryComparison("root", "");
        var left = new DirectoryComparison("onlyLeft", "onlyLeft") { Status = ComparisonStatus.LeftOnly };
        var right = new DirectoryComparison("onlyRight", "onlyRight") { Status = ComparisonStatus.RightOnly };
        root.SubDirectories.Add(left);
        root.SubDirectories.Add(right);

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        result.ApplyMode(SyncMode.LeftToRight);

        var planned = result.CountPlannedActions();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(left.Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(right.Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(planned.DirCopies, Is.EqualTo(1));
            Assert.That(planned.Total, Is.EqualTo(1));
        }
    }

    [Test]
    public void Skip_DoesNothing()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.Skip,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_rightDir, "a.txt")), Is.False);
            Assert.That(report.SuccessCount, Is.Zero);
        }
    }

    [Test]
    public void CopyToRight_OverwritesExistingFile()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "new content");
        File.WriteAllText(Path.Combine(_rightDir, "a.txt"), "old content");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            Action = SyncAction.CopyToRight,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        engine.Execute(result, CancellationToken.None);

        Assert.That(File.ReadAllText(Path.Combine(_rightDir, "a.txt")), Is.EqualTo("new content"));
    }

    [Test]
    public void ErrorOnOneFile_ContinuesWithOthers()
    {
        File.WriteAllText(Path.Combine(_leftDir, "good.txt"), "data");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("bad.txt", "bad.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.CopyToRight,
        });

        root.Files.Add(new("good.txt", "good.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.CopyToRight,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(Path.Combine(_rightDir, "good.txt")), Is.True);
            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.Errors, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void DeleteLeft_RemovesFile()
    {
        var filePath = Path.Combine(_leftDir, "remove.txt");
        File.WriteAllText(filePath, "delete me");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("remove.txt", "remove.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.DeleteLeft,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(filePath), Is.False);
            Assert.That(report.SuccessCount, Is.EqualTo(1));
        }
    }

    [Test]
    public void Report_SplitsCopiedAndDeleted()
    {
        File.WriteAllText(Path.Combine(_leftDir, "copy.txt"), "data");
        File.WriteAllText(Path.Combine(_leftDir, "gone.txt"), "data");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("copy.txt", "copy.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });
        root.Files.Add(new("gone.txt", "gone.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.DeleteLeft });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.CopiedCount, Is.EqualTo(1));
            Assert.That(report.DeletedCount, Is.EqualTo(1));
            Assert.That(report.SuccessCount, Is.EqualTo(2));
        }
    }
}
