using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftReaderPendingTests
{
    private const int Record = 30;

    [Test]
    public void Байты_безымянной_записи_попадают_в_счётчик()
    {
        var entries = Entries(new(1), name: null);
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
        var entries = Entries(new(1), name: "данные.bin");
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
        var names = new NameTable(1);
        var entries = Entries(names, name: null);
        var pending = new MftPending();
        pending.Alternates[Record] = [new(20, 7, names.Intern("чужое.vhdx"), 9)];
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entries[Record].HasName, Is.False);
            Assert.That(statistics.Partial, Is.EqualTo(1));
            Assert.That(statistics.Nameless, Is.EqualTo(1));
        }
    }

    [Test]
    public void Альтернативное_имя_с_нулевым_NameId_закрывает_безымянную_запись_и_отбрасывает_чужое_поколение()
    {
        var names = new NameTable(1);
        var cache = LocalNameCache.Create();
        var zeroId = cache.Intern(names, "спасённый_\uD800.bin".AsSpan());
        var entries = Entries(names, name: null);
        var pending = new MftPending();
        pending.Alternates[Record] =
        [
            new(20, 7, cache.Intern(names, "чужое.bin".AsSpan()), 9),
            new(25, 4, zeroId, 3),
            new(26, 5, cache.Intern(names, "запасной.bin".AsSpan()), 3),
        ];
        var statistics = new MftStatistics();

        MftReader.ApplyPending(entries, pending, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(zeroId, Is.Zero);
            Assert.That(entries[Record].HasName, Is.True);
            Assert.That(entries[Record].Exists, Is.True);
            Assert.That(entries[Record].NameId, Is.EqualTo(zeroId));
            Assert.That(entries[Record].Parent, Is.EqualTo(25));
            Assert.That(entries[Record].ParentSequence, Is.EqualTo(4));
            Assert.That(entries[Record].Names, Is.EqualTo(2));
            Assert.That(statistics.Partial, Is.EqualTo(1));
            Assert.That(statistics.Nameless, Is.Zero);
            Assert.That(statistics.HardLinkedFiles, Is.EqualTo(1));
        }
    }

    [Test]
    public void Размер_из_чужого_поколения_записи_не_применяется()
    {
        var entries = Entries(new(1), name: "данные.bin");
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

    private static MftEntry[] Entries(NameTable names, string? name)
    {
        var entries = new MftEntry[64];
        var entry = new MftEntry
        {
            Parent = -1,
            Names = 1,
            Present = true,
            Sequence = 3,
        };

        if (name is not null)
        {
            entry.SetName(names.Intern(name));
        }

        entries[Record] = entry;
        return entries;
    }
}
