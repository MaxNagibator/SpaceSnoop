using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class GalleryOptionsTests
{
    private const string Default = @"C:\shots";

    [Test]
    public void Без_аргументов_снимаются_все_страницы_в_двух_темах()
    {
        var options = GalleryOptions.Parse([], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Directory, Is.EqualTo(Default));
            Assert.That(options.Pages, Is.EqualTo(SectionKey.All));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppTheme.Light, AppTheme.Dark }));
            Assert.That(options.Width, Is.EqualTo(AppDefaults.GalleryWidthDefault));
            Assert.That(options.Scale, Is.EqualTo(AppDefaults.ViewCaptureScaleDefault));
            Assert.That(options.Unknown, Is.Empty);
        });
    }

    [Test]
    public void Первый_позиционный_аргумент_задаёт_каталог()
    {
        var options = GalleryOptions.Parse([@"D:\out", "--themes", "dark"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Directory, Is.EqualTo(@"D:\out"));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppTheme.Dark }));
        });
    }

    [Test]
    public void Страницы_и_темы_разбираются_без_повторов()
    {
        var options = GalleryOptions.Parse(["--pages", "sync,scan,sync", "--themes", "dark,dark,light"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(new[] { SectionKey.Sync, SectionKey.Scan }));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppTheme.Dark, AppTheme.Light }));
        });
    }

    [Test]
    public void Незнакомая_страница_и_тема_докладываются_а_остальное_снимается()
    {
        var options = GalleryOptions.Parse(["--pages", "scan,тьма", "--themes", "light,неон"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(new[] { SectionKey.Scan }));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppTheme.Light }));
            Assert.That(options.Unknown, Is.EqualTo(new[] { "тьма", "неон" }));
        });
    }

    [Test]
    public void Целиком_незнакомый_список_откатывается_к_дефолту()
    {
        var options = GalleryOptions.Parse(["--pages", "неон", "--themes", "сепия"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Pages, Is.EqualTo(SectionKey.All));
            Assert.That(options.Themes, Is.EqualTo(new[] { AppTheme.Light, AppTheme.Dark }));
        });
    }

    [TestCase("1920x1080", 1920, 1080)]
    [TestCase("1920X1080", 1920, 1080)]
    [TestCase("99999x10", AppDefaults.GallerySizeMax, AppDefaults.GallerySizeMin)]
    [TestCase("мусор", AppDefaults.GalleryWidthDefault, AppDefaults.GalleryHeightDefault)]
    public void Размер_окна_разбирается_и_зажимается(string value, int width, int height)
    {
        var options = GalleryOptions.Parse(["--size", value], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Width, Is.EqualTo(width));
            Assert.That(options.Height, Is.EqualTo(height));
        });
    }

    [TestCase("2", 2d)]
    [TestCase("0.1", AppDefaults.ViewCaptureScaleMin)]
    [TestCase("10", AppDefaults.ViewCaptureScaleMax)]
    [TestCase("мусор", AppDefaults.ViewCaptureScaleDefault)]
    public void Масштаб_кадра_разбирается_и_зажимается(string value, double scale)
    {
        Assert.That(GalleryOptions.Parse(["--scale", value], Default).Scale, Is.EqualTo(scale));
    }

    [Test]
    public void Неизвестный_флаг_не_считается_каталогом()
    {
        var options = GalleryOptions.Parse(["--depth", "3"], Default);

        Assert.Multiple(() =>
        {
            Assert.That(options.Directory, Is.EqualTo(Default));
            Assert.That(options.Unknown, Does.Contain("--depth"));
        });
    }
}
