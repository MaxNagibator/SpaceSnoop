using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftReaderVolumeTests
{
    private const int Records = 32;
    private const char Letter = 'T';

    [Test]
    public void Записи_тома_читаются_подряд()
    {
        var builder = new MftVolumeBuilder();

        var image = builder
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "отчёт.pdf").ResidentData(128))
            .Record(21, new MftRecordBuilder().AsDirectory().FileName(MftLayout.RootRecord, 1, "Загрузки"))
            .Build(Records);

        var table = Read(image);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries, Has.Length.EqualTo(Records));
            Assert.That(table.Entries[20].Name, Is.EqualTo("отчёт.pdf"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(128));
            Assert.That(table.Entries[21].IsDirectory, Is.True);
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void Запись_на_границе_отрезков_не_теряется()
    {
        var image = new MftVolumeBuilder(sectorsPerCluster: 1)
            .Fragment(3)
            .Fragment(61)
            .Record(1, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "на-границе.bin").ResidentData(64))
            .Record(2, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "следом.bin").ResidentData(96))
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "далеко.bin").ResidentData(32))
            .Build(Records);

        var table = Read(image);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[1].Name, Is.EqualTo("на-границе.bin"));
            Assert.That(table.Entries[1].Size, Is.EqualTo(64));
            Assert.That(table.Entries[2].Name, Is.EqualTo("следом.bin"));
            Assert.That(table.Entries[20].Name, Is.EqualTo("далеко.bin"));
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void Недочитанный_хвост_таблицы_роняет_разбор()
    {
        var image = new MftVolumeBuilder()
            .Fragment(4)
            .Build(Records, declaredBytes: Records * 1024L);

        Assert.Throws<InvalidDataException>(() => Read(image));
    }

    [TestCase(0, TestName = "Собственная запись $MFT без подписи роняет разбор")]
    [TestCase(510, TestName = "Собственная запись $MFT с чужой подписью сектора роняет разбор")]
    public void Испорченная_собственная_запись_роняет_разбор(int damageOffset)
    {
        var builder = new MftVolumeBuilder();
        var image = builder.Build(Records);

        image.AsSpan((int)builder.MftOffset + damageOffset, 2).Clear();

        Assert.Throws<InvalidDataException>(() => Read(image));
    }

    [Test]
    public void Номера_записей_не_разъезжаются_при_нарезке_на_блоки()
    {
        var builder = new MftVolumeBuilder();
        var expected = new string[Records];

        for (var index = MftLayout.FirstUserRecord; index < Records; index++)
        {
            expected[index] = $"файл-{index}.bin";
            builder.Record(index, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, expected[index]).ResidentData(index));
        }

        var table = Read(builder.Build(Records), threads: 4, blockBytes: 1024);
        var names = table.Entries.Select(x => x.Name).ToArray()[MftLayout.FirstUserRecord..];
        var sizes = table.Entries.Select(x => x.Size).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(names, Is.EqualTo(expected[MftLayout.FirstUserRecord..]));
            Assert.That(sizes[MftLayout.FirstUserRecord], Is.EqualTo(MftLayout.FirstUserRecord));
            Assert.That(sizes[Records - 1], Is.EqualTo(Records - 1));
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    [TestCase(1, TestName = "Расширение доезжает до базовой записи в один поток")]
    [TestCase(4, TestName = "Расширение доезжает до базовой записи в четыре потока")]
    public void Имя_и_размер_из_расширения_склеиваются_через_границу_блоков(int threads)
    {
        var extension = new MftRecordBuilder()
            .ExtensionOf(20)
            .FileName(MftLayout.RootRecord, 1, "огромный.iso")
            .NonResidentData(9_000_000_000);

        var image = new MftVolumeBuilder()
            .Record(20, new MftRecordBuilder())
            .Record(28, extension)
            .Build(Records);

        var table = Read(image, threads, blockBytes: 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[20].Name, Is.EqualTo("огромный.iso"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(9_000_000_000));
            Assert.That(table.Entries[20].SizeKnown, Is.True);
            Assert.That(table.Statistics.Extensions, Is.EqualTo(1));
        }
    }

    [Test]
    public void Повторные_имена_из_разных_блоков_складываются_в_одну_запись()
    {
        var second = new MftRecordBuilder().ExtensionOf(20).FileName(60, 1, "второе.dll");
        var third = new MftRecordBuilder().ExtensionOf(20).FileName(70, 1, "третье.dll");

        var image = new MftVolumeBuilder()
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "первое.dll").ResidentData(64))
            .Record(24, second)
            .Record(28, third)
            .Build(Records);

        var table = Read(image, threads: 4, blockBytes: 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[20].Name, Is.EqualTo("первое.dll"));
            Assert.That(table.Entries[20].Names, Is.EqualTo(3));
            Assert.That(table.Alternates[20].Select(x => x.Name), Is.EqualTo(new[] { "второе.dll", "третье.dll" }));
            Assert.That(table.Statistics.HardLinkedFiles, Is.EqualTo(1));
        }
    }

    [TestCase(false, TestName = "Резидентный $ATTRIBUTE_LIST дочитывает таблицу целиком")]
    [TestCase(true, TestName = "Нерезидентный $ATTRIBUTE_LIST дочитывает таблицу целиком")]
    public void Экстенты_из_списка_атрибутов_дочитывают_таблицу(bool listOutside)
    {
        var image = new MftVolumeBuilder()
            .Fragment(4)
            .Fragment(4)
            .SplitSelf(1, 15, listOutside)
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "во-втором-экстенте.bin").ResidentData(48))
            .Record(Records - 1, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "последняя.bin").ResidentData(64))
            .Build(Records);

        var table = Read(image);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries, Has.Length.EqualTo(Records));
            Assert.That(table.Entries[20].Name, Is.EqualTo("во-втором-экстенте.bin"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(48));
            Assert.That(table.Entries[Records - 1].Name, Is.EqualTo("последняя.bin"));
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    [Test]
    public void Запись_расширение_вне_первого_экстента_роняет_разбор()
    {
        var image = new MftVolumeBuilder()
            .Fragment(4)
            .Fragment(4)
            .SplitSelf(1, 20)
            .Build(Records);

        Assert.Throws<InvalidDataException>(() => Read(image));
    }

    [Test]
    public void Не_первый_экстент_в_собственной_записи_роняет_разбор()
    {
        var image = new MftVolumeBuilder().SelfStartVcn(4).Build(Records);

        Assert.Throws<InvalidDataException>(() => Read(image));
    }

    [Test]
    public void Отмена_чтения_не_выглядит_порчей_разметки()
    {
        var image = new MftVolumeBuilder().Build(Records);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Catch<OperationCanceledException>(
            () => MftReader.Read(new MftMemoryVolume(image), Letter, 4, 1024, null, cancel.Token));
    }

    private static MftTable Read(byte[] image, int threads = 1, int blockBytes = 4096)
    {
        return MftReader.Read(new MftMemoryVolume(image), Letter, threads, blockBytes, null, CancellationToken.None);
    }
}
