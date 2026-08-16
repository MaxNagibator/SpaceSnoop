using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class PathCaseTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string _tempDir = null!;

    [Test]
    public void IsCaseSensitive_OnWindowsVolume_IsFalse()
    {
        Assume.That(OperatingSystem.IsWindows());
        File.WriteAllText(Path.Combine(_tempDir, "Probe.txt"), "payload");

        Assert.That(PathCase.IsCaseSensitive(_tempDir), Is.False);
    }

    [Test]
    public void IsCaseSensitive_OnEmptyDirectory_FallsBackToPlatform()
    {
        Assert.That(PathCase.IsCaseSensitive(_tempDir), Is.EqualTo(PathCase.PlatformDefaultIsCaseSensitive));
    }

    [Test]
    public void IsCaseSensitive_OnMissingDirectory_FallsBackToPlatform()
    {
        var missing = Path.Combine(_tempDir, "missing");

        Assert.That(PathCase.IsCaseSensitive(missing), Is.EqualTo(PathCase.PlatformDefaultIsCaseSensitive));
    }

    [Test]
    public void IsCaseSensitive_WhenNamesDifferOnlyByCase_IsTrue()
    {
        Assume.That(!OperatingSystem.IsWindows());
        File.WriteAllText(Path.Combine(_tempDir, "probe.txt"), "one");
        File.WriteAllText(Path.Combine(_tempDir, "Probe.txt"), "two");

        Assert.That(PathCase.IsCaseSensitive(_tempDir), Is.True);
    }

    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, true, true)]
    public void Stricter_TakesSensitiveWhenEitherSideIsSensitive(bool left, bool right, bool expected)
    {
        var rules = new PathCaseRules(
            left ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase,
            right ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        Assert.That(rules.MatchIsSensitive, Is.EqualTo(expected));
    }
}
