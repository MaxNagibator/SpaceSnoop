using SpaceSnoop.Core;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanSummaryTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-summary-" + Guid.NewGuid().ToString("N"));
        var nested = Directory.CreateDirectory(Path.Combine(_root, "nested"));

        File.WriteAllBytes(Path.Combine(_root, "own.bin"), new byte[1024]);
        File.WriteAllBytes(Path.Combine(nested.FullName, "deep.bin"), new byte[4096]);
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
    public void Объём_прогона_считается_по_всему_дереву_а_не_по_файлам_корня()
    {
        var tree = new DiskSpaceCalculator().Calculate(new DirectoryInfo(_root));
        var summary = new ScanSummaryViewModel();

        var run = summary.Apply(tree, TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(run.Bytes, Is.EqualTo(5120));
            Assert.That(run.BytesPerSecond, Is.EqualTo(2560).Within(1));
        });
    }

    [Test]
    public void Скорость_итога_меряется_файлами_а_не_байтами_в_секунду()
    {
        var tree = new DiskSpaceCalculator().Calculate(new DirectoryInfo(_root));
        var summary = new ScanSummaryViewModel();

        summary.Apply(tree, TimeSpan.FromSeconds(2));

        Assert.That(summary.ResultRateText, Is.EqualTo("1 файл/с"));
    }
}
