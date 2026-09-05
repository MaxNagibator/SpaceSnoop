using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Schedule;
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
    public void Несуществующий_источник_даёт_Unavailable()
    {
        var profile = new SyncProfile { Left = Path.Combine(_root, "missing"), Right = Make("right") };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    [Test]
    public void Несуществующий_приёмник_при_слева_направо_не_блокирует()
    {
        var profile = new SyncProfile { Left = Make("left"), Right = Path.Combine(_root, "missing"), Mode = 0 };

        Assert.That(OverviewPipeline.Classify(profile), Is.Null);
    }

    [Test]
    public void Пустой_путь_даёт_Unavailable()
    {
        var profile = new SyncProfile { Left = string.Empty, Right = Make("right") };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    [Test]
    public void Тот_же_отсутствующий_каталог_как_источник_справа_налево_даёт_Unavailable()
    {
        var profile = new SyncProfile { Left = Make("left"), Right = Path.Combine(_root, "missing"), Mode = 1 };

        Assert.That(OverviewPipeline.Classify(profile), Is.EqualTo(OverviewRunStatus.Unavailable));
    }

    [TestCase(0, true, false, ExpectedResult = false)]
    [TestCase(0, false, true, ExpectedResult = true)]
    [TestCase(1, false, true, ExpectedResult = false)]
    [TestCase(1, true, false, ExpectedResult = true)]
    [TestCase(2, true, false, ExpectedResult = false)]
    [TestCase(2, false, true, ExpectedResult = false)]
    [TestCase(2, false, false, ExpectedResult = true)]
    public bool SourceMissing_зависит_от_направления(int mode, bool leftExists, bool rightExists)
    {
        var left = leftExists ? Make("left") : Path.Combine(_root, "noleft");
        var right = rightExists ? Make("right") : Path.Combine(_root, "noright");

        return SyncProfile.SourceMissing(left, right, HeadlessSync.MapMode(mode));
    }

    private string Make(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
