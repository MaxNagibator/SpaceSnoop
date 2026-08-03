using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Globalization;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanProgressTests
{
    [SetUp]
    public void SetUp()
    {
        _culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new("ru-RU");
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = _culture;
    }

    private CultureInfo _culture = CultureInfo.CurrentCulture;

    [Test]
    public void Оценка_объёма_есть_только_у_корня_диска()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-estimate");

        try
        {
            var root = new DirectoryInfo(Path.GetPathRoot(directory.FullName)!);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(directory), Is.Null);
                Assert.That(ScanProgressViewModel.EstimateTotalBytes(root), Is.Not.Null.And.GreaterThan(0));
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void Неготовый_диск_не_даёт_оценку()
    {
        var used = DriveInfo.GetDrives().Select(static drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVU".Cast<char?>().FirstOrDefault(letter => !used.Contains(letter!.Value));

        Assume.That(free, Is.Not.Null, "на машине нет свободной буквы диска");

        Assert.That(ScanProgressViewModel.EstimateTotalBytes(new($@"{free}:\")), Is.Null);
    }
}
