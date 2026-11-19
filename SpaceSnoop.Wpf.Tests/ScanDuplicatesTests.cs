using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

public class ScanDuplicatesTests
{
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
    public void Длинный_путь_внутри_корня_не_обрезается()
    {
        var nested = @"C:\Data\" + new string('и', 200);

        Assert.That(DuplicateText.Directory(nested + @"\файл.bin", @"C:\Data"), Is.EqualTo(new string('и', 200)));
    }

    [TestCase(@"C:\Data\Загрузки\отпуск.jpg", @"C:\Data", "Загрузки")]
    [TestCase(@"C:\Data\отпуск.jpg", @"C:\Data", ".")]
    [TestCase(@"D:\Прочее\отпуск.jpg", @"C:\Data", @"D:\Прочее")]
    public void Каталог_показывается_от_корня_скана(string path, string root, string expected)
    {
        Assert.That(DuplicateText.Directory(path, root), Is.EqualTo(expected));
    }

    [Test]
    public void Полный_результат_без_ограничений_примечания_не_даёт()
    {
        Assert.That(ScanDuplicatesViewModel.DescribeLimits(new([Group()], 4096, 8, 0, 0, [])), Is.Empty);
    }

    [Test]
    public void Пустой_результат_возвращаемого_места_не_называет()
    {
        Assert.That(ScanDuplicatesViewModel.DescribeReclaim(new([], 0, 128, 0, 0, [])), Is.Empty);
    }

    [Test]
    public void Пустой_результат_называет_число_проверенных_файлов()
    {
        var report = new DuplicateReport([], 0, 128, 0, 0, []);

        Assert.That(ScanDuplicatesViewModel.DescribeReport(report), Is.EqualTo("Дубликатов не найдено · проверено файлов: 128"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Режим_дубликатов_появляется_в_панели_только_по_настройке(bool enabled)
    {
        var modes = ScanViewModel.BuildViewModes(enabled);

        Assert.That(
            modes.Any(mode => mode.Text == "Дубликаты"),
            Is.EqualTo(enabled),
            "Состав сегментов разошёлся с настройкой режима дубликатов.");
    }

    [TestCase("", 0, ExpectedResult = "2|Дубликатов не найдено|Искать от 0 МБ|3")]
    [TestCase("x0", 0, ExpectedResult = "1|Дубликатов не найдено|Искать от 0 МБ|2")]
    [TestCase("y0", 0, ExpectedResult = "2|Дубликатов не найдено|Искать от 0 МБ|2")]
    [TestCase("x0 y0 y1", 0, ExpectedResult = "0|Дубликатов не осталось|Искать от 0 МБ|0")]
    [TestCase("x0 y0 y1", 3, ExpectedResult = "0|Показанные дубликаты разобраны|Искать заново|0")]
    public string Удаление_пересчитывает_группы_и_итог(string deleted, int omittedGroups)
    {
        const long size = 4096;
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "spacesnoop-duplicates-removed", Guid.NewGuid().ToString("N")));
        var root = new DirectorySpace("root", null, DateTime.Now, DateTime.Now);
        var files = new Dictionary<string, FileSpace>();

        DuplicateMember Copy(string name)
        {
            var path = Path.Combine(folder.FullName, name);
            File.WriteAllBytes(path, new byte[size]);
            files[name] = FileSpace.Create(new(path), root);
            return new(files[name], path, DuplicateMemberKind.Copy);
        }

        DuplicateGroup[] groups;

        try
        {
            groups = [new(size, [Copy("x0"), Copy("x1")], 2), new(size, [Copy("y0"), Copy("y1"), Copy("y2")], 3)];
        }
        finally
        {
            folder.Delete(true);
        }

        var settings = new MemorySettings();

        var duplicates = new ScanDuplicatesViewModel(
            new NoopDialogs(),
            new(new(), new FakeUiDispatcher(), NullLogger<DuplicateProgressDialogViewModel>.Instance),
            settings,
            new(settings),
            new(new(), new(settings)),
            () => root,
            static (_, _) => 0,
            static () => false);

        duplicates.Apply(new(groups, 3 * size, 5, omittedGroups, 0, []));
        duplicates.RemoveDeleted(deleted.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(name => (SpaceBase)files[name]).ToHashSet());

        var reclaim = duplicates.ReclaimText.Length == 0
            ? 0
            : Enumerable.Range(1, 3).Single(units => duplicates.ReclaimText == $"вернёт {SizeFormatter.Format(units * size)}");

        var repeats = ReferenceEquals(duplicates.NothingFoundCommand, duplicates.FindCommand);
        Assert.That(repeats, Is.EqualTo(duplicates.NothingFoundActionText == "Искать заново"));

        return $"{duplicates.Groups.Count}|{duplicates.NothingFoundHeading}|{duplicates.NothingFoundActionText}|{reclaim}";
    }

    [TestCase("a", ExpectedResult = true)]
    [TestCase("b", ExpectedResult = true)]
    [TestCase("соседний", ExpectedResult = false)]
    public bool Удалённый_предок_снимает_файл_из_групп(string deleted)
    {
        var root = new DirectorySpace("root", null, DateTime.Now, DateTime.Now);
        var a = new DirectorySpace("a", root, DateTime.Now, DateTime.Now);
        var b = new DirectorySpace("b", a, DateTime.Now, DateTime.Now);
        var sibling = new DirectorySpace("соседний", root, DateTime.Now, DateTime.Now);

        var set = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance)
        {
            deleted switch
            {
                "a" => a,
                "b" => b,
                _ => sibling,
            },
        };

        return ScanDuplicatesViewModel.IsGone(b, set);
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

    private static DuplicateGroup Group()
    {
        return new(4096, [], 2);
    }
}
