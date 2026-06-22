using SpaceSnoop.Core.Docker;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DockerUsageTests
{
    private const string Sample =
        """
        {"Active":"5","Reclaimable":"7.52GB (39%)","Size":"19.26GB","TotalCount":"98","Type":"Images"}
        {"Active":"0","Reclaimable":"201.1MB (100%)","Size":"201.1MB","TotalCount":"8","Type":"Containers"}
        {"Active":"4","Reclaimable":"1.8GB (90%)","Size":"2GB","TotalCount":"20","Type":"Local Volumes"}
        {"Active":"0","Reclaimable":"19.45GB","Size":"31.25GB","TotalCount":"541","Type":"Build Cache"}
        """;

    [Test]
    public void Parse_ReadsAllBuckets()
    {
        var buckets = DockerUsage.Parse(Sample);

        Assert.That(buckets, Has.Count.EqualTo(4));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(buckets[0].Type, Is.EqualTo("Images"));
            Assert.That(buckets[0].TotalCount, Is.EqualTo(98));
            Assert.That(buckets[0].Active, Is.EqualTo(5));
            Assert.That(buckets[0].Size, Is.EqualTo("19.26GB"));
            Assert.That(buckets[3].Type, Is.EqualTo("Build Cache"));
            Assert.That(buckets[3].Reclaimable, Is.EqualTo("19.45GB"));
        }
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("\n\n")]
    public void Parse_BlankInput_ReturnsEmpty(string input)
    {
        Assert.That(DockerUsage.Parse(input), Is.Empty);
    }

    [Test]
    public void Parse_SkipsMalformedLines()
    {
        var input = "not json\n" + """{"Type":"Images","TotalCount":"1","Active":"0","Size":"1GB","Reclaimable":"0B"}""";

        var buckets = DockerUsage.Parse(input);

        Assert.That(buckets, Has.Count.EqualTo(1));
        Assert.That(buckets[0].Type, Is.EqualTo("Images"));
    }
}
