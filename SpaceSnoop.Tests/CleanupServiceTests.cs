using SpaceSnoop.Core.Cleanup;
using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop.Tests;

[TestFixture]
public class CleanupServiceTests
{
    private string _tempDir = null!;
    private string _targetDir = null!;

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

    [Test]
    public void Clean_CancelledAfterLastFileStillReportsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var target = Target() with
        {
            Path = Path.Combine(_targetDir, "nested"),
        };

        var progress = new InlineProgress(_ => cts.Cancel());

        var report = new CleanupService().Clean(target, progress, cts.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.Cancelled, Is.True);
        }
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
    public void Clean_DirectoryHeldByFreshFileIsNotReportedAsError()
    {
        var nested = Path.Combine(_targetDir, "nested");
        File.WriteAllText(Path.Combine(nested, "fresh.tmp"), "new");

        var report = new CleanupService().Clean(Target(TimeSpan.FromHours(24)), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(nested), Is.True);
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Skipped, Is.Zero);
        }
    }

    [Test]
    public void Clean_EmptiedSubdirectoryRemovedRootKept()
    {
        var report = new CleanupService().Clean(Target(), null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Directory.Exists(Path.Combine(_targetDir, "nested")), Is.False);
            Assert.That(Directory.Exists(_targetDir), Is.True);
            Assert.That(report.Errors, Is.Empty);
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
    public void Clean_VanishedFileAfterWalkDistinguishedFromChangedAndAccessError()
    {
        var vanished = Path.Combine(_targetDir, "nested", "b.tmp");
        var changed = Path.Combine(_targetDir, "nested", "c.tmp");
        var locked = Path.Combine(_targetDir, "nested", "d.tmp");
        WriteAged(changed, "charlie");
        WriteAged(locked, "delta");

        using var lockStream = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None);

        var triggered = false;
        var progress = new InlineProgress(_ =>
        {
            if (triggered)
            {
                return;
            }

            triggered = true;
            File.Delete(vanished);
            File.WriteAllText(changed, "charlie-rewritten");
        });

        var report = new CleanupService().Clean(Target(), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.FreedBytes, Is.EqualTo("alpha".Length));
            Assert.That(report.Skipped, Is.EqualTo(2));
            Assert.That(report.Errors, Has.Count.EqualTo(2));
            Assert.That(report.Errors, Has.None.Contains(vanished));
            Assert.That(report.Errors, Has.Some.Contains(changed).And.Some.Contains("изменился после обхода"));
            Assert.That(report.Errors.Any(e => e.Contains(locked, StringComparison.OrdinalIgnoreCase) && !e.Contains("изменился после обхода", StringComparison.Ordinal)), Is.True);
        }
    }

    [Test]
    public void Clean_VanishedFileInEmptyRunLeavesZeroErrorsAndZeroSkipped()
    {
        var vanished = Path.Combine(_targetDir, "nested", "b.tmp");
        var triggered = false;
        var progress = new InlineProgress(_ =>
        {
            if (!triggered)
            {
                triggered = true;
                File.Delete(vanished);
            }
        });

        var report = new CleanupService().Clean(Target(), progress, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Deleted, Is.EqualTo(1));
            Assert.That(report.FreedBytes, Is.EqualTo("alpha".Length));
            Assert.That(report.Skipped, Is.Zero);
            Assert.That(report.Errors, Is.Empty);
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
    public void Measure_CancelledThrows()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => new CleanupService().Measure(Target(), cts.Token));
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
    public void Measure_ParentJunctionRefusedAsUnsafe()
    {
        var link = Path.Combine(_tempDir, "link");

        if (!TryCreateJunction(link, _targetDir))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var measurement = new CleanupService().Measure(
            Target() with
            {
                Path = Path.Combine(link, "nested"),
            },
            CancellationToken.None);

        Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
    }

    [Test]
    public void Measure_RootJunctionRefusedAsUnsafe()
    {
        var link = Path.Combine(_tempDir, "link");

        if (!TryCreateJunction(link, _targetDir))
        {
            Assert.Ignore("не удалось создать junction");
        }

        var measurement = new CleanupService().Measure(
            Target() with
            {
                Path = link,
            },
            CancellationToken.None);

        Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
    }

    [Test]
    public void Measure_UnreadableSubdirectoryReported()
    {
        var closed = Path.Combine(_targetDir, "closed");
        Directory.CreateDirectory(closed);
        TestAcl.DenyEnumeration(closed);

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
            TestAcl.AllowEnumeration(closed);
        }
    }

    [Test]
    public void Measure_UnsupportedTargetStillSized()
    {
        var measurement = new CleanupService().Measure(Target(supported: false), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsupported));
            Assert.That(measurement.Files, Is.EqualTo(2));
            Assert.That(measurement.Bytes, Is.EqualTo(16));
        }
    }

    [Test]
    public void Measure_VolumeRootRefusedAsUnsafe()
    {
        var target = Target() with
        {
            Path = Path.GetPathRoot(_targetDir)!,
        };

        var measurement = new CleanupService().Measure(target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
            Assert.That(measurement.Files, Is.Zero);
        }
    }

    [Test]
    public void Catalog_ContainsExpectedTargetsAndPaths()
    {
        var age = TimeSpan.FromHours(12);
        var catalog = CleanupCatalog.BuildDefault(age);
        var ids = catalog.Select(static x => x.Id).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ids,
                Is.EqualTo(
                [
                    "TempFiles",
                    "SystemTemp",
                    "WindowsUpdate",
                    "DeliveryOptimization",
                    "Prefetch",
                    "Thumbnails",
                    "DirectXShaderCache",
                    "GpuShaderCache",
                    "RecycleBin",
                    "ErrorReports",
                    "WindowsErrorReporting",
                    "LiveKernelReports",
                    "OldWindowsInstallation",
                ]));

            var gpu = catalog.Single(static x => x.Id == "GpuShaderCache");
            var wer = catalog.Single(static x => x.Id == "WindowsErrorReporting");
            var kernel = catalog.Single(static x => x.Id == "LiveKernelReports");

            Assert.That(gpu.Paths, Has.Count.EqualTo(11));
            Assert.That(gpu.Path, Is.EqualTo(gpu.Paths[0]));
            Assert.That(gpu.MinimumAge, Is.EqualTo(age));
            Assert.That(wer.Paths, Has.Count.EqualTo(3));
            Assert.That(kernel.SearchPattern, Is.EqualTo("*.dmp"));
        }
    }

    [Test]
    public void MultiPath_MeasuresAndCleansExistingDirectoriesAndResolvesFirstExistingPath()
    {
        var missing = Path.Combine(_targetDir, "missing");
        var first = Path.Combine(_targetDir, "first");
        var second = Path.Combine(_targetDir, "second");
        var sub = Path.Combine(second, "nested");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(sub);
        WriteAged(Path.Combine(first, "a.bin"), "12345");
        WriteAged(Path.Combine(sub, "b.bin"), "1234567");

        var target = new CleanupTarget
        {
            Id = "Multi",
            Name = "Многопутевая цель",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Paths = [missing, first, second],
            MinimumAge = TimeSpan.Zero,
        };

        var service = new CleanupService();
        var measurement = service.Measure(target, CancellationToken.None);
        var report = service.Clean(target, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Path, Is.EqualTo(missing));
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Available));
            Assert.That(measurement.ResolvedPath, Is.EqualTo(first));
            Assert.That(measurement.Files, Is.EqualTo(2));
            Assert.That(measurement.Bytes, Is.EqualTo(12));
            Assert.That(report.Deleted, Is.EqualTo(2));
            Assert.That(report.FreedBytes, Is.EqualTo(12));
            Assert.That(report.Skipped, Is.Zero);
            Assert.That(Directory.Exists(first), Is.True);
            Assert.That(Directory.Exists(second), Is.True);
            Assert.That(Directory.Exists(sub), Is.False);
        }
    }

    [Test]
    public void MultiPath_UnsafeCandidateRefusesEntireTarget()
    {
        var safe = Path.Combine(_targetDir, "safe");
        Directory.CreateDirectory(safe);
        var file = Path.Combine(safe, "keep.bin");
        WriteAged(file, "123456");

        var target = new CleanupTarget
        {
            Id = "MultiUnsafe",
            Name = "Небезопасный кандидат",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Paths = [safe, Path.GetPathRoot(_targetDir)!],
            MinimumAge = TimeSpan.Zero,
        };

        var service = new CleanupService();
        var measurement = service.Measure(target, CancellationToken.None);
        var report = service.Clean(target, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
            Assert.That(measurement.Files, Is.Zero);
            Assert.That(report.Deleted, Is.Zero);
            Assert.That(report.Skipped, Is.EqualTo(1));
            Assert.That(File.Exists(file), Is.True);
        }
    }

    [Test]
    public void MultiPath_DeduplicatesCanonicalPaths()
    {
        var dir = Path.Combine(_targetDir, "dup");
        Directory.CreateDirectory(dir);
        WriteAged(Path.Combine(dir, "once.bin"), "1234");

        var target = new CleanupTarget
        {
            Id = "MultiDup",
            Name = "Дубли путей",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Paths = [dir, dir + Path.DirectorySeparatorChar],
            MinimumAge = TimeSpan.Zero,
        };

        var measurement = new CleanupService().Measure(target, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Available));
            Assert.That(measurement.Files, Is.EqualTo(1));
            Assert.That(measurement.Bytes, Is.EqualTo(4));
        }
    }

    [Test]
    public void MultiPath_UnreadableCandidateReportedInMeasurementAndClean()
    {
        var open = Path.Combine(_targetDir, "open");
        var closed = Path.Combine(_targetDir, "closed");
        Directory.CreateDirectory(open);
        Directory.CreateDirectory(closed);
        WriteAged(Path.Combine(open, "ok.bin"), "1234");
        TestAcl.DenyEnumeration(closed);

        try
        {
            var service = new CleanupService();
            var mixed = new CleanupTarget
            {
                Id = "MultiUnreadable",
                Name = "Частично закрытая цель",
                Description = "Тест",
                Kind = CleanupTargetKind.Directory,
                Paths = [closed, open],
                MinimumAge = TimeSpan.Zero,
            };

            var measurement = service.Measure(mixed, CancellationToken.None);
            var report = service.Clean(mixed, null, CancellationToken.None);

            var allClosed = new CleanupTarget
            {
                Id = "AllClosed",
                Name = "Полностью закрытая цель",
                Description = "Тест",
                Kind = CleanupTargetKind.Directory,
                Paths = [closed],
                MinimumAge = TimeSpan.Zero,
            };

            var deniedMeasurement = service.Measure(allClosed, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Available));
                Assert.That(measurement.ResolvedPath, Is.EqualTo(open));
                Assert.That(measurement.Files, Is.EqualTo(1));
                Assert.That(measurement.Unreadable, Does.Contain(closed));
                Assert.That(report.Deleted, Is.EqualTo(1));
                Assert.That(report.Skipped, Is.EqualTo(1));
                Assert.That(report.Errors, Has.Some.Contains(closed));
                Assert.That(deniedMeasurement.Availability, Is.EqualTo(CleanupAvailability.NeedsAdmin));
                Assert.That(deniedMeasurement.ResolvedPath, Is.EqualTo(closed));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(closed);
        }
    }

    [Test]
    public void MultiPath_OverlappingParentAndChildRootsRefusedAsUnsafe()
    {
        var parent = Path.Combine(_tempDir, "overlap_parent");
        var child = Path.Combine(parent, "child");
        Directory.CreateDirectory(child);
        var file = Path.Combine(child, "item.bin");
        WriteAged(file, "1234");

        var service = new CleanupService();

        var parentFirst = new CleanupTarget
        {
            Id = "OverlapParentFirst",
            Name = "Перекрывающиеся корни",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Paths = [parent, child],
            MinimumAge = TimeSpan.Zero,
        };

        var childFirst = parentFirst with
        {
            Id = "OverlapChildFirst",
            Paths = [child, parent],
        };

        var parentFirstMeasure = service.Measure(parentFirst, CancellationToken.None);
        var parentFirstReport = service.Clean(parentFirst, null, CancellationToken.None);
        var childFirstMeasure = service.Measure(childFirst, CancellationToken.None);
        var childFirstReport = service.Clean(childFirst, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parentFirstMeasure.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
            Assert.That(parentFirstMeasure.Files, Is.Zero);
            Assert.That(parentFirstMeasure.Bytes, Is.Zero);
            Assert.That(parentFirstReport.Deleted, Is.Zero);
            Assert.That(parentFirstReport.Skipped, Is.EqualTo(1));
            Assert.That(childFirstMeasure.Availability, Is.EqualTo(CleanupAvailability.Unsafe));
            Assert.That(childFirstMeasure.Files, Is.Zero);
            Assert.That(childFirstMeasure.Bytes, Is.Zero);
            Assert.That(childFirstReport.Deleted, Is.Zero);
            Assert.That(childFirstReport.Skipped, Is.EqualTo(1));
            Assert.That(Directory.Exists(parent), Is.True);
            Assert.That(Directory.Exists(child), Is.True);
            Assert.That(File.Exists(file), Is.True);
        }
    }

    [TestCase(false, 1, 4, TestName = "Корни Cache и cache в обычном каталоге дедуплицируются без повторного счёта")]
    [TestCase(true, 2, 10, TestName = "Корни Cache и cache в регистрозависимом каталоге измеряются и очищаются раздельно")]
    public void MultiPath_SiblingRootsRespectParentDirectoryCaseSensitivity(bool caseSensitive, int expectedFiles, long expectedBytes)
    {
        var parent = Directory.CreateDirectory(Path.Combine(_tempDir, "cased_roots")).FullName;

        if (caseSensitive)
        {
            Assume.That(TryEnableCaseSensitivity(parent), "fsutil не включает регистрозависимость каталога на этой машине");
        }

        var upper = Directory.CreateDirectory(Path.Combine(parent, "Cache")).FullName;
        var lower = Path.Combine(parent, "cache");
        var upperFile = Path.Combine(upper, "upper.bin");
        WriteAged(upperFile, "1234");

        var lowerFile = Path.Combine(lower, "lower.bin");

        if (caseSensitive)
        {
            Directory.CreateDirectory(lower);
            WriteAged(lowerFile, "123456");
        }

        var target = new CleanupTarget
        {
            Id = "CaseRoots",
            Name = "Регистр корней",
            Description = "Тест",
            Kind = CleanupTargetKind.Directory,
            Paths = [upper, lower],
            MinimumAge = TimeSpan.Zero,
        };

        var service = new CleanupService();
        var measurement = service.Measure(target, CancellationToken.None);
        var report = service.Clean(target, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(measurement.Availability, Is.EqualTo(CleanupAvailability.Available));
            Assert.That(measurement.Files, Is.EqualTo(expectedFiles));
            Assert.That(measurement.Bytes, Is.EqualTo(expectedBytes));
            Assert.That(report.Deleted, Is.EqualTo(expectedFiles));
            Assert.That(report.FreedBytes, Is.EqualTo(expectedBytes));
            Assert.That(report.Skipped, Is.Zero);
            Assert.That(report.Errors, Is.Empty);
            Assert.That(Directory.Exists(upper), Is.True);
            Assert.That(File.Exists(upperFile), Is.False);

            if (caseSensitive)
            {
                Assert.That(Directory.Exists(lower), Is.True);
                Assert.That(File.Exists(lowerFile), Is.False);
            }
        }
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
