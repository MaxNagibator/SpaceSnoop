using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class CleanupTotalsTests
{
    [Test]
    public void Недоступная_цель_не_попадает_ни_в_итог_ни_в_выбранное()
    {
        var available = Row("TempFiles", 300, 3, CleanupAvailability.Available, true);
        var unsupported = Row("WindowsOld", 700, 7, CleanupAvailability.Unsupported, true);

        var tally = CleanupTotals.Compute([available, unsupported]);

        Assert.Multiple(() =>
        {
            Assert.That(tally.TotalBytes, Is.EqualTo(300));
            Assert.That(tally.SelectedBytes, Is.EqualTo(300));
            Assert.That(tally.SelectedFiles, Is.EqualTo(3));
            Assert.That(tally.SelectedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Невыбранная_цель_считается_в_итоге_но_не_в_выбранном()
    {
        var selected = Row("TempFiles", 100, 1, CleanupAvailability.Available, true);
        var idle = Row("Prefetch", 400, 4, CleanupAvailability.Available, false);

        var tally = CleanupTotals.Compute([selected, idle]);

        Assert.Multiple(() =>
        {
            Assert.That(tally.TotalBytes, Is.EqualTo(500));
            Assert.That(tally.SelectedBytes, Is.EqualTo(100));
            Assert.That(tally.SelectedCount, Is.EqualTo(1));
        });
    }

    [Test]
    public void Доля_считается_от_доступных_а_недоступной_не_ставится()
    {
        var available = Row("TempFiles", 300, 3, CleanupAvailability.Available, false);
        var unsupported = Row("WindowsOld", 700, 7, CleanupAvailability.Unsupported, false);
        unsupported.Share = 0.7;
        List<CleanupTargetViewModel> rows = [available, unsupported];

        CleanupTotals.ApplyShares(rows, CleanupTotals.Compute(rows).TotalBytes);

        Assert.Multiple(() =>
        {
            Assert.That(available.Share, Is.EqualTo(1).Within(0.0001));
            Assert.That(unsupported.Share, Is.Zero);
        });
    }

    [Test]
    public void Пустой_итог_не_даёт_деления_на_ноль()
    {
        var row = Row("TempFiles", 0, 0, CleanupAvailability.Available, true);
        List<CleanupTargetViewModel> rows = [row];

        CleanupTotals.ApplyShares(rows, CleanupTotals.Compute(rows).TotalBytes);

        Assert.That(row.Share, Is.Zero);
    }

    private static CleanupTargetViewModel Row(string id, long bytes, int files, CleanupAvailability availability, bool selected)
    {
        CleanupTarget target = new()
        {
            Id = id,
            Name = id,
            Description = id,
            Kind = CleanupTargetKind.Directory,
            Path = @"C:\" + id,
            Supported = availability != CleanupAvailability.Unsupported,
        };

        return new(target)
        {
            SizeBytes = bytes,
            Files = files,
            Availability = availability,
            IsSelected = selected,
        };
    }
}
