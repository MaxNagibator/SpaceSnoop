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
    public void DirectoryStatistics_CountsSubdirectoriesByStatus_ExcludingRoot()
    {
        var root = new DirectoryComparison("root", "") { Status = ComparisonStatus.Modified };

        root.SubDirectories.Add(new("only-left", "only-left") { Status = ComparisonStatus.LeftOnly });
        root.SubDirectories.Add(new("only-right", "only-right") { Status = ComparisonStatus.RightOnly });

        var modified = new DirectoryComparison("modified", "modified") { Status = ComparisonStatus.Modified };
        modified.SubDirectories.Add(new("nested-id", "modified\\nested-id") { Status = ComparisonStatus.Identical });
        root.SubDirectories.Add(modified);

        var stats = new ComparisonResult("C:\\Left", "C:\\Right", root).GetDirectoryStatistics();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats[ComparisonStatus.LeftOnly], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.RightOnly], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.Modified], Is.EqualTo(1));
            Assert.That(stats[ComparisonStatus.Identical], Is.EqualTo(1));
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
    public void ApplyBidirectionalMode_NewestWins_ConflictOnSameDate_CopiesOneSided()
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
            Assert.That(root.Files[3].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[4].Action, Is.EqualTo(SyncAction.CopyToLeft));
        }
    }

    [TestCase(SyncWinner.Left, SyncAction.CopyToRight)]
    [TestCase(SyncWinner.Right, SyncAction.CopyToLeft)]
    public void ApplyBidirectional_WinnerSide_ResolvesModifiedRegardlessOfDate(SyncWinner winner, SyncAction expected)
    {
        var now = DateTime.Now;
        var earlier = now.AddHours(-1);

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.Modified, LeftModified = earlier, RightModified = now });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.Modified, LeftModified = now, RightModified = earlier });
        root.Files.Add(new("l.txt", "l.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("r.txt", "r.txt") { Status = ComparisonStatus.RightOnly });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.Bidirectional, false, winner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(expected));
            Assert.That(root.Files[1].Action, Is.EqualTo(expected));
            Assert.That(root.Files[0].Status, Is.EqualTo(ComparisonStatus.Modified));
            Assert.That(root.Files[2].Action, Is.EqualTo(SyncAction.CopyToRight));
            Assert.That(root.Files[3].Action, Is.EqualTo(SyncAction.CopyToLeft));
        }
    }

    [TestCase(SyncWinner.Left)]
    [TestCase(SyncWinner.Right)]
    public void ApplyBidirectional_MirrorWinner_DeletesLoserOnlyItems_KeepsWinnerOnly(SyncWinner winner)
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("left.txt", "left.txt") { Status = ComparisonStatus.LeftOnly });
        root.Files.Add(new("right.txt", "right.txt") { Status = ComparisonStatus.RightOnly });

        var leftDir = new DirectoryComparison("ld", "ld") { Status = ComparisonStatus.LeftOnly };
        var rightDir = new DirectoryComparison("rd", "rd") { Status = ComparisonStatus.RightOnly };
        root.SubDirectories.Add(leftDir);
        root.SubDirectories.Add(rightDir);

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.Bidirectional, true, winner);

        var keepLeft = winner == SyncWinner.Left;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(keepLeft ? SyncAction.CopyToRight : SyncAction.DeleteLeft));
            Assert.That(root.Files[1].Action, Is.EqualTo(keepLeft ? SyncAction.DeleteRight : SyncAction.CopyToLeft));
            Assert.That(leftDir.Action, Is.EqualTo(keepLeft ? SyncAction.CopyToRight : SyncAction.DeleteLeft));
            Assert.That(rightDir.Action, Is.EqualTo(keepLeft ? SyncAction.DeleteRight : SyncAction.CopyToLeft));
        }
    }

    [TestCase(SyncWinner.Left, SyncAction.CopyToRight)]
    [TestCase(SyncWinner.Right, SyncAction.CopyToLeft)]
    public void ApplyBidirectional_ReapplyWithWinnerSide_ResolvesFileThatNewestFlaggedConflict(SyncWinner winner, SyncAction expected)
    {
        var now = DateTime.Now;

        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.Modified, LeftModified = now, RightModified = now });

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.Bidirectional, false, SyncWinner.Newest);

        Assume.That(root.Files[0].Status, Is.EqualTo(ComparisonStatus.Conflict));

        result.ApplyMode(SyncMode.Bidirectional, false, winner);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Status, Is.EqualTo(ComparisonStatus.Modified));
            Assert.That(root.Files[0].Action, Is.EqualTo(expected));
        }
    }

    [Test]
    public void CountPlannedActions_SplitsNewAndModifiedAndDeletes()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("a.txt", "a.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.CopyToRight });
        root.Files.Add(new("b.txt", "b.txt") { Status = ComparisonStatus.RightOnly, Action = SyncAction.CopyToLeft });
        root.Files.Add(new("c.txt", "c.txt") { Status = ComparisonStatus.Modified, Action = SyncAction.CopyToRight });
        root.Files.Add(new("d.txt", "d.txt") { Status = ComparisonStatus.LeftOnly, Action = SyncAction.DeleteLeft });
        root.Files.Add(new("e.txt", "e.txt") { Status = ComparisonStatus.Identical, Action = SyncAction.Skip });

        var sub = new DirectoryComparison("sub", "sub");
        sub.Files.Add(new("f.txt", "sub\\f.txt") { Status = ComparisonStatus.Modified, Action = SyncAction.CopyToLeft });
        root.SubDirectories.Add(sub);

        var planned = new ComparisonResult("C:\\Left", "C:\\Right", root).CountPlannedActions();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(planned.NewCopies, Is.EqualTo(2));
            Assert.That(planned.ModifiedCopies, Is.EqualTo(2));
            Assert.That(planned.Deletes, Is.EqualTo(1));
            Assert.That(planned.Copies, Is.EqualTo(4));
            Assert.That(planned.Total, Is.EqualTo(5));
        }
    }

    [TestCase(SyncMode.LeftToRight, ComparisonStatus.RightOnly, SyncAction.DeleteRight)]
    [TestCase(SyncMode.RightToLeft, ComparisonStatus.LeftOnly, SyncAction.DeleteLeft)]
    public void Mirror_DeletesOppositeSideOnlyItems(SyncMode mode, ComparisonStatus orphanStatus, SyncAction expected)
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("orphan.txt", "orphan.txt") { Status = orphanStatus });
        var orphanDir = new DirectoryComparison("orphan", "orphan") { Status = orphanStatus };
        root.SubDirectories.Add(orphanDir);

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(mode, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.Files[0].Action, Is.EqualTo(expected));
            Assert.That(orphanDir.Action, Is.EqualTo(expected));
        }
    }

    [Test]
    public void MirrorCount_CountsDirDeleteOnce_NotNestedFiles()
    {
        var root = new DirectoryComparison("root", "");
        root.Files.Add(new("keep.txt", "keep.txt") { Status = ComparisonStatus.LeftOnly });

        var extra = new DirectoryComparison("extra", "extra") { Status = ComparisonStatus.RightOnly };
        extra.Files.Add(new("nested.txt", "extra\\nested.txt") { Status = ComparisonStatus.RightOnly });
        root.SubDirectories.Add(extra);

        var result = new ComparisonResult("C:\\Left", "C:\\Right", root);
        result.ApplyMode(SyncMode.LeftToRight, true);
        var planned = result.CountPlannedActions();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(planned.NewCopies, Is.EqualTo(1));
            Assert.That(planned.Deletes, Is.Zero);
            Assert.That(planned.DirDeletes, Is.EqualTo(1));
            Assert.That(planned.Total, Is.EqualTo(2));
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
