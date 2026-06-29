using SpaceSnoop.Core.Git;

namespace SpaceSnoop.Tests;

[TestFixture]
public class GitRepoStateTests
{
    private const string CleanStatus =
        """
        # branch.oid a1b2c3d4e5
        # branch.head master
        # branch.upstream origin/master
        # branch.ab +0 -0
        """;

    private const string DirtyAheadStatus =
        """
        # branch.oid a1b2c3d4e5
        # branch.head feature
        # branch.upstream origin/feature
        # branch.ab +2 -1
        1 .M N... 100644 100644 100644 aaa bbb file1.txt
        ? untracked.txt
        """;

    private const string DetachedNoUpstreamStatus =
        """
        # branch.oid a1b2c3d4e5
        # branch.head (detached)
        """;

    private const string EmptyRepoStatus =
        """
        # branch.oid (initial)
        # branch.head master
        """;

    private const string Log = "a1b2c3d\t2026-06-29T10:15:00+03:00\tInitial commit";

    [Test]
    public void Parse_CleanRepo_NoDirtyInSyncUpstream()
    {
        var state = GitRepoState.Parse(CleanStatus, Log);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Branch, Is.EqualTo("master"));
            Assert.That(state.IsDetached, Is.False);
            Assert.That(state.HasCommits, Is.True);
            Assert.That(state.IsDirty, Is.False);
            Assert.That(state.HasUpstream, Is.True);
            Assert.That(state.Ahead, Is.EqualTo(0));
            Assert.That(state.Behind, Is.EqualTo(0));
            Assert.That(state.ShortHash, Is.EqualTo("a1b2c3d"));
            Assert.That(state.Subject, Is.EqualTo("Initial commit"));
            Assert.That(state.CommittedAt, Is.Not.Null);
        }
    }

    [Test]
    public void Parse_DirtyAhead_CountsChangesAndAheadBehind()
    {
        var state = GitRepoState.Parse(DirtyAheadStatus, Log);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.Branch, Is.EqualTo("feature"));
            Assert.That(state.DirtyCount, Is.EqualTo(2));
            Assert.That(state.IsDirty, Is.True);
            Assert.That(state.Ahead, Is.EqualTo(2));
            Assert.That(state.Behind, Is.EqualTo(1));
        }
    }

    [Test]
    public void Parse_DetachedNoUpstream_FlagsDetachedAndNoUpstream()
    {
        var state = GitRepoState.Parse(DetachedNoUpstreamStatus, Log);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.IsDetached, Is.True);
            Assert.That(state.HasUpstream, Is.False);
            Assert.That(state.Ahead, Is.Null);
            Assert.That(state.Behind, Is.Null);
            Assert.That(state.HasCommits, Is.True);
        }
    }

    [Test]
    public void Parse_EmptyRepo_NoCommits()
    {
        var state = GitRepoState.Parse(EmptyRepoStatus, string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.HasCommits, Is.False);
            Assert.That(state.ShortHash, Is.Empty);
            Assert.That(state.DirtyCount, Is.Zero);
        }
    }

    [Test]
    public void Parse_LogMissing_LeavesCommitFieldsEmpty()
    {
        var state = GitRepoState.Parse(CleanStatus, string.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(state.HasCommits, Is.False);
            Assert.That(state.Subject, Is.Empty);
            Assert.That(state.CommittedAt, Is.Null);
        }
    }
}
