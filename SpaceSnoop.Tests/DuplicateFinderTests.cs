using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using System.Runtime.InteropServices;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DuplicateFinderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopDup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(12)]
    public void Одинаковые_копии_собираются_в_одну_группу(int copies)
    {
        var content = new string('a', 4096);

        for (var index = 0; index < copies; index++)
        {
            Write($"copy{index}.bin", content);
        }

        var report = Find();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Members, Has.Count.EqualTo(copies));
            Assert.That(report.Groups[0].DistinctFiles, Is.EqualTo(copies));
            Assert.That(report.ReclaimableBytes, Is.EqualTo((copies - 1) * 4096L));
            Assert.That(report.Examined, Is.EqualTo(copies));
        }
    }

    [TestCase(2)]
    [TestCase(12)]
    public void Одинаковый_размер_при_разном_содержимом_группы_не_даёт(int files)
    {
        for (var index = 0; index < files; index++)
        {
            Write($"file{index}.bin", new string((char)('a' + index), 4096));
        }

        var report = Find();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Is.Empty);
            Assert.That(report.ReclaimableBytes, Is.Zero);
            Assert.That(report.Examined, Is.EqualTo(files));
        }
    }

    [Test]
    public void Пустые_файлы_и_мелочь_ниже_порога_не_рассматриваются()
    {
        Write("empty1.bin", string.Empty);
        Write("empty2.bin", string.Empty);
        Write("small1.bin", "abc");
        Write("small2.bin", "abc");

        var report = Find(DuplicateOptions.Default with { MinSize = 10 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Is.Empty);
            Assert.That(report.Examined, Is.Zero);
        }
    }

    [Test]
    public void Жёсткая_ссылка_названа_и_места_не_возвращает()
    {
        var content = new string('h', 2048);
        var target = Write("target.bin", content);
        var link = Path.Combine(_tempDir, "link.bin");

        if (!CreateHardLink(link, target, IntPtr.Zero))
        {
            Assert.Ignore("Файловая система не поддерживает жёсткие ссылки");
        }

        var report = Find();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Members.Select(static x => x.Kind), Is.EqualTo(new[] { DuplicateMemberKind.Copy, DuplicateMemberKind.HardLink }));
            Assert.That(report.Groups[0].DistinctFiles, Is.EqualTo(1));
            Assert.That(report.ReclaimableBytes, Is.Zero);
        }
    }

    [Test]
    public void Жёсткая_ссылка_не_мешает_настоящей_копии_вернуть_место()
    {
        var content = new string('h', 2048);
        var target = Write("target.bin", content);
        Write("copy.bin", content);
        var link = Path.Combine(_tempDir, "link.bin");

        if (!CreateHardLink(link, target, IntPtr.Zero))
        {
            Assert.Ignore("Файловая система не поддерживает жёсткие ссылки");
        }

        var report = Find();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Members, Has.Count.EqualTo(3));
            Assert.That(report.Groups[0].DistinctFiles, Is.EqualTo(2));
            Assert.That(report.ReclaimableBytes, Is.EqualTo(2048));
        }
    }

    [Test]
    public void Потолок_членов_режет_список_но_не_возвращаемые_байты()
    {
        var content = new string('m', 1024);

        for (var index = 0; index < 5; index++)
        {
            Write($"copy{index}.bin", content);
        }

        var report = Find(DuplicateOptions.Default with { MemberLimit = 2 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups[0].Members, Has.Count.EqualTo(2));
            Assert.That(report.Groups[0].OmittedMembers, Is.EqualTo(3));
            Assert.That(report.Groups[0].ReclaimableBytes, Is.EqualTo(4 * 1024));
        }
    }

    [Test]
    public void Потолок_групп_объявляется_числом_и_не_меняет_итог()
    {
        Write("big1.bin", new string('b', 4096));
        Write("big2.bin", new string('b', 4096));
        Write("small1.bin", new string('s', 1024));
        Write("small2.bin", new string('s', 1024));

        var report = Find(DuplicateOptions.Default with { GroupLimit = 1 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Size, Is.EqualTo(4096));
            Assert.That(report.OmittedGroups, Is.EqualTo(1));
            Assert.That(report.ReclaimableBytes, Is.EqualTo(4096 + 1024));
        }
    }

    [Test]
    public void Каталог_с_ошибкой_обхода_доложен_отдельно()
    {
        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "x.bin"), "xxxx");

        var tree = Tree();
        tree.SubDirectories[0].Error();

        var report = new DuplicateFinder().Find(tree, DuplicateOptions.Default, null, CancellationToken.None);

        Assert.That(report.UnreadableDirectories, Is.EqualTo(1));
    }

    [Test]
    public void Отмена_прерывает_поиск()
    {
        var content = new string('c', 4096);
        Write("copy1.bin", content);
        Write("copy2.bin", content);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new DuplicateFinder().Find(Tree(), DuplicateOptions.Default, null, cancellation.Token));
    }

    [Test]
    public void Прогресс_считает_проверенные_файлы_и_прочитанные_байты()
    {
        var content = new string('p', 4096);
        Write("copy1.bin", content);
        Write("copy2.bin", content);

        var reports = new List<OperationProgress>();
        new DuplicateFinder().Find(Tree(), DuplicateOptions.Default, new InlineProgress(reports.Add), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reports, Has.Count.EqualTo(2));
            Assert.That(reports[^1].Completed, Is.EqualTo(2));
            Assert.That(reports.Select(static x => x.Current), Has.All.Contains(".bin"));
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr attributes);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private DirectorySpace Tree()
    {
        var directory = new DirectoryInfo(_tempDir);
        var tree = new DiskSpaceCalculator().Calculate(directory, CancellationToken.None);
        tree.FixAbsolutePath(directory);
        return tree;
    }

    private DuplicateReport Find(DuplicateOptions? options = null)
    {
        return new DuplicateFinder().Find(Tree(), options ?? DuplicateOptions.Default, null, CancellationToken.None);
    }
}
