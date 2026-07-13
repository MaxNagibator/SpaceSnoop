using SpaceSnoop.Core.Git;

namespace SpaceSnoop.Tests;

[TestFixture]
public class GitCommitTests
{
    private const string Log =
        "a1b2c3d\t2026-06-29T10:15:00+03:00\tАлиса\tInitial commit\n"
        + "e4f5a6b\t2026-06-28T09:00:00+03:00\tБоб\tFix sync\twith tab\n"
        + "c7d8e9f\tnot-a-date\tЕва\tNo date";

    [Test]
    public void ParseLog_MultipleCommits_ParsesEveryField()
    {
        var commits = GitCommit.ParseLog(Log);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(commits, Has.Count.EqualTo(3));
            Assert.That(commits[0].ShortHash, Is.EqualTo("a1b2c3d"));
            Assert.That(commits[0].Author, Is.EqualTo("Алиса"));
            Assert.That(commits[0].Subject, Is.EqualTo("Initial commit"));
            Assert.That(commits[0].CommittedAt, Is.Not.Null);
            Assert.That(commits[1].Subject, Is.EqualTo("Fix sync\twith tab"));
            Assert.That(commits[2].CommittedAt, Is.Null);
        }
    }

    [TestCase("")]
    [TestCase("\n")]
    [TestCase("\t2026-06-29T10:15:00+03:00\tЕва\tНет хеша")]
    public void ParseLog_EmptyOrHashless_ReturnsEmpty(string input)
    {
        Assert.That(GitCommit.ParseLog(input), Is.Empty);
    }
}
