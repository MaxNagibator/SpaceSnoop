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
    public void CopyToRight_OverwritesReadOnlyDestination()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "new content");
        var destination = Path.Combine(_rightDir, "a.txt");
        File.WriteAllText(destination, "old content");
        File.SetAttributes(destination, File.GetAttributes(destination) | FileAttributes.ReadOnly);

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            Action = SyncAction.CopyToRight,
        });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(destination), Is.EqualTo("new content"));
            Assert.That(report.Errors, Is.Empty);
        }
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
    public void DeleteLeft_RemovesReadOnlyFile()
    {
        var filePath = Path.Combine(_leftDir, "remove.txt");
        File.WriteAllText(filePath, "delete me");
        File.SetAttributes(filePath, File.GetAttributes(filePath) | FileAttributes.ReadOnly);

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
            Assert.That(report.Errors, Is.Empty);
        }
    }

    [Test]
    public void DeleteLeft_SilentMode_RemovesFileWithoutUi()
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
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance, false);
        var report = engine.Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(filePath), Is.False);
            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.Errors, Is.Empty);
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

    [Test]
    public void FullPipeline_CreatesMissingDestination_AndCopiesContent()
    {
        Directory.Delete(_rightDir, true);
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello");
        var subLeft = Path.Combine(_leftDir, "sub");
        Directory.CreateDirectory(subLeft);
        File.WriteAllText(Path.Combine(subLeft, "b.txt"), "world");

        var comparer = new DirectoryComparer(new(string.Empty), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(SyncMode.LeftToRight, true);
        result.ResolveAllConflicts(SyncAction.Skip);
        var report = new SyncEngine(NullLogger<SyncEngine>.Instance).Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(_rightDir), Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_rightDir, "a.txt")), Is.EqualTo("hello"));
            Assert.That(File.ReadAllText(Path.Combine(_rightDir, "sub", "b.txt")), Is.EqualTo("world"));
            Assert.That(report.Errors, Is.Empty);
        }
    }

    [Test]
    public void Verify_AfterSuccessfulSync_ReportsNoMismatches()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello world");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var engine = new SyncEngine(NullLogger<SyncEngine>.Instance);
        var report = engine.Execute(result, CancellationToken.None);

        engine.Verify(report, _leftDir, _rightDir, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Mismatches, Is.Empty);
            Assert.That(report.Verified, Is.True);
        }
    }

    [Test]
    public void Verify_NotRun_LeavesReportUnverified()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello world");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var report = new SyncEngine(NullLogger<SyncEngine>.Instance).Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.SuccessCount, Is.EqualTo(1));
            Assert.That(report.Verified, Is.False);
        }
    }

    [Test]
    public void Verify_Cancelled_LeavesReportUnverifiedAndKeepsIt()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "data");

        var report = new SyncReport();
        report.AddApplied(SyncAction.CopyToRight, "a.txt", 4);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, cts.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Verified, Is.False);
            Assert.That(report.Mismatches, Is.Empty);
            Assert.That(report.Applied, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Execute_FileErrorsAreNotFatal_AndDoNotVerifyReport()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "first");
        File.WriteAllText(Path.Combine(_leftDir, "b.txt"), "second");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("missing.txt", "missing.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var report = new SyncEngine(NullLogger<SyncEngine>.Instance).Execute(result, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Errors, Has.Count.EqualTo(1));
            Assert.That(report.SuccessCount, Is.EqualTo(2));
            Assert.That(report.Verified, Is.False);
        }
    }

    [Test]
    public void Verify_MissingDestination_ReportsMismatch()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "data");

        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.CopyToRight, "a.txt", 4));

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Mismatches, Has.Count.EqualTo(1));
            Assert.That(report.Mismatches[0].RelativePath, Is.EqualTo("a.txt"));
        }
    }

    [Test]
    public void Verify_DivergentContent_ReportsMismatch()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "source-long-content");
        File.WriteAllText(Path.Combine(_rightDir, "a.txt"), "x");

        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.CopyToRight, "a.txt", 0));

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, CancellationToken.None);

        Assert.That(report.Mismatches, Has.Count.EqualTo(1));
    }

    [Test]
    public void Verify_SourceGoneAfterCopy_ReportsMismatch()
    {
        File.WriteAllText(Path.Combine(_rightDir, "a.txt"), "data");

        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.CopyToRight, "a.txt", 4));

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Mismatches, Has.Count.EqualTo(1));
            Assert.That(report.Mismatches[0].RelativePath, Is.EqualTo("a.txt"));
        }
    }

    [Test]
    public void Verify_DeletedFileStillPresent_ReportsMismatch()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "still here");

        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.DeleteLeft, "a.txt", 0));

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, CancellationToken.None);

        Assert.That(report.Mismatches, Has.Count.EqualTo(1));
    }

    [Test]
    public void Verify_DeletedFileGone_NoMismatch()
    {
        var report = new SyncReport();
        report.Applied.Add(new(SyncAction.DeleteLeft, "gone.txt", 0));

        new SyncEngine(NullLogger<SyncEngine>.Instance).Verify(report, _leftDir, _rightDir, CancellationToken.None);

        Assert.That(report.Mismatches, Is.Empty);
    }

    [Test]
    public void WriteDetails_ListsAppliedActionsWithSizes_AndOmitsZeroSize()
    {
        File.WriteAllText(Path.Combine(_leftDir, "copy.txt"), "data");
        File.WriteAllText(Path.Combine(_leftDir, "gone.txt"), "data");

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("copy.txt", "copy.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight, LeftSize = 1024 });
        root.Files.Add(new("gone.txt", "gone.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.DeleteLeft, LeftSize = 0 });

        var result = new ComparisonResult(_leftDir, _rightDir, root);
        var report = new SyncEngine(NullLogger<SyncEngine>.Instance).Execute(result, CancellationToken.None);

        var writer = new StringWriter();
        report.WriteDetails(writer);
        var text = writer.ToString();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Applied, Has.Count.EqualTo(2));
            Assert.That(text, Does.Contain("CopyToRight «copy.txt» (1 КБ)"));
            Assert.That(text, Does.Contain("DeleteLeft «gone.txt»"));
            Assert.That(text, Does.Not.Contain("gone.txt» ("));
        }
    }
}
