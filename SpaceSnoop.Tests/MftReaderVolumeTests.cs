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
            Assert.That(table.GetName(20), Is.EqualTo("отчёт.pdf"));
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
            Assert.That(table.GetName(1), Is.EqualTo("на-границе.bin"));
            Assert.That(table.Entries[1].Size, Is.EqualTo(64));
            Assert.That(table.GetName(2), Is.EqualTo("следом.bin"));
            Assert.That(table.GetName(20), Is.EqualTo("далеко.bin"));
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

    [TestCase(1, TestName = "Расширение доезжает до базовой записи в один поток")]
    [TestCase(4, TestName = "Расширение доезжает до базовой записи в четыре потока")]
    public void Имя_и_размер_из_расширения_склеиваются_через_границу_блоков(int threads)
    {
        var extension = new MftRecordBuilder()
            .ExtensionOf(20)
            .FileName(MftLayout.RootRecord, 1, "огромный.iso")
            .NonResidentData(9_000_000_000);

        var image = new MftVolumeBuilder()
            .Record(20, new())
            .Record(28, extension)
            .Build(Records);

        var table = Read(image, threads, 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetName(20), Is.EqualTo("огромный.iso"));
            Assert.That(table.Entries[20].Parent, Is.EqualTo(MftLayout.RootRecord));
            Assert.That(table.Entries[20].Names, Is.EqualTo(1));
            Assert.That(table.Entries[20].Size, Is.EqualTo(9_000_000_000));
            Assert.That(table.Entries[20].SizeKnown, Is.True);
            Assert.That(table.Entries[28].Present, Is.False);
            Assert.That(table.Statistics.Extensions, Is.EqualTo(1));
            Assert.That(table.Statistics.Nameless, Is.Zero);
        }
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
    public void Не_первый_экстент_в_собственной_записи_роняет_разбор()
    {
        var image = new MftVolumeBuilder().SelfStartVcn(4).Build(Records);

        Assert.Throws<InvalidDataException>(() => Read(image));
    }

    [Test]
    public void Недочитанный_хвост_таблицы_роняет_разбор()
    {
        var image = new MftVolumeBuilder()
            .Fragment(4)
            .Build(Records, Records * 1024L);

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

        var table = Read(builder.Build(Records), 4, 1024);
        var names = Enumerable.Range(MftLayout.FirstUserRecord, Records - MftLayout.FirstUserRecord).Select(table.GetName).ToArray();
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

    [TestCase(1, TestName = "Отмена посреди чтения в один поток выходит отменой")]
    [TestCase(4, TestName = "Отмена посреди чтения в четыре потока выходит отменой")]
    public void Отмена_посреди_чтения_выходит_отменой(int threads)
    {
        using var cancel = new CancellationTokenSource();
        var volume = new MftCountingVolume(
            new MftVolumeBuilder().Build(Records),
            read =>
            {
                if (read == 10)
                {
                    cancel.Cancel();
                }
            });

        Assert.Catch<OperationCanceledException>(() => MftReader.Read(volume, Letter, threads, 1024, null, cancel.Token));
    }

    [Test]
    public void Отмена_чтения_не_выглядит_порчей_разметки()
    {
        var image = new MftVolumeBuilder().Build(Records);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Catch<OperationCanceledException>(() => MftReader.Read(new MftMemoryVolume(image), Letter, 4, 1024, null, cancel.Token));
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

        var table = Read(image, 4, 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetName(20), Is.EqualTo("первое.dll"));
            Assert.That(table.Entries[20].Names, Is.EqualTo(3));
            Assert.That(
                table.Alternates[20].Select(x => table.GetName(x.Name)),
                Is.EqualTo(
                    new[]
                    {
                        "второе.dll", "третье.dll",
                    }));

            Assert.That(table.Statistics.HardLinkedFiles, Is.EqualTo(1));
        }
    }

    [Test]
    public void Расширение_нулевой_записи_не_становится_безымянным_файлом()
    {
        var image = new MftVolumeBuilder()
            .Record(16, new MftRecordBuilder().ExtensionOf(0))
            .Build(Records);

        var table = Read(image, 4, 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[0].SizeKnown, Is.True);
            Assert.That(table.Entries[16].Present, Is.False);
            Assert.That(table.Statistics.Extensions, Is.EqualTo(1));
            Assert.That(table.Statistics.Nameless, Is.Zero);
            Assert.That(table.Statistics.Partial, Is.Zero);
        }
    }

    [TestCase(0, 1)]
    [TestCase(45, 1)]
    [TestCase(0, 4)]
    [TestCase(45, 4)]
    public void Резидентный_размер_из_расширения_доезжает_через_границу_блоков(int size, int threads)
    {
        var image = new MftVolumeBuilder()
            .Record(20, new MftRecordBuilder().WithSequence(7).FileName(MftLayout.RootRecord, 1, "маленький.txt"))
            .Record(28, new MftRecordBuilder().ExtensionOf(20, 7).ResidentData(size))
            .Build(Records);

        var table = Read(image, threads, 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[20].Size, Is.EqualTo(size));
            Assert.That(table.Entries[20].SizeKnown, Is.True);
            Assert.That(table.Statistics.Extensions, Is.EqualTo(1));
            Assert.That(table.Statistics.Partial, Is.Zero);
        }
    }

    [Test]
    public void Резидентный_размер_расширения_чужого_поколения_не_применяется()
    {
        var image = new MftVolumeBuilder()
            .Record(20, new MftRecordBuilder().WithSequence(7).FileName(MftLayout.RootRecord, 1, "маленький.txt"))
            .Record(28, new MftRecordBuilder().ExtensionOf(20, 6).ResidentData(45))
            .Build(Records);

        var table = Read(image, 4, 1024);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[20].SizeKnown, Is.False);
            Assert.That(table.Statistics.Partial, Is.EqualTo(1));
        }
    }

    [TestCase(1, TestName = "Сбой чтения в один поток выходит исходным исключением")]
    [TestCase(4, TestName = "Сбой чтения в четыре потока выходит исходным исключением")]
    public void Сбой_чтения_выходит_исходным_исключением(int threads)
    {
        var volume = new MftCountingVolume(
            new MftVolumeBuilder().Build(Records),
            read =>
            {
                if (read == 10)
                {
                    throw new IOException("Сектор не читается");
                }
            });

        Assert.Throws<IOException>(() => MftReader.Read(volume, Letter, threads, 1024, null, CancellationToken.None));
    }

    [TestCase(0, 512, TestName = "Том из файла отдаёт загрузочный сектор короче страницы")]
    [TestCase(513, 100, TestName = "Том из файла отдаёт кусок с невыровненными смещением и длиной")]
    [TestCase(4095, 2, TestName = "Том из файла отдаёт кусок через границу страницы")]
    [TestCase(8192, 8192, TestName = "Том из файла отдаёт выровненный кусок")]
    public void Том_из_файла_отдаёт_кусок_как_есть(int offset, int length)
    {
        var image = new MftVolumeBuilder().Build(Records);
        Random.Shared.NextBytes(image.AsSpan(0, 16384));
        using var file = new MftImageFile(image);
        using var volume = MftFileVolume.Open(file.Path, "T:");
        var buffer = new byte[length];

        volume.ReadAt(offset, buffer);

        Assert.That(buffer, Is.EqualTo(image.AsSpan(offset, length).ToArray()));
    }

    [TestCase(8, 4, 4, 1, TestName = "Фрагментированный образ с кластером 4 КБ читается в один поток")]
    [TestCase(8, 4, 4, 4, TestName = "Фрагментированный образ с кластером 4 КБ читается в четыре потока")]
    [TestCase(1, 33, 31, 4, TestName = "Фрагментированный образ с кластером 512 Б читается в четыре потока")]
    public void Том_из_файла_сохраняет_записи_разных_экстентов(int sectorsPerCluster, int first, int second, int threads)
    {
        var image = new MftVolumeBuilder(sectorsPerCluster: sectorsPerCluster)
            .Fragment(first)
            .Fragment(second)
            .SplitSelf(1, 15, true)
            .Record(20, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "во-втором-экстенте.bin").ResidentData(48))
            .Record(Records - 1, new MftRecordBuilder().FileName(MftLayout.RootRecord, 1, "последняя.bin").ResidentData(64))
            .Build(Records);

        using var file = new MftImageFile(image);
        using var volume = MftFileVolume.Open(file.Path, "T:");

        var table = MftReader.Read(volume, Letter, threads, 4096, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.GetName(20), Is.EqualTo("во-втором-экстенте.bin"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(48));
            Assert.That(table.GetName(Records - 1), Is.EqualTo("последняя.bin"));
            Assert.That(table.Entries[Records - 1].Size, Is.EqualTo(64));
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
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
            Assert.That(table.GetName(20), Is.EqualTo("во-втором-экстенте.bin"));
            Assert.That(table.Entries[20].Size, Is.EqualTo(48));
            Assert.That(table.GetName(Records - 1), Is.EqualTo("последняя.bin"));
            Assert.That(table.Statistics.RecordsScanned, Is.EqualTo(Records));
            Assert.That(table.Statistics.Damaged, Is.Zero);
        }
    }

    private static MftTable Read(byte[] image, int threads = 1, int blockBytes = 4096)
    {
        return MftReader.Read(new MftMemoryVolume(image), Letter, threads, blockBytes, null, CancellationToken.None);
    }
}
