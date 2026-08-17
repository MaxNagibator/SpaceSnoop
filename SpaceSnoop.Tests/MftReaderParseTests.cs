using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftReaderParseTests
{
    private const int Parent = 40;
    private const uint JunctionTag = 0xA0000003;

    [Test]
    public void Имя_родитель_и_размер_читаются_из_базовой_записи()
    {
        var statistics = new MftStatistics();
        var creation = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        var access = new DateTime(2025, 6, 2, 11, 0, 0, DateTimeKind.Utc);

        var record = new MftRecordBuilder()
            .StandardInformation(creation, access)
            .FileName(Parent, 7, "отчёт.pdf")
            .ResidentData(64);

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Name, Is.EqualTo("отчёт.pdf"));
            Assert.That(entry.Parent, Is.EqualTo(Parent));
            Assert.That(entry.ParentSequence, Is.EqualTo(7));
            Assert.That(entry.Size, Is.EqualTo(64));
            Assert.That(entry.SizeKnown, Is.True);
            Assert.That(entry.CreationTime, Is.EqualTo(creation.ToLocalTime()));
            Assert.That(entry.LastAccessTime, Is.EqualTo(access.ToLocalTime()));
            Assert.That(entry.IsDirectory, Is.False);
            Assert.That(statistics.RecordsInUse, Is.EqualTo(1));
            Assert.That(statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void Каталог_отличается_флагом_записи()
    {
        var record = new MftRecordBuilder()
            .AsDirectory()
            .FileName(Parent, 1, "Загрузки");

        Assert.That(Parse(record).IsDirectory, Is.True);
    }

    [Test]
    public void Имя_в_пространстве_dos_не_выбирается()
    {
        var pending = new MftPending();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "PROGRA~1", space: MftLayout.NamespaceDos)
            .FileName(Parent, 1, "Program Files");

        var entry = Parse(record, pending: pending);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Name, Is.EqualTo("Program Files"));
            Assert.That(pending.Alternates, Is.Empty);
        }
    }

    [Test]
    public void Второе_имя_копится_отдельно_от_выбранного()
    {
        var pending = new MftPending();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "ссылка.dll")
            .FileName(60, 3, "оригинал.dll");

        var entry = Parse(record, pending: pending);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Name, Is.EqualTo("ссылка.dll"));
            Assert.That(pending.Alternates[0], Has.Count.EqualTo(1));
            Assert.That(pending.Alternates[0][0].Name, Is.EqualTo("оригинал.dll"));
            Assert.That(pending.Alternates[0][0].Parent, Is.EqualTo(60));
            Assert.That(pending.Alternates[0][0].ParentSequence, Is.EqualTo(3));
        }
    }

    [TestCase(0L, 5_000_000L, 5_000_000L, true, 0L, TestName = "Первый экстент нерезидентного data задаёт размер")]
    [TestCase(8L, 5_000_000L, 0L, false, 0L, TestName = "Не первый экстент нерезидентного data размер не задаёт")]
    [TestCase(0L, -1L, 0L, false, 1L, TestName = "Отрицательный размер делает запись неполной")]
    public void Размер_из_нерезидентного_data(long startVcn, long realSize, long expected, bool known, long partial)
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "образ.vhdx")
            .NonResidentData(realSize, startVcn);

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Size, Is.EqualTo(expected));
            Assert.That(entry.SizeKnown, Is.EqualTo(known));
            Assert.That(statistics.Partial, Is.EqualTo(partial));
            Assert.That(statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void Атрибут_с_непригодной_длиной_оставляет_запись_в_дереве_и_считается_неполной()
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "битый.bin")
            .BrokenAttribute();

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Partial, Is.EqualTo(1));
            Assert.That(statistics.Damaged, Is.Zero);
            Assert.That(entry.Name, Is.EqualTo("битый.bin"));
        }
    }

    [Test]
    public void Длина_атрибута_у_края_int_не_роняет_разбор()
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "переполнение.bin")
            .OverflowingAttribute();

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Partial, Is.EqualTo(1));
            Assert.That(statistics.Damaged, Is.Zero);
            Assert.That(entry.Name, Is.EqualTo("переполнение.bin"));
        }
    }

    [Test]
    public void Резидентный_data_за_границей_атрибута_размер_не_задаёт()
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .FileName(Parent, 1, "враньё.bin")
            .OversizedResidentData(4096);

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.SizeKnown, Is.False);
            Assert.That(entry.Size, Is.Zero);
            Assert.That(statistics.Partial, Is.EqualTo(1));
        }
    }

    [Test]
    public void Имя_нулевой_длины_записью_не_принимается()
    {
        var record = new MftRecordBuilder()
            .FileName(Parent, 1, string.Empty)
            .ResidentData(16);

        var entry = Parse(record);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Name, Is.Null);
            Assert.That(entry.Exists, Is.False);
        }
    }

    [Test]
    public void Свободная_запись_в_таблицу_не_попадает()
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .AsFree()
            .FileName(Parent, 1, "удалённый.tmp");

        var entry = Parse(record, statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Present, Is.False);
            Assert.That(statistics.RecordsInUse, Is.Zero);
            Assert.That(statistics.RecordsScanned, Is.EqualTo(1));
        }
    }

    [Test]
    public void Запись_без_подписи_с_мусором_считается_повреждённой()
    {
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .WithoutSignature()
            .FileName(Parent, 1, "мусор.bin");

        Parse(record, statistics);

        Assert.That(statistics.Damaged, Is.EqualTo(1));
    }

    [Test]
    public void Пустая_запись_повреждением_не_считается()
    {
        var entries = new MftEntry[1];
        var statistics = new MftStatistics();

        MftReader.Parse(new byte[1024], 512, 0, entries, new(), statistics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statistics.Damaged, Is.Zero);
            Assert.That(statistics.RecordsScanned, Is.EqualTo(1));
        }
    }

    [Test]
    public void Тег_reparse_читается_из_резидентного_атрибута()
    {
        var record = new MftRecordBuilder()
            .AsDirectory()
            .FileName(Parent, 1, "ссылка")
            .Reparse(JunctionTag);

        var entry = Parse(record);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ReparseTag, Is.EqualTo(JunctionTag));
            Assert.That(entry.ReparseUnknown, Is.False);
            Assert.That(entry.IsLink, Is.True);
        }
    }

    [Test]
    public void Нерезидентный_reparse_оставляет_тег_непрочитанным()
    {
        var record = new MftRecordBuilder()
            .AsDirectory()
            .FileName(Parent, 1, "ссылка")
            .NonResidentReparse();

        var entry = Parse(record);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.ReparseTag, Is.Zero);
            Assert.That(entry.ReparseUnknown, Is.True);
            Assert.That(entry.IsLink, Is.True);
        }
    }

    [Test]
    public void Запись_расширение_отдаёт_размер_и_имя_базовой()
    {
        var pending = new MftPending();
        var statistics = new MftStatistics();

        var record = new MftRecordBuilder()
            .ExtensionOf(12)
            .FileName(Parent, 1, "огромный.iso")
            .NonResidentData(9_000_000_000);

        var entry = Parse(record, statistics, pending);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(entry.Present, Is.False);
            Assert.That(statistics.Extensions, Is.EqualTo(1));
            Assert.That(pending.Sizes[12].Size, Is.EqualTo(9_000_000_000));
            Assert.That(pending.Alternates[12][0].Name, Is.EqualTo("огромный.iso"));
        }
    }

    private static MftEntry Parse(MftRecordBuilder builder, MftStatistics? statistics = null, MftPending? pending = null)
    {
        var entries = new MftEntry[1];
        MftReader.Parse(builder.Build(), 512, 0, entries, pending ?? new(), statistics ?? new());
        return entries[0];
    }
}
