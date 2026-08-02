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
        return SyncOperationsViewModel.AddGitExclusion(exclusions);
    }

    [TestCase(".git", ExpectedResult = true)]
    [TestCase(".git/config", ExpectedResult = true)]
    [TestCase(@"sub\.git\HEAD", ExpectedResult = true)]
    [TestCase(".GIT/objects", ExpectedResult = true)]
    [TestCase("src/bin/app.dll", ExpectedResult = true)]
    [TestCase(@"proj\obj\Debug\x.cache", ExpectedResult = true)]
    [TestCase("src/.gitignore", ExpectedResult = false)]
    [TestCase("src/binaries/main.cs", ExpectedResult = false)]
    [TestCase("src/main.cs", ExpectedResult = false)]
    public bool Группируемый_путь_распознаётся_по_сегменту(string relativePath)
    {
        return SyncRowsProjector.IsGroupedPath(relativePath, SyncRowsProjector.ParseGroupFolders(".git,bin,obj"));
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("  ,  ", ExpectedResult = false)]
    public bool Пустой_список_не_группирует(string folders)
    {
        return SyncRowsProjector.IsGroupedPath(".git/config", SyncRowsProjector.ParseGroupFolders(folders));
    }

    [TestCase(".git/config", ExpectedResult = ".git")]
    [TestCase(@"src\bin\app.dll", ExpectedResult = "bin")]
    [TestCase("proj/obj/x.cache", ExpectedResult = "obj")]
    [TestCase(@"bin\.git\HEAD", ExpectedResult = "bin")]
    [TestCase("src/main.cs", ExpectedResult = null)]
    public string? Ключ_подгруппы_это_первый_совпавший_каталог(string relativePath)
    {
        return SyncRowsProjector.GroupedKey(relativePath, SyncRowsProjector.ParseGroupFolders(".git,bin,obj"));
    }

    [Test]
    public void Слева_новее_когда_левый_коммит_позже()
    {
        var left = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);
        var right = new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero);

        Assert.That(SyncGitViewModel.DescribeNewer(left, right), Is.EqualTo("слева новее на 3 дн."));
    }

    [Test]
    public void Справа_новее_когда_правый_коммит_позже()
    {
        var left = new DateTimeOffset(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        var right = new DateTimeOffset(2026, 6, 29, 12, 30, 0, TimeSpan.Zero);

        Assert.That(SyncGitViewModel.DescribeNewer(left, right), Is.EqualTo("справа новее на 2 ч."));
    }

    [TestCaseSource(nameof(NoNewerCases))]
    public void Направление_пустое_когда_не_определить(DateTimeOffset? left, DateTimeOffset? right)
    {
        Assert.That(SyncGitViewModel.DescribeNewer(left, right), Is.Empty);
    }

    [Test]
    public void Git_коммиты_перебивают_свежесть_по_файлам()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncLedgerViewModel.CombineNewer(NewerSide.Left, false, 1), Is.EqualTo(NewerSide.Right));
            Assert.That(SyncLedgerViewModel.CombineNewer(NewerSide.Right, false, -1), Is.EqualTo(NewerSide.Left));
            Assert.That(SyncLedgerViewModel.CombineNewer(NewerSide.Left, true, 0), Is.EqualTo(NewerSide.Tie));
            Assert.That(SyncLedgerViewModel.CombineNewer(NewerSide.Left, false, 0), Is.EqualTo(NewerSide.Left));
            Assert.That(SyncLedgerViewModel.CombineNewer(NewerSide.None, false, 0), Is.EqualTo(NewerSide.None));
        }
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
