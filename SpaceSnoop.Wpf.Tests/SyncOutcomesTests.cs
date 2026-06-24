using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncOutcomesTests
{
    [Test]
    public void Применённые_и_упавшие_файлы_размечаются_по_списку_ошибок()
    {
        var ok = File("ok.txt", SyncAction.CopyToRight);
        var bad = File("bad.txt", SyncAction.CopyToRight);
        var skipped = File("keep.txt", SyncAction.Skip);
        var root = Dir("", ok, bad, skipped);

        var map = SyncOutcomes.Build(Result(root), [new("bad.txt", SyncAction.CopyToRight, "нет доступа")], []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map[ok], Is.EqualTo(SyncOutcome.Applied));
            Assert.That(map[bad], Is.EqualTo(SyncOutcome.Failed));
            Assert.That(map.ContainsKey(skipped), Is.False);
        }
    }

    [Test]
    public void Расхождения_размечаются_янтарным_исходом()
    {
        var ok = File("ok.txt", SyncAction.CopyToRight);
        var drift = File("drift.txt", SyncAction.CopyToRight);
        var root = Dir("", ok, drift);

        var map = SyncOutcomes.Build(Result(root), [], [new("drift.txt", SyncAction.CopyToRight, "содержимое расходится")]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map[ok], Is.EqualTo(SyncOutcome.Applied));
            Assert.That(map[drift], Is.EqualTo(SyncOutcome.Mismatch));
            Assert.That(map[root], Is.EqualTo(SyncOutcome.Mismatch));
        }
    }

    [Test]
    public void Ошибка_перекрывает_расхождение_в_исходе_каталога()
    {
        var failed = File("bad.txt", SyncAction.CopyToRight);
        var drift = File("drift.txt", SyncAction.CopyToRight);
        var root = Dir("", failed, drift);

        var map = SyncOutcomes.Build(Result(root), [new("bad.txt", SyncAction.CopyToRight, "сбой")], [new("drift.txt", SyncAction.CopyToRight, "расхождение")]);

        Assert.That(map[root], Is.EqualTo(SyncOutcome.Failed));
    }

    [Test]
    public void Каталог_наследует_худший_исход_поддерева()
    {
        var ok = File("a/ok.txt", SyncAction.CopyToRight);
        var bad = File("a/bad.txt", SyncAction.CopyToRight);
        var onlyOk = File("b/ok.txt", SyncAction.CopyToLeft);
        var failing = Dir("a", ok, bad);
        var passing = Dir("b", onlyOk);
        var root = Dir("");
        root.SubDirectories.Add(failing);
        root.SubDirectories.Add(passing);

        var map = SyncOutcomes.Build(Result(root), [new("a/bad.txt", SyncAction.CopyToRight, "сбой")], []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(map[failing], Is.EqualTo(SyncOutcome.Failed));
            Assert.That(map[passing], Is.EqualTo(SyncOutcome.Applied));
            Assert.That(map[root], Is.EqualTo(SyncOutcome.Failed));
        }
    }

    private static FileComparison File(string relativePath, SyncAction action)
    {
        var name = relativePath.Split('/')[^1];
        return new(name, relativePath) { Status = ComparisonStatus.LeftOnly, Action = action };
    }

    private static DirectoryComparison Dir(string name, params FileComparison[] files)
    {
        var dir = new DirectoryComparison(name, name);
        dir.Files.AddRange(files);
        return dir;
    }

    private static ComparisonResult Result(DirectoryComparison root)
    {
        return new(@"C:\left", @"C:\right", root);
    }
}
