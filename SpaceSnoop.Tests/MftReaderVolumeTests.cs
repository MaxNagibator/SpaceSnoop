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

    private static MftTable Read(byte[] image)
    {
        return MftReader.Read(new MftMemoryVolume(image), Letter, null, CancellationToken.None);
    }
}
