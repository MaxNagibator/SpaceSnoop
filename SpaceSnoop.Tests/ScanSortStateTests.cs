using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels;

namespace SpaceSnoop.Tests;

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
    public void Compare_DirectoryBeforeFile_RegardlessOfFieldAndInvert([Values(false, true)] bool invert)
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
    public void Compare_BySize_AscendingByDefault_InvertFlips()
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
    public void Compare_ByName_UsesCultureOrder()
    {
        var alpha = MakeDir("alpha", 1);
        var beta = MakeDir("beta", 1);

        var sorter = new ScanSortState { Field = ScanSortField.Name, Invert = false };

        Assert.That(sorter.Compare(alpha, beta), Is.LessThan(0));
    }

    [Test]
    public void Compare_ByCreationDate_AscendingByDefault()
    {
        var older = MakeDir("o", 1, new(2020, 1, 1));
        var newer = MakeDir("n", 1, new(2024, 1, 1));

        var sorter = new ScanSortState { Field = ScanSortField.CreationDate, Invert = false };

        Assert.That(sorter.Compare(older, newer), Is.LessThan(0));
    }

    [Test]
    public void Compare_Nulls_SortToEnd()
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
