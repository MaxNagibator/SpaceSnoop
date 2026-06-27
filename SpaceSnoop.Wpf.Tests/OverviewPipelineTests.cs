using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Overview;

namespace SpaceSnoop.Wpf.Tests;

public class OverviewPipelineTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "ss_overview_" + Guid.NewGuid().ToString("N"));
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
    public void Вложенные_каталоги_дают_Overlap()
    {
        var left = Make("left");
        var nested = Path.Combine(left, "inner");
        Directory.CreateDirectory(nested);
        var profile = new SyncProfile { Left = left, Right = nested };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Overlap));
    }

    [Test]
    public void Два_разных_существующих_каталога_дают_null()
    {
        var profile = new SyncProfile { Left = Make("left"), Right = Make("right") };

        Assert.That(OverviewPipeline.Classify(profile), Is.Null);
    }

    [Test]
    public void Несуществующий_каталог_даёт_Unavailable()
    {
        var profile = new SyncProfile { Left = Path.Combine(_root, "missing"), Right = Make("right") };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    [Test]
    public void Пустой_путь_даёт_Unavailable()
    {
        var profile = new SyncProfile { Left = string.Empty, Right = Make("right") };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
