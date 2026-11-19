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
    public void Достижимая_из_корня_скана_запись_остаётся_на_своём_месте()
    {
        var table = Table();
        table.Entries[File] = Leaf("внутри.bin", Target, 7, 100);
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
    public void Достижимое_от_корня_поддерево_проходом_не_трогается()
    {
        var table = Table();
        table.Entries[40] = Leaf("внутри.bin", Target, 7, 700);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[40].Exists, Is.True);
            Assert.That(table.Statistics.OrphanFiles, Is.Zero);
        }
    }

    [Test]
    public void Другое_имя_спасает_файл_с_родителем_переиспользованным_под_файл()
    {
        var table = Table();
        table.Entries[Outside] = Leaf("exceptions.py", Root, 1, 50);
        table.Entries[Outside].Sequence = 48;
        table.Entries[File] = Leaf("кэш_0", Outside, 27, 24654);
        table.Alternates[File] = [new(Target, 7, "внутри.bin")];

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Root, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Outside], Is.EqualTo(-1));
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(table.Entries[File].Name, Is.EqualTo("внутри.bin"));
            Assert.That(table.Statistics.StaleParents, Is.EqualTo(1));
            Assert.That(table.Statistics.Rehomed, Is.EqualTo(1));
            Assert.That(table.Statistics.Detached, Is.Zero);
            Assert.That(table.Statistics.OrphanFiles, Is.Zero);
            Assert.That(table.Statistics.OrphanBytes, Is.Zero);
        }
    }

    [Test]
    public void Жёсткая_ссылка_переподвешивается_под_имя_достижимое_из_корня_скана()
    {
        var table = Table();
        table.Entries[File] = Leaf("снаружи.bin", Outside, 9, 100);
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
    public void Запись_корня_не_каталог_отменяет_скан()
    {
        var table = Table();
        table.Entries[Root] = Leaf(".", Root, 1, 0);
        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\"), 'C');

        Assert.That(start, Is.EqualTo(-1));
    }

    [Test]
    public void Имя_под_корнем_скана_спасает_запись_с_недоступным_основным_родителем()
    {
        var table = Table();
        table.Entries[File] = Leaf("снаружи.bin", Outside, 3, 100);
        table.Alternates[File] = [new(Target, 7, "внутри.bin")];

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(table.Entries[File].Name, Is.EqualTo("внутри.bin"));
            Assert.That(table.Statistics.Rehomed, Is.EqualTo(1));
            Assert.That(table.Statistics.Detached, Is.Zero);
            Assert.That(table.Statistics.OrphanFiles, Is.Zero);
            Assert.That(table.Statistics.OrphanBytes, Is.Zero);
        }
    }

    [Test]
    public void Исключённое_системное_поддерево_не_считается_потерей_пользовательских_файлов()
    {
        var table = Table();
        table.Entries[11] = Folder("$Extend", Root, 1, 11);
        table.Entries[24] = Folder("$RmMetadata", 11, 11, 2);
        table.Entries[File] = Leaf("$TxfLog", 24, 2, 700);
        table.Entries[40] = Leaf("пользовательский.txt", Target, 7, 100);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Root, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[File].Exists, Is.True);
            Assert.That(table.Statistics.Detached, Is.Zero);
            Assert.That(table.Statistics.OrphanFiles, Is.Zero);
            Assert.That(table.Statistics.OrphanBytes, Is.Zero);
            Assert.That(links.NextSibling[11], Is.EqualTo(-1));
            Assert.That(links.FirstChild[Root], Is.EqualTo(Target));
            Assert.That(links.NextSibling[Target], Is.EqualTo(Outside));
            Assert.That(links.NextSibling[Outside], Is.EqualTo(-1));
            Assert.That(links.FirstChild[Target], Is.EqualTo(40));
        }
    }

    [Test]
    public void Каталог_предок_корня_скана_не_переподвешивается_под_потомка()
    {
        var table = Table();
        table.Entries[Target].Parent = Outside;
        table.Entries[Target].ParentSequence = 9;
        table.Entries[File] = Leaf("внутри.bin", Target, 7, 100);
        table.Alternates[Outside] = [new(Target, 7, "повреждённый-предок")];
        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Target, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[Outside].Parent, Is.EqualTo(Root));
            Assert.That(table.Entries[File].Exists, Is.True);
            Assert.That(table.Statistics.Rehomed, Is.Zero);
            Assert.That(table.Statistics.Detached, Is.Zero);
        }
    }

    [Test]
    public void Каталог_с_недоступным_родителем_спасается_допустимым_альтернативным_именем()
    {
        var table = Table();
        table.Entries[Outside].Parent = 63;
        table.Entries[File] = Leaf("внутри.bin", Outside, 9, 100);
        table.Alternates[Outside] = [new(Target, 7, "спасённый-каталог")];
        var links = MftLinks.Build(table, CancellationToken.None);

        links.Rehome(table, Target, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[Outside].Parent, Is.EqualTo(Target));
            Assert.That(table.Entries[Outside].Name, Is.EqualTo("спасённый-каталог"));
            Assert.That(table.Entries[File].Exists, Is.True);
            Assert.That(table.Statistics.Rehomed, Is.EqualTo(1));
            Assert.That(table.Statistics.Detached, Is.Zero);
        }
    }

    [Test]
    public void Недостижимый_обычный_каталог_не_маскируется_системным_поддеревом()
    {
        var table = Table();
        table.Entries[11] = Folder("$Extend", Root, 1, 11);
        table.Entries[24] = Leaf("$Quota", 11, 11, 200);
        table.Entries[40] = Folder("оторванный", 63, 1, 5);
        table.Entries[41] = Leaf("потерянный.bin", 40, 5, 700);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[24].Exists, Is.True);
            Assert.That(table.Entries[41].Exists, Is.False);
            Assert.That(table.Statistics.Detached, Is.EqualTo(2));
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(700));
        }
    }

    [TestCase(0u, true, true, TestName = "Каталог с непрочитанным тегом считается ссылкой")]
    [TestCase(0u, false, false, TestName = "Файл с непрочитанным тегом остаётся файлом")]
    public void Непрочитанный_тег_reparse_судится_по_виду_записи(uint tag, bool directory, bool expected)
    {
        var entry = new MftEntry
        {
            ReparseTag = tag,
            ReparseUnknown = true,
            IsDirectory = directory,
        };

        Assert.That(entry.IsLink, Is.EqualTo(expected));
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
    public void Причины_отрыва_различают_удалённого_родителя_устаревшее_поколение_и_каскад_а_переподвес_убирает_образец()
    {
        var table = Table();
        table.Entries[35] = Leaf("спасённый.bin", 60, 1, 128);
        table.Alternates[35] = [new(Target, 7, "внутри.bin")];
        table.Entries[36] = Leaf("удалённый-родитель.bin", 61, 1, 256);
        table.Entries[37] = Leaf("устаревший-родитель.bin", Target, 3, 512);
        table.Entries[40] = Folder("оторванный", 63, 1, 5);
        table.Entries[41] = Leaf("каскадный.bin", 40, 5, 1024);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Root, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);
        var report = MftScanner.FormatReport(table.Statistics, 0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Statistics.Rehomed, Is.EqualTo(1));
            Assert.That(table.Statistics.MissingParents, Is.EqualTo(3));
            Assert.That(table.Statistics.StaleParents, Is.EqualTo(1));
            Assert.That(table.Statistics.Unreachable, Is.EqualTo(1));
            Assert.That(table.Statistics.Detached, Is.EqualTo(4));
            Assert.That(
                table.Statistics.DetachedSamples.Select(static x => x.Text),
                Is.EqualTo(
                    new[]
                    {
                        "40->63[свободна]", "37->20[поколение 3!=7]", "36->61[свободна]", "41->40[ветка]",
                    }));

            Assert.That(report, Does.Contain("без записи родителя 3"));
            Assert.That(report, Does.Contain("устаревших ссылок на родителя 1"));
            Assert.That(report, Does.Contain("в оторванных ветках 1"));
            Assert.That(report, Does.Contain("примеры: 40->63[свободна]"));
        }
    }

    [Test]
    public void Пропущенная_ссылка_переподвесом_не_воскресает()
    {
        var table = Table();

        table.Entries[File] = Leaf("ссылка", Outside, 9, 100);
        table.Entries[File].ReparseTag = MftLayout.ReparseNameSurrogate | 0x03;
        table.Alternates[File] = [new(Target, 7, "внутри.bin")];

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(-1));
            Assert.That(table.Statistics.SkippedLinks, Is.EqualTo(1));
            Assert.That(table.Statistics.Rehomed, Is.Zero);
        }
    }

    [TestCase(48, 27, 1)]
    [TestCase(48, 48, 0)]
    [TestCase(48, 0, 0)]
    [TestCase(0, 27, 0)]
    public void Родитель_переиспользованный_файлом_учитывает_расхождение_поколения(
        int currentSequence,
        int parentSequence,
        long staleParents)
    {
        var table = Table();
        table.Entries[Target] = Leaf("exceptions.py", Root, 1, 50);
        table.Entries[Target].Sequence = (ushort)currentSequence;
        table.Entries[File] = Leaf("d908ee3d92017122_0", Target, (ushort)parentSequence, 24654);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.Rehome(table, Root, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(-1));
            Assert.That(table.Entries[Target].Exists, Is.True);
            Assert.That(table.Entries[File].Exists, Is.False);
            Assert.That(table.Statistics.StaleParents, Is.EqualTo(staleParents));
            Assert.That(table.Statistics.Detached, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(24654));
        }
    }

    [Test]
    public void Совпадающее_поколение_родителя_подвешивает_запись()
    {
        var table = Table();
        table.Entries[File] = Leaf("a.txt", Target, 7, 100);

        var links = MftLinks.Build(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(links.FirstChild[Target], Is.EqualTo(File));
            Assert.That(table.Statistics.StaleParents, Is.Zero);
        }
    }

    [Test]
    public void Ссылка_без_поколения_принимается_как_есть()
    {
        var table = Table();
        table.Entries[File] = Leaf("a.txt", Target, 0, 100);

        var links = MftLinks.Build(table, CancellationToken.None);

        Assert.That(links.FirstChild[Target], Is.EqualTo(File));
    }

    [Test]
    public void Тег_с_битом_подмены_пространства_имён_остаётся_ссылкой()
    {
        var entry = new MftEntry
        {
            ReparseTag = MftLayout.ReparseNameSurrogate | 0x03,
        };

        Assert.That(entry.IsLink, Is.True);
    }

    [Test]
    public void Точное_совпадение_имени_побеждает_совпадение_без_учёта_регистра()
    {
        var table = Table();
        table.Entries[22] = Folder("Данные", Root, 1, 11);
        table.Entries[23] = Folder("данные", Root, 1, 12);

        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\данные"), 'C');

        Assert.That(start, Is.EqualTo(23));
    }

    [Test]
    public void Устаревшая_ссылка_на_родителя_не_уводит_файл_в_чужое_поддерево()
    {
        var table = Table();
        table.Entries[File] = Leaf("a.txt", Target, 3, 100);

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
    public void Файл_под_отброшенным_каталогом_считается_потерянным_а_не_пропадает_молча()
    {
        var table = Table();
        table.Entries[40] = Folder("оторванный", 63, 1, 5);
        table.Entries[41] = Leaf("внутри.bin", 40, 5, 700);

        var links = MftLinks.Build(table, CancellationToken.None);
        links.DropUnreachable(table, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(700));
            Assert.That(table.Entries[41].Exists, Is.False);
        }
    }

    [Test]
    public void Целая_запись_корня_даёт_стартовую_точку()
    {
        var table = Table();
        var links = MftLinks.Build(table, CancellationToken.None);

        var start = MftScanner.Locate(table, links, new(@"C:\"), 'C');

        Assert.That(start, Is.EqualTo(Root));
    }

    [Test]
    public void Циклические_родители_системного_каталога_не_зацикливают_проверку_достижимости()
    {
        var table = Table();
        table.Entries[11] = Folder("$Extend", 24, 2, 11);
        table.Entries[24] = Folder("повреждённый", 11, 11, 2);
        table.Entries[File] = Leaf("внутри.bin", 24, 2, 700);
        table.Entries[40] = Leaf("оторванный.bin", 63, 1, 100);
        var links = MftLinks.Build(table, CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        links.DropUnreachable(table, cancel.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[File].Exists, Is.True);
            Assert.That(table.Entries[40].Exists, Is.False);
            Assert.That(table.Statistics.Detached, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanFiles, Is.EqualTo(1));
            Assert.That(table.Statistics.OrphanBytes, Is.EqualTo(100));
        }
    }

    private static MftTable Table()
    {
        var entries = new MftEntry[64];
        entries[Root] = Folder(".", Root, 1, 1);
        entries[Target] = Folder("цель", Root, 1, 7);
        entries[Outside] = Folder("чужой", Root, 1, 9);

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
