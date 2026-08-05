using SpaceSnoop.Core.Cleanup;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;

namespace SpaceSnoop.Tests;

[TestFixture]
public class CleanupServiceTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopCleanup_{Guid.NewGuid():N}");
        _targetDir = Path.Combine(_tempDir, "target");
        Directory.CreateDirectory(Path.Combine(_targetDir, "nested"));
        WriteAged(Path.Combine(_targetDir, "a.tmp"), "alpha");
        WriteAged(Path.Combine(_targetDir, "nested", "b.tmp"), "bravo-bravo");
    }

    [TearDown]
    public void TearDown()
    {
        RemoveTree(_tempDir);
    }

    private static void RemoveTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var directory = new DirectoryInfo(path);

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();
            return;
        }

        foreach (var sub in directory.GetDirectories())
        {
            RemoveTree(sub.FullName);
        }

        directory.Delete(true);
    }

    private string _tempDir = null!;
    private string _targetDir = null!;

    private CleanupTarget Target(TimeSpan? minimumAge = null, bool supported = true)
    {
        return new()
        {
            Id = "Test",
            Name = "Тест",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Path = _targetDir,
            Supported = supported,
            MinimumAge = minimumAge ?? TimeSpan.Zero,
        };
    }

    private static void WriteAged(string path, string content)
    {
        File.WriteAllText(path, content);
        var old = DateTime.UtcNow - TimeSpan.FromDays(7);
        File.SetCreationTimeUtc(path, old);
        File.SetLastWriteTimeUtc(path, old);
    }

    private static bool TryCreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        process.WaitForExit();

        return process.ExitCode == 0;
    }

    private static void DenyEnumeration(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        security.AddAccessRule(new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            AccessControlType.Deny));

        new DirectoryInfo(path).SetAccessControl(security);
    }

    private static void AllowEnumeration(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl();
        security.RemoveAccessRuleAll(new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory | FileSystemRights.ReadData,
            AccessControlType.Deny));

        new DirectoryInfo(path).SetAccessControl(security);
    }

    [Test]
    public void Measure_CountsFilesAndBytes()
    {
        var measurement = new CleanupService().Measure(Target(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Files, Is.EqualTo(2));
            Assert.That(measurement.Bytes, Is.EqualTo(16));
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Available));
        }
    }

    [Test]
    public void Measure_MissingDirectoryReportedInsteadOfThrowing()
    {
        Directory.Delete(_targetDir, true);

        var measurement = new CleanupService().Measure(Target(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Missing));
            Assert.That(measurement.Files, Is.Zero);
        }
    }

    [Test]
    public void Measure_JunctionNotTraversed()
    {
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outside);
        WriteAged(Path.Combine(outside, "victim.txt"), "0123456789");

        if (!TryCreateJunction(Path.Combine(_targetDir, "link"), outside))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var measurement = new CleanupService().Measure(Target(), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Files, Is.EqualTo(2));
            Assert.That(measurement.Bytes, Is.EqualTo(16));
        }
    }

    [Test]
    public void Clean_JunctionTargetSurvives()
    {
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "victim.txt");
        WriteAged(victim, "0123456789");

        if (!TryCreateJunction(Path.Combine(_targetDir, "link"), outside))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var report = new CleanupService().Clean(Target(), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(victim), Is.True);
            Assert.That(report.Deleted, Is.EqualTo(2));
        }
    }

    [Test]
    public void Clean_KeepsFileYoungerThanMinimumAge()
    {
        var fresh = Path.Combine(_targetDir, "fresh.tmp");
        File.WriteAllText(fresh, "new");

        var report = new CleanupService().Clean(Target(TimeSpan.FromHours(24)), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(fresh), Is.True);
            Assert.That(report.Deleted, Is.EqualTo(2));
        }
    }

    [Test]
    public void Measure_UnreadableSubdirectoryReported()
    {
        var closed = Path.Combine(_targetDir, "closed");
        Directory.CreateDirectory(closed);
        DenyEnumeration(closed);

        try
        {
            var measurement = new CleanupService().Measure(Target(), CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(measurement.Unreadable, Does.Contain(closed));
                Assert.That(measurement.Files, Is.EqualTo(2));
            }
        }
        finally
        {
            AllowEnumeration(closed);
        }
    }

    [Test]
    public void Measure_CancelledThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new CleanupService().Measure(Target(), cts.Token));
    }

    [Test]
    public void Clean_CancelledMidDeleteReturnsPartialReport()
    {
        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cts.Cancel());

        var report = new CleanupService().Clean(Target(), progress, cts.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.Cancelled, Is.True);
            Assert.That(report.FreedBytes, Is.GreaterThan(0));
            Assert.That(Directory.GetFiles(_targetDir, "*", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public void Measure_VolumeRootRefusedAsUnsafe()
    {
        var target = Target() with { Path = Path.GetPathRoot(_targetDir)! };

        var measurement = new CleanupService().Measure(target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
            Assert.That(measurement.Files, Is.Zero);
        }
    }

    [Test]
    public void Measure_RootJunctionRefusedAsUnsafe()
    {
        var link = Path.Combine(_tempDir, "link");

        if (!TryCreateJunction(link, _targetDir))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var measurement = new CleanupService().Measure(Target() with { Path = link }, CancellationToken.None);

        Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
    }

    [Test]
    public void Measure_ParentJunctionRefusedAsUnsafe()
    {
        var link = Path.Combine(_tempDir, "link");

        if (!TryCreateJunction(link, _targetDir))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var measurement = new CleanupService().Measure(Target() with { Path = Path.Combine(link, "nested") }, CancellationToken.None);

        Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
    }

    [Test]
    public void Clean_CancelledAfterLastFileStillReportsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var target = Target() with { Path = Path.Combine(_targetDir, "nested") };
        var progress = new InlineProgress(_ => cts.Cancel());

        var report = new CleanupService().Clean(target, progress, cts.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.Cancelled, Is.True);
        }
    }

    [Test]
    public void Clean_FileReplacedAfterWalkSurvives()
    {
        var service = new CleanupService();
        var target = Target();
        var replaced = Path.Combine(_targetDir, "a.tmp");

        var progress = new InlineProgress(_ => File.WriteAllText(Path.Combine(_targetDir, "nested", "b.tmp"), "fresh-and-longer"));

        var report = service.Clean(target, progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(replaced), Is.False);
            Assert.That(File.Exists(Path.Combine(_targetDir, "nested", "b.tmp")), Is.True);
            Assert.That(report.Skipped, Is.EqualTo(1));
            Assert.That(report.Errors, Has.Some.Contains("изменился после обхода"));
        }
    }

    [Test]
    public void Clean_LockedFileCountedAsSkipped()
    {
        using var handle = new FileStream(Path.Combine(_targetDir, "a.tmp"), FileMode.Open, FileAccess.Read, FileShare.None);

        var report = new CleanupService().Clean(Target(), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Skipped, Is.EqualTo(1));
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.FreedBytes, Is.EqualTo(11));
            Assert.That(report.Errors, Is.Not.Empty);
        }
    }

    [Test]
    public void Clean_UnsupportedTargetRefused()
    {
        var report = new CleanupService().Clean(Target(supported: false), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.Zero);
            Assert.That(report.Skipped, Is.EqualTo(1));
            Assert.That(report.Errors, Is.Not.Empty);
            Assert.That(File.Exists(Path.Combine(_targetDir, "a.tmp")), Is.True);
        }
    }

    [Test]
    public void Clean_RemovesEmptiedSubdirectories()
    {
        new CleanupService().Clean(Target(), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(_targetDir, "nested")), Is.False);
            Assert.That(Directory.Exists(_targetDir), Is.True);
        }
    }
}
