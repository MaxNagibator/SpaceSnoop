using SpaceSnoop.Wpf.ViewModels.Overview;

namespace SpaceSnoop.Wpf.Tests;

public class OverviewNarrativeTests
{
    [Test]
    public void Пропущенным_считается_всё_что_не_сравнилось_и_не_упало()
    {
        var compared = Row(OverviewRunStatus.Compared);
        var failed = Row(OverviewRunStatus.Error);
        var unavailable = Row(OverviewRunStatus.Unavailable);
        var skipped = Row(OverviewRunStatus.Skipped);

        var tally = OverviewNarrative.TallyCompare([compared, failed, unavailable, skipped], 6);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tally.Compared, Is.EqualTo(1));
            Assert.That(tally.Failed, Is.EqualTo(1));
            Assert.That(tally.Skipped, Is.EqualTo(4));
        }
    }

    [Test]
    public void Исключённые_из_пакета_профили_попадают_в_пропущенные_синхронизации()
    {
        var synced = Row(OverviewRunStatus.Synced);
        var failed = Row(OverviewRunStatus.Error);
        var skipped = Row(OverviewRunStatus.Skipped);

        var tally = OverviewNarrative.TallySync([synced, failed, skipped], 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tally.Synced, Is.EqualTo(1));
            Assert.That(tally.Failed, Is.EqualTo(1));
            Assert.That(tally.Skipped, Is.EqualTo(3));
        }
    }

    private static OverviewRowViewModel Row(OverviewRunStatus status)
    {
        return new(new() { Left = "l", Right = "r" }, static (_, _) => { }, static () => { }) { Status = status };
    }
}
