using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ComparisonResultTests
{
    [Test]
    public void Statistics_CountsStatusesCorrectly()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.Modified });
        root.Files.Add(new("c.txt", "c.txt") { Status = ComparisonStatus.Identical });

        var sub = new DirectoryComparison("sub", "sub");
        sub.Files.Add(new("d.txt", "sub\\d.txt") { Status = ComparisonStatus.RightOnly });
        root.SubDirectories.Add(sub);

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        var stats = result.GetStatistics();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats[ComparisonStatus.Identical], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.LeftOnly], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.RightOnly], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.Modified], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.Conflict], Is.Zero);
        }
    }

    [Test]
    public void ApplyLeftToRightMode_SetsCorrectActions()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.RightOnly });
        root.Files.Add(new("c.txt", "c.txt") { Status = ComparisonStatus.Modified });
        root.Files.Add(new("d.txt", "d.txt") { Status = ComparisonStatus.Identical });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.LeftToRight);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[1].Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(root.Files[2].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[3].Action, Is.EqualTo(SyncAction.Skip));
        }
    }

    [Test]
    public void ApplyRightToLeftMode_SetsCorrectActions()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.RightOnly });
        root.Files.Add(new("c.txt", "c.txt") { Status = ComparisonStatus.Modified });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.RightToLeft);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(root.Files[1].Action, Is.EqualTo(SyncAction.CopyToLeft));
            Assert.That(root.Files[2].Action, Is.EqualTo(SyncAction.CopyToLeft));
        }
    }

    [Test]
    public void ApplyBidirectionalMode_NewestWins_ConflictOnSameDate()
    {
        var now = DateTime.Now;
        var earlier = now.AddHours(-1);

        var root = new DirectoryComparison("root", "");

        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Modified,
            LeftModified = now,
            RightModified = earlier,
        });

        root.Files.Add(new("b.txt", "b.txt")
        {
            Status = ComparisonStatus.Modified,
            LeftModified = earlier,
            RightModified = now,
        });

        root.Files.Add(new("c.txt", "c.txt")
        {
            Status = ComparisonStatus.Modified,
            LeftModified = now,
            RightModified = now,
        });

        root.Files.Add(new("d.txt", "d.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("e.txt", "e.txt") { Status = ComparisonStatus.RightOnly });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.Bidirectional);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[1].Action, Is.EqualTo(SyncAction.CopyToLeft));
            Assert.That(root.Files[2].Action, Is.EqualTo(SyncAction.None));
            Assert.That(root.Files[2].Status, Is.EqualTo(ComparisonStatus.Conflict));
            Assert.That(root.Files[3].Action, Is.EqualTo(SyncAction.None));
            Assert.That(root.Files[4].Action, Is.EqualTo(SyncAction.None));
        }
    }

    [Test]
    public void HasUnresolvedConflicts_ReturnsTrueWhenConflictsWithNoAction()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt")
        {
            Status = ComparisonStatus.Conflict,
            Action = SyncAction.None,
        });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        Assert.That(result.HasUnresolvedConflicts(), Is.True);

        root.Files[0].Action = SyncAction.CopyToRight;
        Assert.That(result.HasUnresolvedConflicts(), Is.False);
    }

    [Test]
    public void ResolveAllConflicts_SetsActionAndClearsUnresolvedFlag()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.Conflict, Action = SyncAction.None });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.None });
        root.Files.Add(new("c.txt", "c.txt") { Status = ComparisonStatus.RightOnly, Action = SyncAction.None });
        root.Files.Add(new("d.txt", "d.txt") { Status = ComparisonStatus.Identical, Action = SyncAction.Skip });
        root.Files.Add(new("e.txt", "e.txt") { Status = ComparisonStatus.Modified, Action = SyncAction.CopyToRight });

        var sub = new DirectoryComparison("sub", "sub");
        sub.Files.Add(new("f.txt", "sub\\f.txt") { Status = ComparisonStatus.Conflict, Action = SyncAction.None });
        root.SubDirectories.Add(sub);

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        var count = result.ResolveAllConflicts(SyncAction.CopyToRight);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(4));
            Assert.That(root.Files[0].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[1].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[2].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[3].Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(root.Files[4].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(sub.Files[0].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(result.HasUnresolvedConflicts(), Is.False);
        }
    }
}
