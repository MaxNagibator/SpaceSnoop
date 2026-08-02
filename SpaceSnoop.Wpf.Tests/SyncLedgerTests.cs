using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncLedgerTests
{
    [Test]
    public void Применённые_копии_становятся_одинаковыми_а_удаления_исчезают_из_леджера()
    {
        var copied = File("new.txt", ComparisonStatus.LeftOnly, SyncAction.CopyToRight);
        var deleted = File("del.txt", ComparisonStatus.RightOnly, SyncAction.DeleteRight);
        var drift = File("drift.txt", ComparisonStatus.Modified, SyncAction.CopyToRight);
        var kept = File("keep.txt", ComparisonStatus.Identical, SyncAction.Skip);

        var copiedDir = Dir("copy", ComparisonStatus.LeftOnly, SyncAction.CopyToRight, File("copy/y.txt", ComparisonStatus.LeftOnly, SyncAction.CopyToRight));
        var goneDir = Dir("gone", ComparisonStatus.RightOnly, SyncAction.DeleteRight, File("gone/x.txt", ComparisonStatus.RightOnly, SyncAction.DeleteRight));

        var root = Dir("", ComparisonStatus.Identical, SyncAction.None, copied, deleted, drift, kept);
        root.SubDirectories.Add(copiedDir);
        root.SubDirectories.Add(goneDir);

        var result = new ComparisonResult(@"C:\left", @"C:\right", root);
        var outcomes = SyncOutcomes.Build(result, [], [new("drift.txt", SyncAction.CopyToRight, "содержимое расходится")]);

        var (files, dirs) = SyncLedgerViewModel.CountRemaining(root, outcomes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(files[ComparisonStatus.Identical], Is.EqualTo(3), "new.txt + keep.txt + copy/y.txt");
            Assert.That(files[ComparisonStatus.Modified], Is.EqualTo(1), "drift.txt – расхождение, остаётся изменённым");
            Assert.That(files[ComparisonStatus.LeftOnly], Is.EqualTo(0));
            Assert.That(files[ComparisonStatus.RightOnly], Is.EqualTo(0), "del.txt и gone/x.txt удалены – вне счёта");
            Assert.That(dirs[ComparisonStatus.Identical], Is.EqualTo(1), "скопированный каталог сошёлся");
            Assert.That(dirs[ComparisonStatus.RightOnly], Is.EqualTo(0), "удалённый каталог исчез вместе с поддеревом");
        }
    }

    private static FileComparison File(string relativePath, ComparisonStatus status, SyncAction action)
    {
        var name = relativePath.Split('/')[^1];
        return new(name, relativePath) { Status = status, Action = action };
    }

    private static DirectoryComparison Dir(string name, ComparisonStatus status, SyncAction action, params FileComparison[] files)
    {
        var dir = new DirectoryComparison(name, name) { Status = status, Action = action };
        dir.Files.AddRange(files);
        return dir;
    }
}
