using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;

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

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_CountsDirectoryItCouldNotRead(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();
        var progress = new ScanProgress();
        var missing = new DirectoryInfo(Path.Combine(_tempDir, "vanished"));

        var result = multithreaded
            ? calculator.CalculateMultithreaded(missing, 4, progress, CancellationToken.None)
            : calculator.Calculate(missing, progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.State, Is.EqualTo(SpaceState.Error));
            Assert.That(progress.CreateSnapshot().DirectoriesFailed, Is.EqualTo(1));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_CountsHiddenAndSystemFiles(bool multithreaded)
    {
        var hidden = Path.Combine(_tempDir, "hidden.txt");
        File.WriteAllText(hidden, new string('h', 700));
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.System);

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalFileCount, Is.EqualTo(6));
            Assert.That(result.TotalSize, Is.EqualTo(2200));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_DoesNotFollowReparsePoint(bool multithreaded)
    {
        var link = Path.Combine(_tempDir, "link");

        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_tempDir, "sub"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("не удалось создать символьную ссылку");
        }

        var calculator = new DiskSpaceCalculator();

        var result = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, CancellationToken.None)
            : calculator.Calculate(new(_tempDir), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(1500));
            Assert.That(result.TotalDirectoryCount, Is.EqualTo(2));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Calculate_ThrowsOnCancellation(bool multithreaded)
    {
        var calculator = new DiskSpaceCalculator();
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => _ = multithreaded
            ? calculator.CalculateMultithreaded(new(_tempDir), 4, source.Token)
            : calculator.Calculate(new(_tempDir), source.Token));
    }

    [Test]
    public void CalculateMultithreaded_KeepsPartialContentOfUnreadableDirectory()
    {
        var calculator = new DiskSpaceCalculator();

        var result = calculator.CalculateMultithreaded(new(Path.Combine(_tempDir, "vanished")), 4, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.State, Is.EqualTo(SpaceState.Error));
            Assert.That(result.TotalSize, Is.Zero);
        }
    }

    [Test]
    public void ScanProgressSnapshot_FractionIsNull_WhenTopLevelUnknown()
    {
        var progress = new ScanProgress();

        Assert.That(progress.CreateSnapshot().Fraction, Is.Null);
    }
}
