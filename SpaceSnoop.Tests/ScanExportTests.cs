using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Export;

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

    [Test]
    public Task ToJson_MatchesContractSnapshot()
    {
        return Verify(Scrub(ScanExport.ToJson(Build(Options))), "json");
    }

    [Test]
    public void ToJson_WritesGeneratedAtWithLocalOffset()
    {
        var stamp = JsonDocument.Parse(ScanExport.ToJson(Build(Options))).RootElement
            .GetProperty("generatedAt")
            .GetString();

        var parsed = DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed, Is.True, stamp);
            Assert.That(value.Offset, Is.EqualTo(DateTimeOffset.Now.Offset));
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

    private static void Write(string path, int size)
    {
        File.WriteAllBytes(path, new byte[size]);
    }

    private string Scrub(string json)
    {
        return GeneratedAt().Replace(json, "\"generatedAt\": \"{time}\"")
            .Replace(JsonEncode(_root), "{root}", StringComparison.Ordinal)
            .Replace(_root, "{root}", StringComparison.Ordinal);
    }

    private static string JsonEncode(string path)
    {
        return path.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    [GeneratedRegex("\"generatedAt\": *\"[^\"]*\"")]
    private static partial Regex GeneratedAt();

    private ScanExportModel Build(ScanExportOptions options, int entryLimit = ScanExport.DefaultEntryLimit)
    {
        var root = new DiskSpaceCalculator().Calculate(new(_root), CancellationToken.None);

        return ScanExport.Build(root, _root, options, "{version}", entryLimit);
    }
}
