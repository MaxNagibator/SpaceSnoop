using SpaceSnoop.Core;
using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ScanExportTests
{
    private static readonly ScanExportOptions Options = new(ScanExport.DefaultDepth, false, 1);

    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopScan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        Write(Path.Combine(_root, "a.txt"), 100);

        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        Write(Path.Combine(big, "big.bin"), 5000);

        var deep = Path.Combine(big, "deep");
        Directory.CreateDirectory(deep);
        Write(Path.Combine(deep, "deep.bin"), 700);

        var small = Path.Combine(_root, "данные");
        Directory.CreateDirectory(small);
        Write(Path.Combine(small, "s.txt"), 50);

        Directory.CreateDirectory(Path.Combine(_root, "empty"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public void Build_CountsWholeTreeRegardlessOfDepth()
    {
        var model = Build(Options with { Depth = 1 });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Totals.Bytes, Is.EqualTo(5850));
            Assert.That(model.Totals.Files, Is.EqualTo(4));
            Assert.That(model.Totals.Directories, Is.EqualTo(4));
            Assert.That(model.Totals.Errors, Is.Zero);
        }
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    public void Build_ListsSubDirectoriesUpToDepth(int depth, bool expectsNested)
    {
        var model = Build(Options with { Depth = depth });

        Assert.That(model.Directories.Select(x => x.Path), expectsNested
            ? Does.Contain(Path.Combine("big", "deep"))
            : Does.Not.Contain(Path.Combine("big", "deep")));
    }

    [Test]
    public void Build_OrdersDirectoriesBySizeAndSeparatesOwnBytes()
    {
        var model = Build(Options);
        var big = model.Directories[0];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Directories.Select(x => x.TotalBytes), Is.Ordered.Descending);
            Assert.That(big.Path, Is.EqualTo("big"));
            Assert.That(big.TotalBytes, Is.EqualTo(5700));
            Assert.That(big.OwnBytes, Is.EqualTo(5000));
            Assert.That(big.FileCount, Is.EqualTo(2));
            Assert.That(big.DirectoryCount, Is.EqualTo(1));
            Assert.That(big.Error, Is.False);
        }
    }

    [Test]
    public void Build_KeepsLargestFilesWithRelativePaths()
    {
        var model = Build(Options);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Files.Select(x => x.Path), Is.EqualTo(new[]
            {
                Path.Combine("big", "big.bin"),
                Path.Combine("big", "deep", "deep.bin"),
                "a.txt",
                Path.Combine("данные", "s.txt"),
            }));

            Assert.That(model.Files[0].Bytes, Is.EqualTo(5000));
            Assert.That(model.OmittedFiles, Is.Zero);
        }
    }

    [Test]
    public void Build_TruncatesToLargestAndReportsOmitted()
    {
        var model = Build(Options, 2);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Files.Select(x => x.Bytes), Is.EqualTo(new[] { 5000L, 700L }));
            Assert.That(model.OmittedFiles, Is.EqualTo(2));
            Assert.That(model.Directories, Has.Count.EqualTo(2));
            Assert.That(model.OmittedDirectories, Is.EqualTo(2));
        }
    }

    [TestCase(0, ScanExport.MinDepth)]
    [TestCase(99, ScanExport.MaxDepth)]
    public void Build_ClampsDepthAndReportsAppliedValue(int requested, int expected)
    {
        Assert.That(Build(Options with { Depth = requested }).Options.Depth, Is.EqualTo(expected));
    }

    [Test]
    public void ToJson_KeepsCyrillicAndSeparatorsReadable()
    {
        var json = ScanExport.ToJson(Build(Options));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(json, Does.Contain("данные"));
            Assert.That(json, Does.Not.Contain("\\u04"));
        }
    }

    private static void Write(string path, int size)
    {
        File.WriteAllBytes(path, new byte[size]);
    }

    private ScanExportModel Build(ScanExportOptions options, int entryLimit = ScanExport.DefaultEntryLimit)
    {
        var root = new DiskSpaceCalculator().Calculate(new(_root), CancellationToken.None);

        return ScanExport.Build(root, _root, options, "1.0.0", entryLimit);
    }
}
