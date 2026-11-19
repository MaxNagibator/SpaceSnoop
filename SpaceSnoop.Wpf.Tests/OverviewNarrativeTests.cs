using KeepShell.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Overview;

namespace SpaceSnoop.Wpf.Tests;

public class OverviewNarrativeTests
{
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

    [TestCase(0, 0, StatusSeverity.Warning)]
    [TestCase(1, 0, StatusSeverity.Success)]
    [TestCase(0, 1, StatusSeverity.Error)]
    [TestCase(1, 1, StatusSeverity.Error)]
    public void Итог_отдаёт_приоритет_ошибкам_и_сохраняет_подпись(int ok, int failed, StatusSeverity expected)
    {
        var result = OverviewNarrative.DescribeOutcome("Итог", ok, failed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Severity, Is.EqualTo(expected));
            Assert.That(result.Message, Is.EqualTo("Итог"));
        }
    }

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

    private static OverviewRowViewModel Row(OverviewRunStatus status)
    {
        return new(
            new()
            {
                Left = "l",
                Right = "r",
            },
            static (_, _) =>
            {
            },
            static () =>
            {
            })
        {
            Status = status,
        };
    }
}
