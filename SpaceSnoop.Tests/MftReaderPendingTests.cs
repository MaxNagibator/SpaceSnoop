using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftReaderPendingTests
{
    private const int Record = 30;

    [Test]
    public void Имя_из_записи_расширения_достраивает_запись_без_имени()
    {
        var entries = Entries(name: null);
        var pending = new MftPending();
        pending.Alternates[Record] = [new(20, 7, "большой.vhdx")];
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].Name, Is.EqualTo("большой.vhdx"));
            Assert.That(entries[Record].Parent, Is.EqualTo(20));
            Assert.That(entries[Record].Names, Is.EqualTo(1));
            Assert.That(statistics.Nameless, Is.Zero);
        }
    }

    [Test]
    public void Размер_из_записи_расширения_доезжает_до_записи_без_имени()
    {
        var entries = Entries(name: null);
        var pending = new MftPending();
        pending.Sizes[Record] = new(4096);
        pending.Alternates[Record] = [new(20, 7, "большой.vhdx")];

        MftReader.ApplyPending(entries, pending, new());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].Size, Is.EqualTo(4096));
            Assert.That(entries[Record].SizeKnown, Is.True);
        }
    }

    [Test]
    public void Оставшиеся_без_имени_записи_считаются()
    {
        var entries = Entries(name: null);
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, new(), statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Nameless, Is.EqualTo(1));
            Assert.That(entries[Record].Exists, Is.False);
        }
    }

    [Test]
    public void Повторные_имена_складываются_с_основным()
    {
        var entries = Entries(name: "система.dll");
        var pending = new MftPending();
        pending.Alternates[Record] = [new(20, 7, "копия.dll"), new(21, 9, "ещё.dll")];
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].Name, Is.EqualTo("система.dll"));
            Assert.That(entries[Record].Names, Is.EqualTo(3));
            Assert.That(statistics.HardLinkedFiles, Is.EqualTo(1));
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
        }
    }

    private static MftEntry[] Entries(string? name)
    {
        var entries = new MftEntry[64];
        entries[Record] = new() { Name = name, Parent = -1, Names = 1, Present = true, Sequence = 3 };

        return entries;
    }
}
