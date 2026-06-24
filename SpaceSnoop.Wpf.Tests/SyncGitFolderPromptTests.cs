using SpaceSnoop.Wpf.ViewModels;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncGitFolderPromptTests
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
