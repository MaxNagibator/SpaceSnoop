using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftRehomeImageTests
{
    [Test]
    public void Альтернативное_имя_системного_корня_из_образа_не_создаёт_цикл()
    {
        var image = new MftVolumeBuilder()
            .Record(
                5,
                new MftRecordBuilder().AsDirectory()
                    .FileName(5, 1, ".")
                    .FileName(20, 1, "повреждённый-корень"))
            .Record(20, new MftRecordBuilder().AsDirectory().FileName(5, 1, "цель"))
            .Record(30, new MftRecordBuilder().FileName(20, 1, "внутри.bin").ResidentData(32))
            .Build(32);

        var table = MftReader.Read(new MftMemoryVolume(image), 'T', 1, 4096, null, CancellationToken.None);
        var links = MftLinks.Build(table, CancellationToken.None);
        var start = MftScanner.Locate(table, links, new(@"T:\цель"), 'T');
        Assert.That(start, Is.EqualTo(20));
        Assert.That(table.Alternates[5].Any(x => x.Parent == 20), Is.True);

        links.Rehome(table, start, CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        links.DropUnreachable(table, cancel.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(table.Entries[5].Parent, Is.EqualTo(5));
            Assert.That(links.FirstChild[start], Is.EqualTo(30));
            Assert.That(table.Entries[30].Exists, Is.True);
            Assert.That(table.Statistics.Rehomed, Is.Zero);
            Assert.That(table.Statistics.Detached, Is.Zero);
        }
    }
}
