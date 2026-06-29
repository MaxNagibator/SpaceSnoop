using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncGitTests
{
    [TestCase("", ExpectedResult = ".git")]
    [TestCase("bin,obj", ExpectedResult = "bin,obj,.git")]
    [TestCase("bin,obj ", ExpectedResult = "bin,obj,.git")]
    [TestCase("bin,.git,obj", ExpectedResult = "bin,.git,obj")]
    [TestCase("bin,.GIT,obj", ExpectedResult = "bin,.GIT,obj")]
    public string Git_исключение_добавляется_один_раз(string exclusions)
    {
        return SyncViewModel.AddGitExclusion(exclusions);
    }

    [TestCase(".git", ExpectedResult = true)]
    [TestCase(".git/config", ExpectedResult = true)]
    [TestCase(@"sub\.git\HEAD", ExpectedResult = true)]
    [TestCase(".GIT/objects", ExpectedResult = true)]
    [TestCase("src/.gitignore", ExpectedResult = false)]
    [TestCase("src/main.cs", ExpectedResult = false)]
    public bool Git_путь_распознаётся_по_сегменту(string relativePath)
    {
        return SyncViewModel.IsGitPath(relativePath);
    }

    [Test]
    public void Слева_новее_когда_левый_коммит_позже()
    {
        var left = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        var right = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.That(SyncViewModel.DescribeNewer(left, right), Is.EqualTo("слева новее на 3 дн."));
    }

    [Test]
    public void Справа_новее_когда_правый_коммит_позже()
    {
        var left = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var right = new DateTimeOffset(2026, 6, 29, 12, 30, 0, TimeSpan.Zero);

        Assert.That(SyncViewModel.DescribeNewer(left, right), Is.EqualTo("справа новее на 2 ч."));
    }

    [TestCaseSource(nameof(NoNewerCases))]
    public void Направление_пустое_когда_не_определить(DateTimeOffset? left, DateTimeOffset? right)
    {
        Assert.That(SyncViewModel.DescribeNewer(left, right), Is.Empty);
    }

    private static IEnumerable<TestCaseData> NoNewerCases()
    {
        var stamp = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        yield return new(stamp, stamp);
        yield return new(null, stamp);
        yield return new(stamp, null);
    }

    [Test]
    public void Выбор_без_чекбокса_не_запоминается()
    {
        var prompt = new GitFolderPromptViewModel(1);

        prompt.SkipCommand.Execute(null);

        Assert.That(prompt.Choice, Is.EqualTo(GitFolderPromptChoice.Ask));
    }

    [Test]
    public void Выбор_с_чекбоксом_запоминается()
    {
        var prompt = new GitFolderPromptViewModel(1) { RememberChoice = true };

        prompt.KeepCommand.Execute(null);

        Assert.That(prompt.Choice, Is.EqualTo(GitFolderPromptChoice.Keep));
    }
}
