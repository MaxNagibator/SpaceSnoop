using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftLayoutTests
{
    [Test]
    public void Один_отрезок_разбирается_в_кластер_и_длину()
    {
        var runs = MftLayout.DecodeRuns([0x21, 0x18, 0x34, 0x56, 0x00]);

        Assert.That(runs, Has.Count.EqualTo(1));
        Assert.That(runs[0].Count, Is.EqualTo(0x18));
        Assert.That(runs[0].Cluster, Is.EqualTo(0x5634));
    }

    [Test]
    public void Смещения_отрезков_складываются_а_не_задают_адрес_заново()
    {
        var runs = MftLayout.DecodeRuns([0x11, 0x10, 0x20, 0x11, 0x08, 0x10, 0x00]);

        Assert.That(runs.Select(static run => run.Cluster), Is.EqualTo(new long[] { 0x20, 0x30 }));
        Assert.That(runs.Select(static run => run.Count), Is.EqualTo(new long[] { 0x10, 0x08 }));
    }

    [Test]
    public void Отрицательное_смещение_уводит_отрезок_назад()
    {
        var runs = MftLayout.DecodeRuns([0x11, 0x10, 0x40, 0x11, 0x08, 0xF0, 0x00]);

        Assert.That(runs.Select(static run => run.Cluster), Is.EqualTo(new long[] { 0x40, 0x30 }));
    }

    [Test]
    public void Разреженный_отрезок_не_попадает_в_список_и_не_сдвигает_адрес()
    {
        var runs = MftLayout.DecodeRuns([0x11, 0x10, 0x40, 0x01, 0x08, 0x11, 0x04, 0x10, 0x00]);

        Assert.That(runs.Select(static run => run.Cluster), Is.EqualTo(new long[] { 0x40, 0x50 }));
        Assert.That(runs.Select(static run => run.Count), Is.EqualTo(new long[] { 0x10, 0x04 }));
    }

    [Test]
    public void Обрубленный_список_отрезков_не_роняет_разбор()
    {
        var runs = MftLayout.DecodeRuns([0x22, 0x10]);

        Assert.That(runs, Is.Empty);
    }

    [Test]
    public void Обрубленный_список_отрезков_объявляется_неполным()
    {
        MftLayout.DecodeRuns([0x11, 0x10, 0x20, 0x22, 0x10], out var truncated);

        Assert.That(truncated, Is.True);
    }

    [Test]
    public void Целый_список_отрезков_неполным_не_объявляется()
    {
        var runs = MftLayout.DecodeRuns([0x11, 0x10, 0x20, 0x11, 0x08, 0x10, 0x00], out var truncated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(truncated, Is.False);
            Assert.That(runs, Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Подпись_секторов_возвращается_на_место()
    {
        var record = BuildRecord(out var expectedFirst, out var expectedSecond);

        Assert.That(MftLayout.ApplyFixup(record, 512), Is.True);
        Assert.That(record[510], Is.EqualTo(expectedFirst[0]));
        Assert.That(record[511], Is.EqualTo(expectedFirst[1]));
        Assert.That(record[1022], Is.EqualTo(expectedSecond[0]));
        Assert.That(record[1023], Is.EqualTo(expectedSecond[1]));
    }

    [Test]
    public void Запись_с_чужой_подписью_сектора_отвергается()
    {
        var record = BuildRecord(out _, out _);
        record[1022] = 0xEE;

        Assert.That(MftLayout.ApplyFixup(record, 512), Is.False);
    }

    [Test]
    public void Дата_вне_диапазона_даёт_пустое_значение()
    {
        Assert.That(MftLayout.ToDateTime(0), Is.EqualTo(default(DateTime)));
        Assert.That(MftLayout.ToDateTime(-1), Is.EqualTo(default(DateTime)));
        Assert.That(MftLayout.ToDateTime(long.MaxValue), Is.EqualTo(default(DateTime)));
    }

    [Test]
    public void Не_ntfs_отличается_по_подписи_загрузочного_сектора()
    {
        var boot = new byte[512];
        "NTFS    "u8.CopyTo(boot.AsSpan(3));

        Assert.That(MftLayout.IsNtfs(boot), Is.True);

        boot[3] = (byte)'F';

        Assert.That(MftLayout.IsNtfs(boot), Is.False);
    }

    private static byte[] BuildRecord(out byte[] firstTail, out byte[] secondTail)
    {
        var record = new byte[1024];

        record[4] = 0x30;
        record[6] = 3;

        record[0x30] = 0xAB;
        record[0x31] = 0xCD;

        firstTail = [0x11, 0x22];
        secondTail = [0x33, 0x44];

        record[0x32] = firstTail[0];
        record[0x33] = firstTail[1];
        record[0x34] = secondTail[0];
        record[0x35] = secondTail[1];

        record[510] = 0xAB;
        record[511] = 0xCD;
        record[1022] = 0xAB;
        record[1023] = 0xCD;

        return record;
    }
}
