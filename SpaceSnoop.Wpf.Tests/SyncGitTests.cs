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
