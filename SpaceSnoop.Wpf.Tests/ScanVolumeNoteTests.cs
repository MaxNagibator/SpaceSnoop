using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanLinkNoteTests
{
    [Test]
    public void Снятый_двойной_счёт_показывается_размером()
    {
        Assert.That(ScanLinkNote.Describe(13L * 1024 * 1024 * 1024), Does.Contain("13").And.Contain("ГБ"));
    }

    [TestCase(0L)]
    [TestCase(-1L)]
    public void Без_повторных_имён_примечания_нет(long extraNameBytes)
    {
        Assert.That(ScanLinkNote.Describe(extraNameBytes), Is.Null);
    }

    [Test]
    public void Подсказка_называет_величину_и_причину()
    {
        var hint = ScanLinkNote.Explain("≈13 ГБ");

        Assert.That(hint, Does.Contain("≈13 ГБ").And.Contain(ScanLinkNote.Hint));
    }
}

[TestFixture]
public class ScanVolumeNoteTests
{
    private const long Total = 60L * 1024 * 1024 * 1024;
    private const long Used = 34L * 1024 * 1024 * 1024;

    [Test]
    public void Логический_объём_выше_занятого_объясняется_примечанием()
    {
        var note = ScanVolumeNote.Describe(@"C:\", Used + (7L * 1024 * 1024 * 1024), new DriveCapacity("C:", Total, Used));

        Assert.That(note, Does.Contain("7").And.Contain("ГБ"));
    }

    [Test]
    public void Подсказка_называет_разницу_и_её_причины()
    {
        var hint = ScanVolumeNote.Explain("≈7 ГБ");

        Assert.That(hint, Does.Contain("≈7 ГБ").And.Contain(ScanVolumeNote.Hint));
    }

    [TestCase(@"C:")]
    [TestCase(@"C:\")]
    [TestCase(@"c:\")]
    public void Корень_тома_узнаётся_с_разделителем_и_без_него(string path)
    {
        var note = ScanVolumeNote.Describe(path, Used * 2, new DriveCapacity("C:", Total, Used));

        Assert.That(note, Is.Not.Null);
    }

    [Test]
    public void Каталог_на_томе_примечания_не_получает()
    {
        var note = ScanVolumeNote.Describe(@"C:\Downloads", Used * 2, new DriveCapacity("C:", Total, Used));

        Assert.That(note, Is.Null);
    }

    [Test]
    public void Недоступный_диск_примечания_не_получает()
    {
        var note = ScanVolumeNote.Describe(@"C:\", Used * 2, null);

        Assert.That(note, Is.Null);
    }

    [Test]
    public void Превышение_в_пределах_порога_молчит()
    {
        var note = ScanVolumeNote.Describe(@"C:\", Used + (long)(Used * AppDefaults.ScanVolumeNoteFraction / 2), new DriveCapacity("C:", Total, Used));

        Assert.That(note, Is.Null);
    }

    [Test]
    public void Объём_меньше_занятого_молчит()
    {
        var note = ScanVolumeNote.Describe(@"C:\", Used / 2, new DriveCapacity("C:", Total, Used));

        Assert.That(note, Is.Null);
    }
}

[TestFixture]
public class ScanDropNoteTests
{
    [Test]
    public void Полный_скан_о_потерях_молчит()
    {
        Assert.That(ScanDropNote.Describe(0, 0), Is.Null);
    }

    [TestCase(3L, 0L, "3")]
    [TestCase(0L, 4L, "4")]
    [TestCase(2L, 5L, "7")]
    public void Отброшенное_называется_числом(long dropped, long unknownSize, string expected)
    {
        Assert.That(ScanDropNote.Describe(dropped, unknownSize), Does.StartWith(expected));
    }

    [Test]
    public void Подсказка_разделяет_пропавшее_и_непрочитанный_размер()
    {
        var hint = ScanDropNote.Explain(2, 4096, 3);

        Assert.That(hint, Does.Contain("нет в дереве").And.Contain("размер не прочитан").And.Contain(ScanDropNote.Hint));
    }

    [Test]
    public void Подсказка_без_потерянных_байт_их_не_называет()
    {
        var hint = ScanDropNote.Explain(2, 0, 0);

        Assert.That(hint, Does.Contain("нет в дереве").And.Not.Contain("на ≈"));
    }
}
