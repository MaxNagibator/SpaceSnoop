using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;

namespace SpaceSnoop.Wpf.Tests;

public class SyncProfileWinnerTests
{
    [TestCase(0, SyncWinner.Newest)]
    [TestCase(1, SyncWinner.Left)]
    [TestCase(2, SyncWinner.Right)]
    [TestCase(99, SyncWinner.Newest)]
    public void WinnerFromIndex_Мапит_индекс_в_победителя(int index, SyncWinner expected)
    {
        Assert.That(SyncProfile.WinnerFromIndex(index), Is.EqualTo(expected));
    }

    [TestCase(SyncWinner.Newest, 0)]
    [TestCase(SyncWinner.Left, 1)]
    [TestCase(SyncWinner.Right, 2)]
    [TestCase(SyncWinner.None, 0)]
    public void IndexOfWinner_Мапит_победителя_в_индекс(SyncWinner winner, int expected)
    {
        Assert.That(SyncProfile.IndexOfWinner(winner), Is.EqualTo(expected));
    }

    [TestCase(SyncMode.LeftToRight, SyncWinner.Newest, "L")]
    [TestCase(SyncMode.RightToLeft, SyncWinner.Newest, "R")]
    [TestCase(SyncMode.Bidirectional, SyncWinner.Left, "L")]
    [TestCase(SyncMode.Bidirectional, SyncWinner.Right, "R")]
    [TestCase(SyncMode.Bidirectional, SyncWinner.Newest, null)]
    public void MirrorSource_Даёт_авторитетную_сторону_или_null(SyncMode mode, SyncWinner winner, string? expected)
    {
        var source = SyncProfile.MirrorSource(mode, winner, "L", "R");

        Assert.That(source, Is.EqualTo(expected));
    }
}
