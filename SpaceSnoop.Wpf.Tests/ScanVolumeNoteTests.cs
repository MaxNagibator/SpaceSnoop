using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;

namespace SpaceSnoop.Wpf.Tests;

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
