using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Tests;

[TestFixture]
public partial class ScanExportTests
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

    [TestCase(0, ScanExport.MinDepth)]
    [TestCase(99, ScanExport.MaxDepth)]
    public void Build_ClampsDepthAndReportsAppliedValue(int requested, int expected)
    {
        Assert.That(
            Build(
                    Options with
                    {
                        Depth = requested,
                    })
                .Options.Depth,
            Is.EqualTo(expected));
    }

    [Test]
    public void Build_CountsWholeTreeRegardlessOfDepth()
    {
        var model = Build(
            Options with
            {
                Depth = 1,
            });

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
        var model = Build(
            Options with
            {
                Depth = depth,
            });

        Assert.That(
            model.Directories.Select(x => x.Path),
            expectsNested
                ? Does.Contain(Path.Combine("big", "deep"))
                : Does.Not.Contain(Path.Combine("big", "deep")));
    }

    [Test]
    public void Build_TopKDirectoriesPreservesTiesAndOmittedCounts()
    {
        const int count = 50;
        const int take = 5;
        var root = new DirectorySpace(@"C:\synthetic", null, default, default);
        var sizes = Enumerable.Range(0, count).Select(i => i % 5 * 10L).ToArray();
        var children = sizes.Select((size, i) =>
            {
                var child = new DirectorySpace($"d{i}", root, default, default);
                child.SetScanned([FileSpace.Create(new ScanEntry("f.bin", size, default, default, FileAttributes.Normal, false), child)], []);
                return child;
            })
            .ToArray();

        root.SetScanned([], children);
        root.AggregateTotals();

        var model = ScanExport.Build(root, root.Name, Options, entryLimit: take);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Directories.Select(x => x.TotalBytes), Is.EqualTo(Enumerable.Repeat(40L, 5)));
            Assert.That(model.Directories.Select(x => x.Path).Distinct().Count(), Is.EqualTo(take));
            Assert.That(model.OmittedDirectories, Is.EqualTo(count - take));
            Assert.That(model.OmittedFiles, Is.EqualTo(count - take));
            Assert.That(model.Totals.Directories, Is.EqualTo(count));
        }
    }

    [TestCase(0, 1, 0, 0)]
    [TestCase(20, 15, 5, 0)]
    [TestCase(20, 0, 0, 20)]
    [TestCase(500, 0, 0, 256)]
    public void Build_TopKPreservesLargestWeightsIncludingTiesAndZero(int requested, int largest, int nextLargest, int empty)
    {
        const int count = 256;
        var root = new DirectorySpace(@"C:\synthetic", null, default, default);
        var sizes = Enumerable.Range(0, count).Select(i => empty > 0 ? 0L : i * 37 % 17 * 100L).ToArray();
        var files = sizes.Select((size, i) => FileSpace.Create(new ScanEntry($"f{i}.bin", size, default, default, FileAttributes.Normal, false), root)).ToList();
        root.SetScanned(files, []);
        root.AggregateTotals();

        var model = ScanExport.Build(root, root.Name, Options, entryLimit: requested);
        var expectedCount = largest + nextLargest + empty;
        var expectedBytes = Enumerable.Repeat(1600L, largest)
            .Concat(Enumerable.Repeat(1500L, nextLargest))
            .Concat(Enumerable.Repeat(0L, empty));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(model.Files.Select(x => x.Bytes), Is.EqualTo(expectedBytes));
            Assert.That(model.Files.Select(x => x.Path).Distinct().Count(), Is.EqualTo(expectedCount));
            Assert.That(model.OmittedFiles, Is.EqualTo(count - expectedCount));
            Assert.That(model.Totals.Files, Is.EqualTo(count));
            Assert.That(model.Totals.Bytes, Is.EqualTo(sizes.Sum()));
        }
    }

    [Test]
    public Task ToJson_MatchesContractSnapshot()
    {
        return Verify(Scrub(ScanExport.ToJson(Build(Options))), "json");
    }

    [Test]
    public void ToJson_WritesGeneratedAtWithLocalOffset()
    {
        var stamp = JsonDocument.Parse(ScanExport.ToJson(Build(Options)))
            .RootElement
            .GetProperty("generatedAt")
            .GetString();

        var parsed = DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True, stamp);
            Assert.That(value.Offset, Is.EqualTo(DateTimeOffset.Now.Offset));
        }
    }

    private static void Write(string path, int size)
    {
        File.WriteAllBytes(path, new byte[size]);
    }

    private string Scrub(string json)
    {
        return GeneratedAt()
            .Replace(json, "\"generatedAt\": \"{time}\"")
            .Replace(JsonEncode(_root), "{root}", StringComparison.Ordinal)
            .Replace(_root, "{root}", StringComparison.Ordinal);
    }

    private static string JsonEncode(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    [GeneratedRegex("\"generatedAt\": *\"[^\"]*\"")]
    private static partial Regex GeneratedAt();

    private ScanExportModel Build(ScanExportOptions options)
    {
        var root = new DiskSpaceCalculator().Calculate(new(_root), CancellationToken.None);

        return ScanExport.Build(root, _root, options, "{version}");
    }
}
