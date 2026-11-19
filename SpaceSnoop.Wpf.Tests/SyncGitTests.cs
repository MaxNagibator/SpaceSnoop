using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
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
        return SyncGitViewModel.AddGitExclusion(exclusions);
    }

    [Test]
    public void Git_коммиты_перебивают_свежесть_по_файлам()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(SyncPlanNarrative.CombineNewer(NewerSide.Left, false, 1), Is.EqualTo(NewerSide.Right));
            Assert.That(SyncPlanNarrative.CombineNewer(NewerSide.Right, false, -1), Is.EqualTo(NewerSide.Left));
            Assert.That(SyncPlanNarrative.CombineNewer(NewerSide.Left, true, 0), Is.EqualTo(NewerSide.Tie));
            Assert.That(SyncPlanNarrative.CombineNewer(NewerSide.Left, false, 0), Is.EqualTo(NewerSide.Left));
            Assert.That(SyncPlanNarrative.CombineNewer(NewerSide.None, false, 0), Is.EqualTo(NewerSide.None));
        }
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
        var prompt = new GitFolderPromptViewModel(1)
        {
            RememberChoice = true,
        };

        prompt.KeepCommand.Execute(null);

        Assert.That(prompt.Choice, Is.EqualTo(GitFolderPromptChoice.Keep));
    }

    [TestCase(".git", ExpectedResult = ".git")]
    [TestCase(".git/config", ExpectedResult = ".git")]
    [TestCase(@"sub\.git\HEAD", ExpectedResult = ".git")]
    [TestCase(".GIT/objects", ExpectedResult = ".git")]
    [TestCase("src/.gitignore", ExpectedResult = null)]
    [TestCase("src/binaries/main.cs", ExpectedResult = null)]
    [TestCase(@"src\bin\app.dll", ExpectedResult = "bin")]
    [TestCase("proj/obj/x.cache", ExpectedResult = "obj")]
    [TestCase(@"bin\.git\HEAD", ExpectedResult = "bin")]
    [TestCase("src/main.cs", ExpectedResult = null)]
    public string? Ключ_подгруппы_это_первый_совпавший_каталог(string relativePath)
    {
        return SyncRowsProjector.GroupedKey(relativePath, SyncRowsProjector.ParseGroupFolders(".git,bin,obj"));
    }

    [TestCaseSource(nameof(NoNewerCases))]
    public void Направление_пустое_когда_не_определить(DateTimeOffset? left, DateTimeOffset? right)
    {
        Assert.That(SyncGitViewModel.DescribeNewer(left, right), Is.Empty);
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("  ,  ", ExpectedResult = false)]
    public bool Пустой_список_не_группирует(string folders)
    {
        return SyncRowsProjector.IsGroupedPath(".git/config", SyncRowsProjector.ParseGroupFolders(folders));
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

    [Test]
    public void Выбор_числа_коммитов_игнорирует_пустое_значение_и_сохраняет_текущее()
    {
        var git = new SyncGitViewModel(new MemorySettings(), new NoopDialogs(), NullLogger.Instance);
        var changed = new List<string>();
        git.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changed.Add(args.PropertyName);
            }
        };

        git.SelectedGitHistoryCountOption = SyncGitViewModel.GitHistoryCountOptions[1];
        changed.Clear();
        git.SelectedGitHistoryCountOption = null!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(git.GitHistoryCount, Is.EqualTo(8));
            Assert.That(git.SelectedGitHistoryCountOption, Is.EqualTo(SyncGitViewModel.GitHistoryCountOptions[1]));
            Assert.That(changed, Does.Contain(nameof(SyncGitViewModel.SelectedGitHistoryCountOption)));
        }
    }

    private static IEnumerable<TestCaseData> NoNewerCases()
    {
        var stamp = new DateTimeOffset(2026, 6, 29, 12, 0, 0, TimeSpan.Zero);

        yield return new(stamp, stamp);
        yield return new(null, stamp);
        yield return new(stamp, null);
    }
}
