using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

public class ProfileBatchTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_batch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
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
    public void Подпапки_источника_дают_пары_с_зеркальным_приёмником()
    {
        var source = Make("src", "A", "B");
        var dest = Path.Combine(_root, "dst");

        var rows = ProfileBatch.BuildPairs(source, dest, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(row => row.Name), Is.EquivalentTo(new[] { "A", "B" }));
            Assert.That(rows.All(row => row.Right.StartsWith(dest, StringComparison.Ordinal)), Is.True);
            Assert.That(rows.All(row => row.DestWillBeCreated), Is.True);
            Assert.That(rows.All(row => row.Include), Is.True);
        }
    }

    [Test]
    public void Совпадающие_родители_отбрасывают_перекрывающиеся_пары()
    {
        var source = Make("src", "A");

        var rows = ProfileBatch.BuildPairs(source, source, []);

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void Существующий_подкаталог_приёмника_не_помечается_к_созданию()
    {
        var source = Make("src", "A");
        var dest = Make("dst", "A");

        var row = ProfileBatch.BuildPairs(source, dest, []).Single();

        Assert.That(row.DestWillBeCreated, Is.False);
    }

    [Test]
    public void Существующий_профиль_помечает_пару_как_дубль_и_снимает_галочку()
    {
        var source = Make("src", "A");
        var dest = Path.Combine(_root, "dst");
        var existing = new SyncProfile { Left = Path.Combine(source, "A"), Right = Path.Combine(dest, "A") };

        var row = ProfileBatch.BuildPairs(source, dest, [existing]).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.AlreadyExists, Is.True);
            Assert.That(row.Include, Is.False);
        }
    }

    private string Make(string name, params string[] subdirectories)
    {
        var parent = Path.Combine(_root, name);
        Directory.CreateDirectory(parent);

        foreach (var sub in subdirectories)
        {
            Directory.CreateDirectory(Path.Combine(parent, sub));
        }

        return parent;
    }
}
