using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncFreshnessTests
{
    private static readonly DateTime Old = new(2024, 1, 1, 10, 0, 0);
    private static readonly DateTime Mid = new(2025, 1, 1, 10, 0, 0);
    private static readonly DateTime New = new(2026, 6, 1, 10, 0, 0);

    [Test]
    public void Вердикт_по_свежайшему_файлу_а_не_по_числу_голосов()
    {
        var leftA = Modified("a.txt", Mid, Old);
        var leftB = Modified("b.txt", Mid, Old);
        var rightBig = Modified("c.txt", Old, New);
        var root = Dir("", leftA, leftB, rightBig);

        var summary = SyncFreshness.Compute(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.LeftNewer, Is.EqualTo(2));
            Assert.That(summary.RightNewer, Is.EqualTo(1));
            Assert.That(summary.LeftChangedMax, Is.EqualTo(Mid));
            Assert.That(summary.RightChangedMax, Is.EqualTo(New));
            Assert.That(summary.Verdict, Is.EqualTo(NewerSide.Right));
        }
    }

    [Test]
    public void Разница_в_пределах_FAT_допуска_даёт_ничью()
    {
        var withinTolerance = Modified("a.txt", New, New.AddSeconds(1));
        var root = Dir("", withinTolerance);

        var summary = SyncFreshness.Compute(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.LeftNewer, Is.Zero);
            Assert.That(summary.RightNewer, Is.Zero);
            Assert.That(summary.Verdict, Is.EqualTo(NewerSide.Tie));
        }
    }

    [Test]
    public void Симметричные_изменения_дают_ничью()
    {
        var root = Dir("", Modified("a.txt", New, Old), Modified("b.txt", Old, New));

        Assert.That(SyncFreshness.Compute(root).Verdict, Is.EqualTo(NewerSide.Tie));
    }

    [Test]
    public void Сторона_с_единственными_изменениями_свежее()
    {
        var leftOnly = new FileComparison("l.txt", "l.txt") { Status = ComparisonStatus.LeftOnly, LeftModified = Old };
        var root = Dir("", leftOnly);

        var summary = SyncFreshness.Compute(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.RightChangedMax, Is.Null);
            Assert.That(summary.Verdict, Is.EqualTo(NewerSide.Left));
        }
    }

    [Test]
    public void Сводка_считает_максимум_и_уникальные_рекурсивно()
    {
        var leftOnly = new FileComparison("l.txt", "l.txt") { Status = ComparisonStatus.LeftOnly, LeftModified = Old };
        var rightOnly = new FileComparison("r.txt", "sub/r.txt") { Status = ComparisonStatus.RightOnly, RightModified = New };
        var root = Dir("", leftOnly);
        root.SubDirectories.Add(Dir("sub", rightOnly));

        var summary = SyncFreshness.Compute(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary.LeftOnly, Is.EqualTo(1));
            Assert.That(summary.RightOnly, Is.EqualTo(1));
            Assert.That(summary.LeftMax, Is.EqualTo(Old));
            Assert.That(summary.RightMax, Is.EqualTo(New));
            Assert.That(summary.LeftChangedMax, Is.EqualTo(Old));
            Assert.That(summary.RightChangedMax, Is.EqualTo(New));
        }
    }

    [Test]
    public void Свежесть_удаляемого_берёт_сторону_по_действию_и_не_входит_в_удаляемый_каталог()
    {
        var deletedRight = new FileComparison("x.txt", "x.txt")
        {
            Action = SyncAction.DeleteRight,
            RightModified = New,
        };

        var kept = new FileComparison("y.txt", "y.txt") { Action = SyncAction.CopyToRight, RightModified = New.AddYears(1) };
        var insideDeletedDir = new FileComparison("z.txt", "gone/z.txt")
        {
            Action = SyncAction.DeleteRight,
            RightModified = New.AddYears(5),
        };

        var root = Dir("", deletedRight, kept);
        var goneDir = Dir("gone", insideDeletedDir);
        goneDir.Action = SyncAction.DeleteRight;
        goneDir.RightModified = Old;
        root.SubDirectories.Add(goneDir);

        var (count, newest) = SyncFreshness.DeletionRecency(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(count, Is.EqualTo(2));
            Assert.That(newest, Is.EqualTo(New));
        }
    }

    [Test]
    public void Перевес_свежести_измеряется_в_секундах_и_бесконечен_для_односторонних()
    {
        var both = SyncFreshness.Compute(Dir("", Modified("a.txt", New, New.AddMinutes(-5))));
        var oneSided = SyncFreshness.Compute(Dir("", new FileComparison("l.txt", "l.txt") { Status = ComparisonStatus.LeftOnly, LeftModified = Old }));
        var nothing = SyncFreshness.Compute(Dir("", Modified("a.txt", New, New)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(both.LeadSeconds, Is.EqualTo(300));
            Assert.That(oneSided.LeadSeconds, Is.EqualTo(double.MaxValue));
            Assert.That(nothing.LeadSeconds, Is.Zero);
        }
    }

    private static FileComparison Modified(string path, DateTime left, DateTime right)
    {
        return new(path, path)
        {
            Status = ComparisonStatus.Modified,
            LeftModified = left,
            RightModified = right,
        };
    }

    private static DirectoryComparison Dir(string name, params FileComparison[] files)
    {
        var dir = new DirectoryComparison(name, name);
        dir.Files.AddRange(files);
        return dir;
    }
}
