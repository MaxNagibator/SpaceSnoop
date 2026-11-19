using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftReaderPendingTests
{
    private const int Record = 30;

    [Test]
    public void Байты_безымянной_записи_попадают_в_счётчик()
    {
        var entries = Entries(name: null);
        entries[Record].Size = 8192;

        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, new(), statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Nameless, Is.EqualTo(1));
            Assert.That(statistics.NamelessBytes, Is.EqualTo(8192));
            Assert.That(entries[Record].Exists, Is.False);
        }
    }

    [Test]
    public void Известный_размер_записью_расширения_не_перебивается()
    {
        var entries = Entries(name: "данные.bin");
        entries[Record].Size = 100;
        entries[Record].SizeKnown = true;
        var pending = new MftPending();
        pending.Sizes[Record] = new(999);

        MftReader.ApplyPending(entries, pending, new());

        Assert.That(entries[Record].Size, Is.EqualTo(100));
    }

    [Test]
    public void Имя_из_чужого_поколения_записи_не_применяется()
    {
        var entries = Entries(name: null);
        var pending = new MftPending();
        pending.Alternates[Record] = [new(20, 7, "чужое.vhdx", 9)];
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].Name, Is.Null);
            Assert.That(statistics.Partial, Is.EqualTo(1));
            Assert.That(statistics.Nameless, Is.EqualTo(1));
        }
    }

    [Test]
    public void Размер_из_чужого_поколения_записи_не_применяется()
    {
        var entries = Entries(name: "данные.bin");
        var pending = new MftPending();
        pending.Sizes[Record] = new(4096, 9);
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].SizeKnown, Is.False);
            Assert.That(statistics.Partial, Is.EqualTo(1));
        }
    }

    private static MftEntry[] Entries(string? name)
    {
        var entries = new MftEntry[64];
        entries[Record] = new()
        {
            Name = name,
            Parent = -1,
            Names = 1,
            Present = true,
            Sequence = 3,
        };

        return entries;
    }
}
