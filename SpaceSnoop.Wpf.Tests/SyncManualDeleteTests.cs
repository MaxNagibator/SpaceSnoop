using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncManualDeleteTests
{
    [Test]
    public void Запрещённое_удаление_файла_не_назначается_из_меню()
    {
        var file = new FileComparison("orphan.txt", "blind/orphan.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.Skip,
            DeleteLeftBlocked = true,
        };

        var row = Row(file, out _);
        row.DeleteCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.Action, Is.EqualTo(SyncAction.Skip));
            Assert.That(row.DeleteAllowed, Is.False);
            Assert.That(row.DeleteHint, Is.Not.Empty);
        }
    }

    [Test]
    public void Разрешённое_удаление_файла_назначается_как_прежде()
    {
        var file = new FileComparison("orphan.txt", "orphan.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.Skip,
        };

        var row = Row(file, out _);
        row.DeleteCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.Action, Is.EqualTo(SyncAction.DeleteLeft));
            Assert.That(row.DeleteAllowed, Is.True);
        }
    }

    [Test]
    public void Цикл_действий_пропускает_запрещённое_удаление()
    {
        var file = new FileComparison("orphan.txt", "orphan.txt")
        {
            Status = ComparisonStatus.LeftOnly,
            Action = SyncAction.Skip,
            DeleteLeftBlocked = true,
        };

        var row = Row(file, out _);
        row.CycleActionCommand.Execute(null);

        Assert.That(file.Action, Is.EqualTo(SyncAction.CopyToRight));
    }

    [Test]
    public void Запрещённое_удаление_каталога_не_доходит_до_поддерева()
    {
        var dir = new DirectoryComparison("blind", "blind")
        {
            Status = ComparisonStatus.RightOnly,
            DeleteRightBlocked = true,
        };

        var row = Row(dir, out var host);
        row.DirDeleteCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(host.Applied, Is.Empty);
            Assert.That(row.DeleteAllowed, Is.False);
        }
    }

    [Test]
    public void Конфликт_вида_не_принимает_копирование_но_принимает_пропуск()
    {
        var file = new FileComparison("config", "config")
        {
            Status = ComparisonStatus.Modified,
            Action = SyncAction.None,
            TypeConflict = FileTypeConflict.RightFileLeftDirectory,
        };

        var row = Row(file, out _);
        row.CopyToRightCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(file.Action, Is.EqualTo(SyncAction.None));
            Assert.That(row.CopyAllowed, Is.False);
            Assert.That(row.CopyHint, Is.Not.Empty);
        }

        row.SkipCommand.Execute(null);

        Assert.That(file.Action, Is.EqualTo(SyncAction.Skip));
    }

    private static SyncNodeViewModel Row(FileComparison file, out RowHost host)
    {
        host = new();
        return new(file, 0, host);
    }

    private static SyncNodeViewModel Row(DirectoryComparison dir, out RowHost host)
    {
        host = new();
        return new(dir, 0, true, 0, 0, host);
    }

    private sealed class RowHost : ISyncRowHost
    {
        public List<SyncAction> Applied { get; } = [];

        public bool BlankAbsent => false;

        public bool ChatEnabled => false;

        public void ToggleExpand(DirectoryComparison dir)
        {
        }

        public void ToggleGroup(string? key)
        {
        }

        public void ExpandSubtree(DirectoryComparison dir)
        {
        }

        public void CollapseSubtree(DirectoryComparison dir)
        {
        }

        public void ApplyToSubtree(DirectoryComparison dir, SyncAction action)
        {
            Applied.Add(action);
        }

        public void NotifyActionsChanged()
        {
        }

        public Task CompareContentAsync(FileComparison file)
        {
            return Task.CompletedTask;
        }

        public void AskAgentAbout(SyncNodeViewModel node)
        {
        }
    }
}
