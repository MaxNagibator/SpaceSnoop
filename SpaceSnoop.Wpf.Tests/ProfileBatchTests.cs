using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap.Schedule;

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
    public void Перекрывающаяся_пара_остаётся_в_списке_с_причиной_и_не_выбирается()
    {
        var source = Make("src", "A");

        var row = ProfileBatch.BuildPairs(source, source, []).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.Refusal, Is.EqualTo(SyncRoots.OverlapMessage));
            Assert.That(row.IsSelectable, Is.False);
            Assert.That(row.Include, Is.False);
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
            Assert.That(
                rows.Select(row => row.Name),
                Is.EquivalentTo(
                    new[]
                    {
                        "A", "B",
                    }));

            Assert.That(rows.Select(static row => row.Right), Is.EquivalentTo(new[] { Path.Combine(dest, "A"), Path.Combine(dest, "B") }));
            Assert.That(rows.All(row => row.Include), Is.True);
        }
    }

    [TestCase(0, "A,B,C")]
    [TestCase(1, "C,B,A")]
    [TestCase(2, "B,C,A")]
    public void Сортировка_пар_по_режиму(int mode, string expected)
    {
        var rows = new List<BatchPairRow>
        {
            new("B", "l", "r", false),
            new("A", "l", "r", true),
            new("C", "l", "r", false),
        };

        var sorted = ProfileBatch.Sort(rows, mode);

        Assert.That(string.Join(",", sorted.Select(row => row.Name)), Is.EqualTo(expected));
    }

    [Test]
    public void Существующий_профиль_помечает_пару_как_дубль_и_снимает_галочку()
    {
        var source = Make("src", "A");
        var dest = Path.Combine(_root, "dst");
        var existing = new SyncProfile
        {
            Left = Path.Combine(source, "A"),
            Right = Path.Combine(dest, "A"),
        };

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
