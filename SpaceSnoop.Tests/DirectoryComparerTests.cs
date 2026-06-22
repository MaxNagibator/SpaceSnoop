using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DirectoryComparerTests
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
    public void IdenticalDirectories_AllIdentical()
    {
        var leftPath = Path.Combine(_leftDir, "a.txt");
        var rightPath = Path.Combine(_rightDir, "a.txt");
        File.WriteAllText(leftPath, "hello");
        File.WriteAllText(rightPath, "hello");
        var timestamp = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        File.SetLastWriteTime(leftPath, timestamp);
        File.SetLastWriteTime(rightPath, timestamp);

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        Assert.That(result.Root.Files[0].Status, Is.EqualTo(ComparisonStatus.Identical));
    }

    [Test]
    public void FileOnlyInLeft_MarkedLeftOnly()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        Assert.That(result.Root.Files[0].Status, Is.EqualTo(ComparisonStatus.LeftOnly));
    }

    [Test]
    public void FileOnlyInRight_MarkedRightOnly()
    {
        File.WriteAllText(Path.Combine(_rightDir, "b.txt"), "world");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        Assert.That(result.Root.Files[0].Status, Is.EqualTo(ComparisonStatus.RightOnly));
    }

    [Test]
    public void DifferentFileSize_MarkedModified()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "short");
        File.WriteAllText(Path.Combine(_rightDir, "a.txt"), "this is longer content");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        Assert.That(result.Root.Files[0].Status, Is.EqualTo(ComparisonStatus.Modified));
    }

    [Test]
    public void SubdirOnlyInLeft_MarkedLeftOnly()
    {
        var sub = Path.Combine(_leftDir, "subdir");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "file.txt"), "data");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.SubDirectories, Has.Count.EqualTo(1));
        Assert.That(result.Root.SubDirectories[0].Status, Is.EqualTo(ComparisonStatus.LeftOnly));
    }

    [Test]
    public void RecursiveComparison_FindsNestedDifferences()
    {
        var leftSub = Path.Combine(_leftDir, "sub");
        var rightSub = Path.Combine(_rightDir, "sub");
        Directory.CreateDirectory(leftSub);
        Directory.CreateDirectory(rightSub);

        File.WriteAllText(Path.Combine(leftSub, "same.txt"), "same");
        File.WriteAllText(Path.Combine(rightSub, "same.txt"), "same");
        File.WriteAllText(Path.Combine(leftSub, "only-left.txt"), "left");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        var sub = result.Root.SubDirectories[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(sub.Status, Is.EqualTo(ComparisonStatus.Modified));
            Assert.That(sub.Files, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void ExclusionFilter_SkipsMatchingFiles()
    {
        File.WriteAllText(Path.Combine(_leftDir, "keep.txt"), "data");
        File.WriteAllText(Path.Combine(_leftDir, "skip.tmp"), "temp");

        var comparer = new DirectoryComparer(new("*.tmp"), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        Assert.That(result.Root.Files[0].Name, Is.EqualTo("keep.txt"));
    }

    [Test]
    public void ExclusionFilter_SkipsMatchingDirectories()
    {
        var gitDir = Path.Combine(_leftDir, ".git");
        Directory.CreateDirectory(gitDir);
        File.WriteAllText(Path.Combine(gitDir, "config"), "data");

        File.WriteAllText(Path.Combine(_leftDir, "readme.txt"), "hello");

        var comparer = new DirectoryComparer(new(".git"), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.SubDirectories, Is.Empty);
            Assert.That(result.Root.Files, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void FileComparison_ContainsMetadata()
    {
        File.WriteAllText(Path.Combine(_leftDir, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(_rightDir, "a.txt"), "hello world");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        var file = result.Root.Files[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.LeftSize, Is.EqualTo(5));
            Assert.That(file.RightSize, Is.EqualTo(11));
            Assert.That(file.LeftModified, Is.Not.Null);
            Assert.That(file.RightModified, Is.Not.Null);
        }
    }
}
