using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class GalleryStatesTests
{
    [TestCase(SectionKey.Scan)]
    [TestCase(SectionKey.Sync)]
    [TestCase(SectionKey.Overview)]
    [TestCase(SectionKey.Cleanup)]
    [TestCase(SectionKey.Docker)]
    public void Занятое_состояние_снимается_у_страниц_со_своей_операцией(string page)
    {
        Assert.That(GalleryStates.SupportsPage(page, GalleryStates.Busy), Is.True);
    }

    [TestCase(SectionKey.Logs)]
    [TestCase(SectionKey.About)]
    [TestCase(SectionKey.Settings)]
    [TestCase(SectionKey.Schedule)]
    public void Страница_без_своей_операции_в_занятом_прогоне_пропускается(string page)
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryStates.SupportsPage(page, GalleryStates.Busy), Is.False);
            Assert.That(GalleryStates.SupportsPage(page, GalleryStates.Idle), Is.True);
        });
    }

    [Test]
    public void Состояние_готово_у_страниц_не_снимается()
    {
        Assert.That(GalleryStates.All.Where(state => GalleryStates.SupportsPage(SectionKey.Scan, state)),
            Is.EqualTo(new[] { GalleryStates.Idle, GalleryStates.Busy }));
    }

    [TestCase(GalleryDialogs.Delete)]
    [TestCase(GalleryDialogs.Archive)]
    public void Диалог_операции_снимается_во_всех_трёх_состояниях(string dialog)
    {
        Assert.That(GalleryStates.All.Where(state => GalleryStates.SupportsDialog(dialog, state)),
            Is.EqualTo(GalleryStates.All));
    }

    [TestCase(GalleryDialogs.Confirm)]
    [TestCase(GalleryDialogs.Diff)]
    [TestCase(GalleryDialogs.Git)]
    [TestCase(GalleryDialogs.Batch)]
    public void Диалог_без_операции_снимается_только_в_покое(string dialog)
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryStates.SupportsDialog(dialog, GalleryStates.Idle), Is.True);
            Assert.That(GalleryStates.SupportsDialog(dialog, GalleryStates.Busy), Is.False);
            Assert.That(GalleryStates.SupportsDialog(dialog, GalleryStates.Done), Is.False);
        });
    }

    [Test]
    public void Подсказки_снимаются_только_в_покое()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryStates.SupportsTip(GalleryStates.Idle), Is.True);
            Assert.That(GalleryStates.SupportsTip(GalleryStates.Busy), Is.False);
            Assert.That(GalleryStates.SupportsTip(GalleryStates.Done), Is.False);
        });
    }

    [TestCase("idle", GalleryStates.Idle)]
    [TestCase("Busy", GalleryStates.Busy)]
    [TestCase("покой", null)]
    public void Ключ_состояния_узнаётся_без_учёта_регистра(string value, string? state)
    {
        Assert.That(GalleryStates.Match(value), Is.EqualTo(state));
    }
}
