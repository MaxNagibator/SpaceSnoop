using KeepShell.Bootstrap;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncSubtreeActionTests
{
    [Test]
    public void Встречное_копирование_снимает_удаление_каталога()
    {
        var dir = new DirectoryComparison("orphan", "orphan")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.DeleteLeft,
        };

        dir.Files.Add(new("a.txt", "orphan\\a.txt") { Status = ComparisonStatus.LeftOnly });

        Rows().ApplyToSubtree(dir, SyncAction.CopyToLeft);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dir.Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(dir.Files[0].Action, Is.EqualTo(SyncAction.CopyToLeft));
        }
    }

    [Test]
    public void Копирование_поддерева_не_назначается_конфликту_вида()
    {
        var dir = Conflict(out var file);

        Rows().ApplyToSubtree(dir, SyncAction.CopyToRight);

        Assert.That(file.Action, Is.EqualTo(SyncAction.Skip));
    }

    [Test]
    public void Пропуск_поддерева_конфликту_вида_назначается()
    {
        var dir = Conflict(out var file);
        file.Action = SyncAction.None;

        Rows().ApplyToSubtree(dir, SyncAction.Skip);

        Assert.That(file.Action, Is.EqualTo(SyncAction.Skip));
    }

    [Test]
    public void Цикл_действий_конфликта_вида_предлагает_только_пропуск()
    {
        var cycle = SyncActionCycles.ForFile(ComparisonStatus.Modified, deleteAllowed: true, typeConflict: true);

        Assert.That(cycle, Is.EqualTo(new[] { SyncAction.Skip }));
    }

    private static DirectoryComparison Conflict(out FileComparison file)
    {
        file = new("config", "config")
        {
            Status = ComparisonStatus.Modified,
            Action = SyncAction.Skip,
            TypeConflict = FileTypeConflict.LeftFileRightDirectory,
        };

        var dir = new DirectoryComparison("root", "");
        dir.Files.Add(file);
        return dir;
    }

    private static SyncRowsViewModel Rows()
    {
        ISettingsStore settings = new MemorySettings();
        return new(settings, new OperationPreferences(settings), new AgentPreferences(settings), _ => Task.CompletedTask, _ => { });
    }
}
