using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanSortStateTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSort_{Guid.NewGuid():N}");
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

    private string _tempDir = null!;

    [Test]
    public void Каталог_всегда_раньше_файла_независимо_от_поля_и_инверсии([Values(false, true)] bool invert)
    {
        var dir = MakeDir("a", 1);
        var file = MakeFile("b.bin", 1_000_000);

        var sorter = new ScanSortState { Field = ScanSortField.Size, Invert = invert };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sorter.Compare(dir, file), Is.LessThan(0));
            Assert.That(sorter.Compare(file, dir), Is.GreaterThan(0));
        }
    }

    [Test]
    public void По_размеру_сортирует_по_возрастанию_а_инверсия_переворачивает()
    {
        var small = MakeDir("s", 10);
        var big = MakeDir("b", 1000);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(new ScanSortState { Field = ScanSortField.Size, Invert = false }.Compare(small, big), Is.LessThan(0));
            Assert.That(new ScanSortState { Field = ScanSortField.Size, Invert = true }.Compare(small, big), Is.GreaterThan(0));
        }
    }

    [Test]
    public void По_имени_сортирует_в_культурном_порядке()
    {
        var alpha = MakeDir("alpha", 1);
        var beta = MakeDir("beta", 1);

        var sorter = new ScanSortState { Field = ScanSortField.Name, Invert = false };

        Assert.That(sorter.Compare(alpha, beta), Is.LessThan(0));
    }

    [Test]
    public void По_дате_создания_сортирует_по_возрастанию()
    {
        var older = MakeDir("o", 1, new(2020, 1, 1));
        var newer = MakeDir("n", 1, new(2024, 1, 1));

        var sorter = new ScanSortState { Field = ScanSortField.CreationDate, Invert = false };

        Assert.That(sorter.Compare(older, newer), Is.LessThan(0));
    }

    [Test]
    public void По_количеству_файлов_учитывает_вложенные_и_инверсия_переворачивает()
    {
        var few = MakeDirWithFiles("few", 1);
        var many = MakeDirWithFiles("many", 1);
        Assert.That(many.TotalFileCount, Is.EqualTo(1));

        var nested = MakeDirWithFiles("nested", 4);
        many.Add(nested);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(many.TotalFileCount, Is.EqualTo(5));
            Assert.That(new ScanSortState { Field = ScanSortField.FileCount, Invert = false }.Compare(few, many), Is.LessThan(0));
            Assert.That(new ScanSortState { Field = ScanSortField.FileCount, Invert = true }.Compare(few, many), Is.GreaterThan(0));
        }
    }

    [Test]
    public void Пустые_узлы_уходят_в_конец()
    {
        var dir = MakeDir("a", 1);
        var sorter = new ScanSortState();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sorter.Compare(null, dir), Is.GreaterThan(0));
            Assert.That(sorter.Compare(dir, null), Is.LessThan(0));
            Assert.That(sorter.Compare(null, null), Is.EqualTo(0));
        }
    }

    private DirectorySpace MakeDir(string name, long size, DateTime? created = null)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        var dir = new DirectorySpace(name, null, created ?? DateTime.Now, DateTime.Now);

        if (size > 0)
        {
            var filePath = Path.Combine(path, "payload.bin");
            File.WriteAllBytes(filePath, new byte[size]);
            FileInfo[] files = [new(filePath)];
            dir.AddFiles(files.AsSpan());
        }

        return dir;
    }

    private DirectorySpace MakeDirWithFiles(string name, int fileCount)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(path);
        var dir = new DirectorySpace(name, null, DateTime.Now, DateTime.Now);

        var infos = new FileInfo[fileCount];

        for (var i = 0; i < fileCount; i++)
        {
            var filePath = Path.Combine(path, $"f{i}.bin");
            File.WriteAllBytes(filePath, [1]);
            infos[i] = new(filePath);
        }

        dir.AddFiles(infos.AsSpan());
        return dir;
    }

    private FileSpace MakeFile(string name, long size)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, new byte[size]);

        var holder = new DirectorySpace("holder", null, DateTime.Now, DateTime.Now);
        FileInfo[] files = [new(path)];
        holder.AddFiles(files.AsSpan());

        return holder.Files[0];
    }
}
