using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftLinksTests
{
    private const int Root = 5;
    private const int Target = 20;
    private const int Outside = 21;
    private const int File = 30;

    [Test]
    public void Совпадающее_поколение_родителя_подвешивает_запись()
    {
        var table = Table();
        table.Entries[File] = Leaf(name: "a.txt", parent: Target, parentSequence: 7, size: 100);

        var links = MftLinks.Build(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(table.Statistics.StaleParents, Is.Zero);
        }
    }

    [Test]
    public void Устаревшая_ссылка_на_родителя_не_уводит_файл_в_чужое_поддерево()
    {
        var table = Table();
        table.Entries[File] = Leaf(name: "a.txt", parent: Target, parentSequence: 3, size: 100);

        var links = MftLinks.Build(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(-1));
            Assert.That(table.Statistics.StaleParents, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(100));
        }
    }

    [Test]
    public void Ссылка_без_поколения_принимается_как_есть()
    {
        var table = Table();
        table.Entries[File] = Leaf(name: "a.txt", parent: Target, parentSequence: 0, size: 100);

        var links = MftLinks.Build(table, CancellationToken.None);

        Assert.That(links.FirstChild[Target], Is.EqualTo(File));
    }

    [Test]
    public void Жёсткая_ссылка_переподвешивается_под_имя_достижимое_из_корня_скана()
    {
        var table = Table();
        table.Entries[File] = Leaf(name: "снаружи.bin", parent: Outside, parentSequence: 9, size: 100);
        table.Alternates[File] = [new(Target, 7, "внутри.bin")];

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(links.FirstChild[Outside], Is.EqualTo(-1));
            Assert.That(table.Entries[File].Name, Is.EqualTo("внутри.bin"));
            Assert.That(table.Statistics.Rehomed, Is.EqualTo(1));
        }
    }

    [Test]
    public void Достижимая_из_корня_скана_запись_остаётся_на_своём_месте()
    {
        var table = Table();
        table.Entries[File] = Leaf(name: "внутри.bin", parent: Target, parentSequence: 7, size: 100);
        table.Alternates[File] = [new(Outside, 9, "снаружи.bin")];

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(table.Statistics.Rehomed, Is.Zero);
        }
    }

    [Test]
    public void Файл_под_отброшенным_каталогом_считается_потерянным_а_не_пропадает_молча()
    {
        var table = Table();
        table.Entries[40] = Folder(name: "оторванный", parent: 63, parentSequence: 1, sequence: 5);
        table.Entries[41] = Leaf(name: "внутри.bin", parent: 40, parentSequence: 5, size: 700);

        var links = MftLinks.Build(table, CancellationToken.None);
        var attached = links.FirstChild[40];
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(attached, Is.EqualTo(41), "файл успевает подвеситься к каталогу до его отбрасывания");
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(700));
            Assert.That(table.Entries[41].Exists, Is.False);
        }
    }

    [Test]
    public void Достижимое_от_корня_поддерево_проходом_не_трогается()
    {
        var table = Table();
        table.Entries[40] = Leaf(name: "внутри.bin", parent: Target, parentSequence: 7, size: 700);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[40].Exists, Is.True);
            Assert.That(table.Statistics.OrphanFiles, Is.Zero);
        }
    }

    [Test]
    public void Повреждённая_запись_корня_отменяет_скан_вместо_пустого_дерева()
    {
        var table = Table();
        table.Entries[Root] = default;
        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\"), 'C');

        Assert.That(start, Is.EqualTo(-1));
    }

    [Test]
    public void Запись_корня_не_каталог_отменяет_скан()
    {
        var table = Table();
        table.Entries[Root] = Leaf(name: ".", parent: Root, parentSequence: 1, size: 0);
        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\"), 'C');

        Assert.That(start, Is.EqualTo(-1));
    }

    [Test]
    public void Целая_запись_корня_даёт_стартовую_точку()
    {
        var table = Table();
        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\"), 'C');

        Assert.That(start, Is.EqualTo(Root));
    }

    [TestCase(0u, true, true, TestName = "Каталог с непрочитанным тегом считается ссылкой")]
    [TestCase(0u, false, false, TestName = "Файл с непрочитанным тегом остаётся файлом")]
    public void Непрочитанный_тег_reparse_судится_по_виду_записи(uint tag, bool directory, bool expected)
    {
        var entry = new MftEntry { ReparseTag = tag, ReparseUnknown = true, IsDirectory = directory };

        Assert.That(entry.IsLink, Is.EqualTo(expected));
    }

    [Test]
    public void Тег_с_битом_подмены_пространства_имён_остаётся_ссылкой()
    {
        var entry = new MftEntry { ReparseTag = MftLayout.ReparseNameSurrogate | 0x03 };

        Assert.That(entry.IsLink, Is.True);
    }

    private static MftTable Table()
    {
        var entries = new MftEntry[64];
        entries[Root] = Folder(name: ".", parent: Root, parentSequence: 1, sequence: 1);
        entries[Target] = Folder(name: "цель", parent: Root, parentSequence: 1, sequence: 7);
        entries[Outside] = Folder(name: "чужой", parent: Root, parentSequence: 1, sequence: 9);

        return new(entries, new(), []);
    }

    private static MftEntry Folder(string name, int parent, ushort parentSequence, ushort sequence)
    {
        return new()
        {
            Name = name,
            Parent = parent,
            ParentSequence = parentSequence,
            Sequence = sequence,
            IsDirectory = true,
            Names = 1,
            Present = true,
        };
    }

    private static MftEntry Leaf(string name, int parent, ushort parentSequence, long size)
    {
        return new()
        {
            Name = name,
            Parent = parent,
            ParentSequence = parentSequence,
            Sequence = 1,
            Size = size,
            SizeKnown = true,
            Names = 1,
            Present = true,
        };
    }
}
