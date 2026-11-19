using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class GalleryStatesTests
{
    [Test]
    public void Выбор_цели_снимается_только_у_скана(
        [Values(SectionKey.Sync, SectionKey.Cleanup, SectionKey.Settings)]
        string page,
        [Values(GalleryStates.Picker, GalleryStates.Welcome, GalleryStates.Advice)]
        string state)
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryStates.SupportsPage(SectionKey.Scan, state), Is.True);
            Assert.That(GalleryStates.SupportsPage(page, state), Is.False);
            Assert.That(GalleryStates.SupportsTip(state), Is.False);
            Assert.That(GalleryDialogs.All.Any(dialog => GalleryStates.SupportsDialog(dialog, state)), Is.False);
        });
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

    [TestCase(GalleryDialogs.Delete)]
    [TestCase(GalleryDialogs.Archive)]
    public void Диалог_операции_снимается_во_всех_трёх_состояниях(string dialog)
    {
        Assert.That(
            GalleryStates.All.Where(state => GalleryStates.SupportsDialog(dialog, state)),
            Is.EqualTo(
                new[]
                {
                    GalleryStates.Idle, GalleryStates.Busy, GalleryStates.Done,
                }));
    }

    [TestCase(SectionKey.Scan)]
    [TestCase(SectionKey.Sync)]
    [TestCase(SectionKey.Overview)]
    [TestCase(SectionKey.Cleanup)]
    [TestCase(SectionKey.Docker)]
    public void Занятое_состояние_снимается_у_страниц_со_своей_операцией(string page)
    {
        Assert.That(GalleryStates.SupportsPage(page, GalleryStates.Busy), Is.True);
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

    [Test]
    public void Состояние_workloads_показывает_страницу_очистки_без_запуска_cli()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryStates.All, Does.Contain(GalleryStates.DevelopmentWorkloads));
            Assert.That(GalleryStates.All, Does.Contain(GalleryStates.DevelopmentMsBuild));
            Assert.That(GalleryStates.SupportsPage(SectionKey.Cleanup, GalleryStates.DevelopmentWorkloads), Is.True);
            Assert.That(GalleryStates.SupportsPage(SectionKey.Cleanup, GalleryStates.DevelopmentMsBuild), Is.True);
            Assert.That(GalleryStates.SupportsDialog(GalleryDialogs.Delete, GalleryStates.DevelopmentWorkloads), Is.False);
            Assert.That(GalleryStates.SupportsDialog(GalleryDialogs.Delete, GalleryStates.DevelopmentMsBuild), Is.False);
        });
    }

    [Test]
    public void Состояние_готово_у_страниц_не_снимается()
    {
        Assert.That(GalleryStates.SupportsPage(SectionKey.Scan, GalleryStates.Done), Is.False);
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
}
