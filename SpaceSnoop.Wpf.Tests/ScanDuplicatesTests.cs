using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

public class ScanDuplicatesTests
{
    [Test]
    public void Пустой_результат_называет_число_проверенных_файлов()
    {
        var report = new DuplicateReport([], 0, 128, 0, 0, []);

        Assert.That(ScanDuplicatesViewModel.DescribeReport(report), Is.EqualTo("Дубликатов не найдено · проверено файлов: 128"));
    }

    [Test]
    public void Итог_называет_группы_отдельно_от_возвращаемого_места()
    {
        var report = new DuplicateReport([Group()], 4096, 8, 0, 0, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ScanDuplicatesViewModel.DescribeReport(report), Is.EqualTo("Групп: 1 · проверено файлов: 8"));
            Assert.That(ScanDuplicatesViewModel.DescribeReclaim(report), Does.StartWith("вернёт "));
        }
    }

    [Test]
    public void Пустой_результат_возвращаемого_места_не_называет()
    {
        Assert.That(ScanDuplicatesViewModel.DescribeReclaim(new([], 0, 128, 0, 0, [])), Is.Empty);
    }

    [TestCase(@"C:\Data\Загрузки\отпуск.jpg", @"C:\Data", "Загрузки")]
    [TestCase(@"C:\Data\отпуск.jpg", @"C:\Data", ".")]
    [TestCase(@"D:\Прочее\отпуск.jpg", @"C:\Data", @"D:\Прочее")]
    public void Каталог_показывается_от_корня_скана(string path, string root, string expected)
    {
        Assert.That(DuplicateText.Directory(path, root), Is.EqualTo(expected));
    }

    [Test]
    public void Каталог_вне_корня_остаётся_полным()
    {
        var nested = @"C:\Data\" + new string('и', 200);

        Assert.That(DuplicateText.Directory(nested + @"\файл.bin", @"C:\Data"), Is.EqualTo(new string('и', 200)));
    }

    [Test]
    public void Усечение_и_неполный_обход_объявляются_числом()
    {
        var report = new DuplicateReport([Group()], 4096, 8, 12, 3, ["a", "b"]);

        var notice = ScanDuplicatesViewModel.DescribeLimits(report);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(notice, Does.Contain("ещё 12 групп"));
            Assert.That(notice, Does.Contain("каталогов без доступа при сканировании – 3"));
            Assert.That(notice, Does.Contain("не удалось прочитать файлов: 2"));
        }
    }

    [Test]
    public void Полный_результат_без_ограничений_примечания_не_даёт()
    {
        Assert.That(ScanDuplicatesViewModel.DescribeLimits(new([Group()], 4096, 8, 0, 0, [])), Is.Empty);
    }

    private static DuplicateGroup Group()
    {
        return new(4096, [], 2);
    }
}
