using SpaceSnoop.Core;
using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Tests;

[TestFixture]
public class PathCaseTests
{
    private string _tempDir = null!;

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

    [Test]
    public void IsCaseSensitive_OnEmptyDirectoryWithCaseSensitiveFlag_ReadsFlag()
    {
        Assume.That(OperatingSystem.IsWindows());
        Assume.That(TryEnableCaseSensitivity(_tempDir), "fsutil не включает регистрозависимость каталога на этой машине");

        Assert.That(PathCase.IsCaseSensitive(_tempDir), Is.True);
    }

    [Test]
    public void IsCaseSensitive_OnMissingDirectory_FallsBackToPlatform()
    {
        var missing = Path.Combine(_tempDir, "missing");

        Assert.That(PathCase.IsCaseSensitive(missing), Is.False);
    }

    [Test]
    public void IsCaseSensitive_OnWindowsVolume_IsFalse()
    {
        Assume.That(OperatingSystem.IsWindows());
        File.WriteAllText(Path.Combine(_tempDir, "Probe.txt"), "payload");

        Assert.That(PathCase.IsCaseSensitive(_tempDir), Is.False);
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

    private static bool TryEnableCaseSensitivity(string directory)
    {
        try
        {
            using var process = Process.Start(
                new ProcessStartInfo("fsutil", ["file", "setCaseSensitiveInfo", directory, "enable"])
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(10_000);

            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }
}
