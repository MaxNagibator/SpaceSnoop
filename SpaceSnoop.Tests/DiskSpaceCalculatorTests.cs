using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class DiskSpaceCalculatorTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopCalc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), new string('a', 100));
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), new string('b', 200));
        File.WriteAllText(Path.Combine(_tempDir, "c.txt"), new string('c', 300));

        var sub = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "d.txt"), new string('d', 400));
        File.WriteAllText(Path.Combine(sub, "e.txt"), new string('e', 500));

        Directory.CreateDirectory(Path.Combine(_tempDir, "empty"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void Calculate_ComputesExpectedTotals()
    {
        var calculator = new DiskSpaceCalculator();

        var result = calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalFileCount, Is.EqualTo(5));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(2));
        }
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(8)]
    [TestCase(64)]
    public void CalculateMultithreaded_WithDegree_MatchesSingleThreaded(int degree)
    {
        var calculator = new DiskSpaceCalculator();
        var baseline = calculator.Calculate(new(_tempDir), CancellationToken.None);

        var parallel = calculator.CalculateMultithreaded(new(_tempDir), degree, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parallel.TotalSize, Is.EqualTo(baseline.TotalSize));
            Assert.That(parallel.TotalFileCount, Is.EqualTo(baseline.TotalFileCount));
            Assert.That(parallel.TotalDirectoryCount, Is.EqualTo(baseline.TotalDirectoryCount));
        }
    }

    [TestCase(0)]
    [TestCase(-4)]
    public void CalculateMultithreaded_ClampsNonPositiveDegree(int degree)
    {
        var calculator = new DiskSpaceCalculator();
        var baseline = calculator.Calculate(new(_tempDir), CancellationToken.None);

        var parallel = calculator.CalculateMultithreaded(new(_tempDir), degree, CancellationToken.None);

        Assert.That(parallel.TotalSize, Is.EqualTo(baseline.TotalSize));
    }

    [Test]
    public void Calculate_ReportsProgress()
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();

        calculator.Calculate(new(_tempDir), progress, CancellationToken.None);

        var snapshot = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.FilesScanned, Is.EqualTo(5));
            Assert.That(snapshot.BytesScanned, Is.EqualTo(1500));
            Assert.That(snapshot.DirectoriesScanned, Is.EqualTo(3));
            Assert.That(snapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(snapshot.TopLevelCompleted, Is.EqualTo(2));
            Assert.That(snapshot.Fraction, Is.EqualTo(1d));
        }
    }

    [TestCase(1)]
    [TestCase(4)]
    public void CalculateMultithreaded_ReportsProgress(int degree)
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();

        calculator.CalculateMultithreaded(new(_tempDir), degree, progress, CancellationToken.None);

        var snapshot = progress.CreateSnapshot();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(snapshot.FilesScanned, Is.EqualTo(5));
            Assert.That(snapshot.BytesScanned, Is.EqualTo(1500));
            Assert.That(snapshot.DirectoriesScanned, Is.EqualTo(3));
            Assert.That(snapshot.TopLevelTotal, Is.EqualTo(2));
            Assert.That(snapshot.TopLevelCompleted, Is.EqualTo(2));
        }
    }

    [Test]
    public void ScanProgressSnapshot_FractionIsNull_WhenTopLevelUnknown()
    {
        var progress = new ScanProgress();

        Assert.That(progress.CreateSnapshot().Fraction, Is.Null);
    }
}
