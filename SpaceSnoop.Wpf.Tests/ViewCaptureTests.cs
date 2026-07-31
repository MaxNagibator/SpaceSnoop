using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ViewCaptureTests
{
    [TestCase("scan", "scan")]
    [TestCase("Sync Diff Panel", "sync-diff-panel")]
    [TestCase("scan/StructurePanel", "scan-structurepanel")]
    [TestCase("Панель", "view")]
    [TestCase("  ", "view")]
    public void Метка_кадра_приводится_к_имени_файла(string label, string slug)
    {
        Assert.That(ViewCapture.Slug(label), Is.EqualTo(slug));
    }

    [Test]
    public void Имя_файла_несёт_время_и_метку()
    {
        var name = ViewCapture.FileName("scan", new DateTimeOffset(2025, 3, 14, 9, 30, 15, TimeSpan.Zero));

        Assert.That(name, Is.EqualTo("view-20250314-093015-000-scan.png"));
    }

    [Test]
    public void Лишними_считаются_снимки_старше_последних_N()
    {
        string[] files =
        [
            @"C:\shots\view-20250314-093015-000-scan.png",
            @"C:\shots\view-20250314-093020-000-sync.png",
            @"C:\shots\view-20250314-093025-000-logs.png",
        ];

        Assert.Multiple(() =>
        {
            Assert.That(ViewCapture.Obsolete(files, 2), Is.EqualTo(new[] { files[0] }));
            Assert.That(ViewCapture.Obsolete(files, 3), Is.Empty);
            Assert.That(ViewCapture.Obsolete(files, 0), Has.Count.EqualTo(3));
        });
    }
}
