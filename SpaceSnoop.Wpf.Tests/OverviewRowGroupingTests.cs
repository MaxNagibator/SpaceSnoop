using MahApps.Metro.IconPacks;
using SpaceSnoop.Wpf.ViewModels.Overview;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

public class OverviewRowGroupingTests
{
    [Test]
    public void GroupKey_и_GroupOrder_согласованы_с_IsUnchanged()
    {
        var unchanged = Row();
        unchanged.Status = OverviewRunStatus.Compared;

        var changed = Row();
        changed.LeftOnlyCount = 3;
        changed.Status = OverviewRunStatus.Compared;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unchanged.GroupOrder, Is.EqualTo(1));
            Assert.That(unchanged.GroupKey, Is.EqualTo("Без изменений"));
            Assert.That(changed.GroupOrder, Is.Zero);
            Assert.That(changed.GroupKey, Is.EqualTo("Профили"));
        }
    }

    [TestCase(OverviewRunStatus.Compared, 0, 0, 0, 0, ExpectedResult = true)]
    [TestCase(OverviewRunStatus.Compared, 1, 0, 0, 0, ExpectedResult = false)]
    [TestCase(OverviewRunStatus.Synced, 0, 0, 0, 0, ExpectedResult = true)]
    [TestCase(OverviewRunStatus.Synced, 0, 1, 0, 0, ExpectedResult = false)]
    [TestCase(OverviewRunStatus.Synced, 0, 0, 1, 0, ExpectedResult = false)]
    [TestCase(OverviewRunStatus.Synced, 0, 0, 0, 1, ExpectedResult = false)]
    [TestCase(OverviewRunStatus.None, 0, 0, 0, 0, ExpectedResult = false)]
    [TestCase(OverviewRunStatus.Error, 0, 0, 0, 0, ExpectedResult = false)]
    public bool IsUnchanged_отражает_статус_и_счётчики(OverviewRunStatus status, int diff, int copied, int deleted, int errors)
    {
        var row = Row();
        row.LeftOnlyCount = diff;
        row.SyncCopied = copied;
        row.SyncDeleted = deleted;
        row.SyncErrors = errors;
        row.Status = status;

        return row.IsUnchanged;
    }

    [TestCase(SyncVerifyState.Completed, 0, true)]
    [TestCase(SyncVerifyState.Completed, 2, false)]
    [TestCase(SyncVerifyState.Interrupted, 0, false)]
    public void Расхождения_и_прерванная_проверка_выводят_строку_из_группы_без_изменений(SyncVerifyState verify, int mismatches, bool unchanged)
    {
        var row = Row();
        row.SyncMismatches = mismatches;
        row.SyncVerify = verify;
        row.Status = OverviewRunStatus.Synced;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.IsUnchanged, Is.EqualTo(unchanged));
            Assert.That(row.SyncHadErrors, Is.EqualTo(!unchanged));
            Assert.That(row.StatusIconKind, Is.EqualTo(unchanged ? PackIconLucideKind.FolderCheck : PackIconLucideKind.TriangleAlert));
        }
    }

    [Test]
    public void Итог_строки_называет_расхождения_и_прерванную_проверку()
    {
        var mismatched = Row();
        mismatched.SyncCopied = 4;
        mismatched.SyncMismatches = 2;
        mismatched.SyncVerify = SyncVerifyState.Completed;
        mismatched.Status = OverviewRunStatus.Synced;

        var interrupted = Row();
        interrupted.SyncVerify = SyncVerifyState.Interrupted;
        interrupted.Status = OverviewRunStatus.Synced;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mismatched.StatusText, Is.EqualTo("Синхронизировано: скопировано 4 · расхождений 2"));
            Assert.That(interrupted.StatusText, Is.EqualTo("Синхронизировано: проверка прервана"));
        }
    }

    private static OverviewRowViewModel Row()
    {
        return new(new() { Left = "l", Right = "r" }, static (_, _) => { }, static () => { });
    }
}
