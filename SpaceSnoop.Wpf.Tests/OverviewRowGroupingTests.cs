using SpaceSnoop.Wpf.ViewModels.Overview;

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

    private static OverviewRowViewModel Row()
    {
        return new(new() { Left = "l", Right = "r" }, static (_, _) => { }, static () => { });
    }
}
