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

    [TestCase(2, ExpectedResult = ComparisonStatus.Identical)]
    [TestCase(3, ExpectedResult = ComparisonStatus.Modified)]
    public ComparisonStatus SameSizeTimestampWithinFatGranularity_TreatedIdentical(int secondsApart)
    {
        var leftPath = Path.Combine(_leftDir, "a.txt");
        var rightPath = Path.Combine(_rightDir, "a.txt");
        File.WriteAllText(leftPath, "same content");
        File.WriteAllText(rightPath, "same content");
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Local);
        File.SetLastWriteTime(leftPath, baseTime);
        File.SetLastWriteTime(rightPath, baseTime.AddSeconds(secondsApart));

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        return result.Root.Files[0].Status;
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

    [TestCase(false)]
    [TestCase(true)]
    public void SymbolicLink_SkippedFromComparison(bool isDirectory)
    {
        var target = Path.Combine(_tempDir, "target");
        var link = Path.Combine(_leftDir, "link");

        if (isDirectory)
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            File.WriteAllText(target, "real content");
        }

        if (!TryCreateSymlink(link, target, isDirectory))
        {
            Assert.Ignore("Создание символьных ссылок недоступно (нет прав / режима разработчика).");
        }

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.Files, Is.Empty);
            Assert.That(result.Root.SubDirectories, Is.Empty);
            Assert.That(result.SkippedLinks(), Is.EqualTo(new[] { "link" }), "пропуск ссылки должен доезжать до вызывающего");
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LinkOnOneSide_RealObjectOnOther_IsConflictWithoutActions(bool isDirectory)
    {
        var target = Path.Combine(_tempDir, "target");
        var real = Path.Combine(_rightDir, "data");

        if (isDirectory)
        {
            Directory.CreateDirectory(target);
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, "inner.txt"), "payload");
        }
        else
        {
            File.WriteAllText(target, "link target");
            File.WriteAllText(real, "payload");
        }

        if (!TryCreateSymlink(Path.Combine(_leftDir, "data"), target, isDirectory))
        {
            Assert.Ignore("Создание символьных ссылок недоступно (нет прав / режима разработчика).");
        }

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(SyncMode.LeftToRight, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.SubDirectories, Is.Empty, "спорное имя живёт одной строкой-файлом, а не веткой дерева");
            Assert.That(result.Root.Files, Has.Count.EqualTo(1));
            Assert.That(result.Root.Files[0].TypeConflict, Is.EqualTo(FileTypeConflict.LeftLinkRightObject));
            Assert.That(result.Root.Files[0].Status, Is.EqualTo(ComparisonStatus.Conflict));
            Assert.That(result.Root.Files[0].Action, Is.EqualTo(SyncAction.None), "зеркало не удаляет объект, которого не видели");
            Assert.That(result.CountPlannedActions().Total, Is.EqualTo(0));
        }
    }

    private static bool TryCreateSymlink(string path, string target, bool isDirectory)
    {
        try
        {
            if (isDirectory)
            {
                Directory.CreateSymbolicLink(path, target);
            }
            else
            {
                File.CreateSymbolicLink(path, target);
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Test]
    public void MissingLeftRoot_MarksIncompleteAndBlocksMirrorDeletes()
    {
        File.WriteAllText(Path.Combine(_rightDir, "orphan.txt"), "payload");
        Directory.Delete(_leftDir, true);

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(SyncMode.LeftToRight, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.LeftIncomplete, Is.True);
            Assert.That(result.Root.Files.Single().Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(result.CountPlannedActions().Deletes, Is.EqualTo(0));
            Assert.That(result.IncompleteDirectories(), Is.Not.Empty);
        }
    }

    [Test]
    public void UnreadableLeftDirectory_MarksIncompleteAndBlocksMirrorDeletes()
    {
        var leftLocked = Path.Combine(_leftDir, "locked");
        var rightLocked = Path.Combine(_rightDir, "locked");
        Directory.CreateDirectory(leftLocked);
        Directory.CreateDirectory(rightLocked);
        File.WriteAllText(Path.Combine(leftLocked, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(rightLocked, "a.txt"), "hello");
        File.WriteAllText(Path.Combine(rightLocked, "orphan.txt"), "payload");
        TestAcl.DenyEnumeration(leftLocked);

        try
        {
            if (Readable(leftLocked))
            {
                Assert.Ignore("Deny-ACE не действует на этот процесс (запуск от администратора) – ветку нечем воспроизвести.");
            }

            var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
            var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
            result.ApplyMode(SyncMode.LeftToRight, true);

            var locked = result.Root.SubDirectories.Single(x => x.Name == "locked");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(locked.LeftIncomplete, Is.True);
                Assert.That(locked.Files.Select(x => x.Action), Is.All.EqualTo(SyncAction.Skip));
                Assert.That(result.CountPlannedActions().Deletes, Is.EqualTo(0));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(leftLocked);
        }
    }

    private static bool Readable(string path)
    {
        try
        {
            Directory.EnumerateFileSystemEntries(path).Any();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    [TestCase(SyncMode.LeftToRight, false, SyncWinner.Newest)]
    [TestCase(SyncMode.LeftToRight, true, SyncWinner.Newest)]
    [TestCase(SyncMode.RightToLeft, true, SyncWinner.Newest)]
    [TestCase(SyncMode.Bidirectional, true, SyncWinner.Left)]
    public void NameTakenByFileAndDirectory_IsConflictWithoutActions(SyncMode mode, bool mirror, SyncWinner winner)
    {
        File.WriteAllText(Path.Combine(_leftDir, "config"), "payload");
        var rightConfig = Path.Combine(_rightDir, "config");
        Directory.CreateDirectory(rightConfig);
        File.WriteAllText(Path.Combine(rightConfig, "inner.txt"), "inner");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(mode, mirror, winner);

        var file = result.Root.Files.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.Status, Is.EqualTo(ComparisonStatus.Conflict));
            Assert.That(file.TypeConflict, Is.EqualTo(FileTypeConflict.LeftFileRightDirectory));
            Assert.That(file.Action, Is.EqualTo(SyncAction.None));
            Assert.That(result.Root.SubDirectories, Is.Empty);
            Assert.That(result.CountPlannedActions().Total, Is.EqualTo(0));
            Assert.That(result.HasUnresolvedConflicts(), Is.True);
        }
    }

    [Test]
    public void NameTakenByDirectoryAndFile_NamesTheSideThatHoldsFile()
    {
        Directory.CreateDirectory(Path.Combine(_leftDir, "config"));
        File.WriteAllText(Path.Combine(_rightDir, "config"), "payload");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(SyncMode.RightToLeft, true);

        var file = result.Root.Files.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.TypeConflict, Is.EqualTo(FileTypeConflict.RightFileLeftDirectory));
            Assert.That(file.RightSize, Is.EqualTo(7));
            Assert.That(file.LeftSize, Is.Null);
            Assert.That(file.Action, Is.EqualTo(SyncAction.None));
        }
    }

    [Test]
    public void TypeConflict_MassResolveCopiesSkipsIt()
    {
        File.WriteAllText(Path.Combine(_leftDir, "config"), "payload");
        Directory.CreateDirectory(Path.Combine(_rightDir, "config"));

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None);
        result.ApplyMode(SyncMode.LeftToRight);

        var copied = result.ResolveAllConflicts(SyncAction.CopyToRight);
        var skipped = result.ResolveAllConflicts(SyncAction.Skip);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copied, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(1));
            Assert.That(result.Root.Files.Single().Action, Is.EqualTo(SyncAction.Skip));
        }
    }

    [Test]
    public void CrossCaseNames_OnInsensitiveVolumes_AreOneFile()
    {
        File.WriteAllText(Path.Combine(_leftDir, "foo.txt"), "left");
        File.WriteAllText(Path.Combine(_rightDir, "Foo.txt"), "right side");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None, caseRules: PathCaseRules.Insensitive);

        var file = result.Root.Files.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.Status, Is.EqualTo(ComparisonStatus.Modified));
            Assert.That(file.TypeConflict, Is.EqualTo(FileTypeConflict.None));
            Assert.That(file.LeftSize, Is.EqualTo(4));
            Assert.That(file.RightSize, Is.EqualTo(10));
        }
    }

    [Test]
    public void CrossCaseNames_OnSensitiveVolumes_AreTwoOneSidedFiles()
    {
        File.WriteAllText(Path.Combine(_leftDir, "foo.txt"), "left");
        File.WriteAllText(Path.Combine(_rightDir, "Foo.txt"), "right");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None, caseRules: PathCaseRules.Sensitive);
        result.ApplyMode(SyncMode.LeftToRight, true);

        var left = result.Root.Files.Single(x => x.Name == "foo.txt");
        var right = result.Root.Files.Single(x => x.Name == "Foo.txt");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.Files, Has.Count.EqualTo(2));
            Assert.That(left.Status, Is.EqualTo(ComparisonStatus.LeftOnly));
            Assert.That(right.Status, Is.EqualTo(ComparisonStatus.RightOnly));
            Assert.That(left.Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(right.Action, Is.EqualTo(SyncAction.DeleteRight));
        }
    }

    [Test]
    public void CrossCaseNames_OnMixedVolumes_AreConflictWithoutActions()
    {
        File.WriteAllText(Path.Combine(_leftDir, "foo.txt"), "left");
        File.WriteAllText(Path.Combine(_rightDir, "Foo.txt"), "right");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var mixed = new PathCaseRules(StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None, caseRules: mixed);
        result.ApplyMode(SyncMode.LeftToRight, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.Files, Has.Count.EqualTo(2));
            Assert.That(result.Root.Files.Select(x => x.TypeConflict),
                Is.All.EqualTo(FileTypeConflict.CaseCollision));
            Assert.That(result.Root.Files.Select(x => x.Status), Is.All.EqualTo(ComparisonStatus.Conflict));
            Assert.That(result.Root.Files.Select(x => x.Action), Is.All.EqualTo(SyncAction.None));
        }
    }

    [Test]
    public void CrossCaseDirectories_OnMixedVolumes_AreConflictAndLeaveTreeAlone()
    {
        Directory.CreateDirectory(Path.Combine(_leftDir, "data"));
        Directory.CreateDirectory(Path.Combine(_rightDir, "Data"));
        File.WriteAllText(Path.Combine(_leftDir, "data", "a.txt"), "left");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var mixed = new PathCaseRules(StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None, caseRules: mixed);
        result.ApplyMode(SyncMode.LeftToRight, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Root.SubDirectories, Is.Empty);
            Assert.That(result.Root.Files.Select(x => x.Name), Is.EquivalentTo(new[] { "Data", "data" }));
            Assert.That(result.Root.Files.Select(x => x.TypeConflict),
                Is.All.EqualTo(FileTypeConflict.CaseCollision));
            Assert.That(result.Root.Files.Select(x => x.Action), Is.All.EqualTo(SyncAction.None));
        }
    }

    [Test]
    public void CaseCollision_MassResolveCopiesSkipsIt()
    {
        File.WriteAllText(Path.Combine(_leftDir, "foo.txt"), "left");
        File.WriteAllText(Path.Combine(_rightDir, "Foo.txt"), "right");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var mixed = new PathCaseRules(StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase);
        var result = comparer.Compare(_leftDir, _rightDir, CancellationToken.None, caseRules: mixed);
        result.ApplyMode(SyncMode.LeftToRight);

        var copied = result.ResolveAllConflicts(SyncAction.CopyToRight);
        var skipped = result.ResolveAllConflicts(SyncAction.Skip);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(copied, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(2));
            Assert.That(result.Root.Files.Select(x => x.Action), Is.All.EqualTo(SyncAction.Skip));
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
