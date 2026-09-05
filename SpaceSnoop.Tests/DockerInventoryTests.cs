using SpaceSnoop.Core.Docker;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DockerInventoryTests
{
    private const string Sample =
        """
        {"Images":[
          {"Containers":"2","CreatedSince":"2 hours ago","ID":"sha256:6519cac115dec11b8aac5c730016c248d4c1683f0beb38505dbdbd597a44c6de","Repository":"thevsakeeper/twitchtrackerbot","Size":"1.57GB","Tag":"latest"},
          {"Containers":"0","CreatedSince":"4 hours ago","ID":"sha256:54c150461e3843b3c8e626ceb143a7f5088fabd13fc15e0b0a9a3e9f300a5224","Repository":"<none>","Size":"143MB","Tag":"<none>"}],
        "Containers":[
          {"ID":"19f19c32bd80","Names":"ttb_full","Size":"1.77MB","State":"exited","Status":"Exited (137) 2 days ago"}],
        "Volumes":[
          {"Links":"1","Name":"data-vol","Size":"512MB"},
          {"Links":"0","Name":"0d4ccd469bdd","Size":"0B"}],
        "BuildCache":[
          {"ID":"v0k7c6ebkqir","Size":"0B"}]}
        """;

    [Test]
    public void Parse_FlattensImagesContainersVolumes_SkippingBuildCache()
    {
        var objects = DockerInventory.Parse(Sample);

        Assert.That(objects, Has.Count.EqualTo(5));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(objects.Count(o => o.Kind == DockerObjectKind.Image), Is.EqualTo(2));
            Assert.That(objects.Count(o => o.Kind == DockerObjectKind.Container), Is.EqualTo(1));
            Assert.That(objects.Count(o => o.Kind == DockerObjectKind.Volume), Is.EqualTo(2));
        }
    }

    [Test]
    public void Parse_MapsFieldsAndInUse()
    {
        var objects = DockerInventory.Parse(Sample);

        var tagged = objects.First(o => o.Kind == DockerObjectKind.Image && o.InUse);
        var untagged = objects.First(o => o.Kind == DockerObjectKind.Image && !o.InUse);
        var volume = objects.First(o => o.Kind == DockerObjectKind.Volume && o.Name == "data-vol");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(tagged.Name, Is.EqualTo("thevsakeeper/twitchtrackerbot:latest"));
            Assert.That(tagged.SizeBytes, Is.EqualTo(1_570_000_000));
            Assert.That(untagged.Name, Is.EqualTo("54c150461e38"));
            Assert.That(volume.InUse, Is.True);
            Assert.That(volume.Id, Is.EqualTo("data-vol"));
        }
    }

    [Test]
    public void Parse_LocalizesAgeAndStatus()
    {
        var objects = DockerInventory.Parse(Sample);

        var image = objects.First(o => o.Kind == DockerObjectKind.Image && o.InUse);
        var container = objects.First(o => o.Kind == DockerObjectKind.Container);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(image.Detail, Is.EqualTo("2 часа назад"));
            Assert.That(container.Detail, Is.EqualTo("Остановлен (137), 2 дня назад"));
        }
    }

    [TestCase("", 0L)]
    [TestCase("0B", 0L)]
    [TestCase("512MB", 512_000_000L)]
    [TestCase("1.57GB", 1_570_000_000L)]
    [TestCase("169.6MB", 169_600_000L)]
    [TestCase("1.401kB", 1401L)]
    [TestCase("2TB", 2_000_000_000_000L)]
    public void DockerSize_ToBytes_ParsesHumanUnits(string human, long expected)
    {
        Assert.That(DockerSize.ToBytes(human), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not json")]
    public void Parse_BadInput_ReturnsEmpty(string input)
    {
        Assert.That(DockerInventory.Parse(input), Is.Empty);
    }
}
