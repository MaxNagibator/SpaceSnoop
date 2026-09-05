using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class GalleryOptionsTests
{
    private const string Default = @"C:\shots";

    [OneTimeSetUp]
    public void RegisterThemes()
    {
        PackScheme.Ensure();
        AppThemes.Register();
    }

    [Test]
    public void Без_аргументов_снимаются_все_страницы_в_двух_темах()
    {
        var options = GalleryOptions.Parse([], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Directory, Is.EqualTo(Default));
            Assert.That(options.Pages, Is.EqualTo(SectionKey.All));
            Assert.That(options.Dialogs, Is.EqualTo(GalleryDialogs.All));
            Assert.That(options.Tips, Is.EqualTo(GalleryTips.All));
            Assert.That(options.State, Is.EqualTo(GalleryStates.Idle));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppThemes.LightKey, AppThemes.DarkKey }));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        });
    }

    [Test]
    public void Диалоги_разбираются_без_повторов_а_none_отключает_их_съёмку()
    {
        var picked = GalleryOptions.Parse(["--dialogs", "diff,confirm,diff"], Default);
        var off = GalleryOptions.Parse(["--dialogs", GalleryOptions.NoneValue], Default);

        Assert.Multiple(() =>
        {
            Assert.That(picked.Dialogs, Is.EqualTo(new[] { GalleryDialogs.Diff, GalleryDialogs.Confirm }));
            Assert.That(off.Dialogs, Is.Empty);
            Assert.That(off.Arguments.Unknown, Is.Empty);
        });
    }

    [Test]
    public void Подсказки_разбираются_отдельно_от_диалогов()
    {
        var picked = GalleryOptions.Parse(["--tips", "treemap-corner,treemap-corner"], Default);
        var off = GalleryOptions.Parse(["--tips", GalleryOptions.NoneValue], Default);
        var unknown = GalleryOptions.Parse(["--tips", "treemap-center,всплывашка"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(picked.Tips, Is.EqualTo(new[] { GalleryTips.TreemapCorner }));
            Assert.That(picked.Dialogs, Is.EqualTo(GalleryDialogs.All));
            Assert.That(off.Tips, Is.Empty);
            Assert.That(unknown.Tips, Is.EqualTo(new[] { GalleryTips.TreemapCenter }));
            Assert.That(unknown.Arguments.Unknown, Is.EqualTo(new[] { "всплывашка" }));
        });
    }

    [Test]
    public void Незнакомый_диалог_докладывается_а_остальные_снимаются()
    {
        var options = GalleryOptions.Parse(["--dialogs", "diff,вьюха"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Dialogs, Is.EqualTo(new[] { GalleryDialogs.Diff }));
            Assert.That(options.Arguments.Unknown, Is.EqualTo(new[] { "вьюха" }));
        });
    }

    [TestCase("busy", GalleryStates.Busy)]
    [TestCase("BUSY", GalleryStates.Busy)]
    [TestCase("done", GalleryStates.Done)]
    public void Состояние_разбирается_без_учёта_регистра(string value, string state)
    {
        var options = GalleryOptions.Parse(["--state", value], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.State, Is.EqualTo(state));
            Assert.That(options.Arguments.Unknown, Is.Empty);
        });
    }

    [Test]
    public void Незнакомое_состояние_докладывается_и_откатывается_к_покою()
    {
        var options = GalleryOptions.Parse(["--state", "работает"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.State, Is.EqualTo(GalleryStates.Idle));
            Assert.That(options.Arguments.Unknown, Is.EqualTo(new[] { "работает" }));
        });
    }

    [Test]
    public void Страницы_и_темы_разбираются_без_повторов()
    {
        var options = GalleryOptions.Parse(["--pages", "sync,scan,sync", "--themes", "dark,dark,light"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(new[] { SectionKey.Sync, SectionKey.Scan }));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppThemes.DarkKey, AppThemes.LightKey }));
        });
    }

    [Test]
    public void Незнакомая_страница_и_тема_докладываются_а_остальное_снимается()
    {
        var options = GalleryOptions.Parse(["--pages", "scan,тьма", "--themes", "light,неон"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(new[] { SectionKey.Scan }));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppThemes.LightKey }));
            Assert.That(options.Arguments.Unknown, Is.EqualTo(new[] { "неон", "тьма" }));
        });
    }

    [Test]
    public void Целиком_незнакомый_список_откатывается_к_дефолту()
    {
        var options = GalleryOptions.Parse(["--pages", "неон", "--themes", "сепия"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(SectionKey.All));
            Assert.That(options.Themes, Is.EqualTo(GalleryOptions.DefaultThemes));
        });
    }

    [Test]
    public void Тема_Tarkov_снимается_только_по_явной_просьбе()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GalleryOptions.Parse([], Default).Themes, Does.Not.Contain(AppThemes.TarkovKey));
            Assert.That(GalleryOptions.Parse(["--themes", AppThemes.TarkovKey], Default).Themes,
                Is.EqualTo(new[] { AppThemes.TarkovKey }));
        });
    }
}
