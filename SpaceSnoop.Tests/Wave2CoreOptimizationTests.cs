using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Core.Export;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace SpaceSnoop.Tests;

[TestFixture]
public class Wave2CoreOptimizationTests
{
    private readonly List<string> _junctions = [];
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopWave2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var junction in _junctions)
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction, false);
            }
        }

        _junctions.Clear();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void DuplicateFinder_UniqueSizesAndSingleDuplicatePair_SkipsSingletonsWithoutLosingGroup()
    {
        for (var i = 0; i < 40; i++)
        {
            WriteBytes($"unique_{i}.bin", 100 + i, (byte)(i + 1));
        }

        var payload = new byte[4096];
        Array.Fill(payload, (byte)'d');
        File.WriteAllBytes(Path.Combine(_tempDir, "dup1.bin"), payload);
        File.WriteAllBytes(Path.Combine(_tempDir, "dup2.bin"), payload);

        var tree = BuildTree();
        var bySize = new Dictionary<long, List<FileSpace>>();
        var unreadable = DuplicateFinder.Collect(tree, 1, bySize, CancellationToken.None);
        var report = new DuplicateFinder().Find(tree, DuplicateOptions.Default, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unreadable, Is.Zero);
            Assert.That(bySize, Has.Count.EqualTo(41));
            Assert.That(bySize[4096], Has.Count.EqualTo(2));
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Members, Has.Count.EqualTo(2));
            Assert.That(report.ReclaimableBytes, Is.EqualTo(4096));
            Assert.That(report.Examined, Is.EqualTo(2));
        }
    }

    [TestCase(3)]
    [TestCase(5)]
    [TestCase(8)]
    public void DuplicateFinder_MediumBucket_SamePrefixDifferentTail_RequiresFullByteComparison(int count)
    {
        const int prefixBytes = 70 * 1024;
        var prefix = new byte[prefixBytes];
        Array.Fill(prefix, (byte)'p');

        for (var i = 0; i < count; i++)
        {
            var data = new byte[prefixBytes + 512];
            prefix.CopyTo(data, 0);
            Array.Fill(data, (byte)('a' + i), prefixBytes, 512);
            File.WriteAllBytes(Path.Combine(_tempDir, $"same_head_{i}.bin"), data);
        }

        var reports = new List<OperationProgress>();
        var report = new DuplicateFinder().Find(BuildTree(), DuplicateOptions.Default, new InlineProgress(reports.Add), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Is.Empty);
            Assert.That(report.Examined, Is.EqualTo(count));
            Assert.That(reports, Is.Not.Empty);
            Assert.That(reports[^1].Completed, Is.EqualTo(count));
        }
    }

    [TestCase(3)]
    [TestCase(8)]
    public void DuplicateFinder_MediumBucket_MixedPrefixesAndRealCopies_GroupsOnlyTrueMatches(int totalInBucket)
    {
        const int size = 16 * 1024;
        var dupPayload = new byte[size];
        Array.Fill(dupPayload, (byte)'x');

        File.WriteAllBytes(Path.Combine(_tempDir, "match_a.bin"), dupPayload);
        File.WriteAllBytes(Path.Combine(_tempDir, "match_b.bin"), dupPayload);

        for (var i = 2; i < totalInBucket; i++)
        {
            WriteBytes($"distinct_{i}.bin", size, (byte)(10 + i));
        }

        var report = new DuplicateFinder().Find(BuildTree(), DuplicateOptions.Default, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(report.Groups, Has.Count.EqualTo(1));
            Assert.That(report.Groups[0].Members, Has.Count.EqualTo(2));
            Assert.That(report.Groups[0].DistinctFiles, Is.EqualTo(2));
            Assert.That(report.ReclaimableBytes, Is.EqualTo(size));
            Assert.That(report.Examined, Is.EqualTo(totalInBucket));
        }
    }

    [Test]
    public void ReparsePoint_Win32FindData_HasExactLayoutAndReadsTags()
    {
        var normalFile = Path.Combine(_tempDir, "normal.txt");
        File.WriteAllText(normalFile, "payload");

        var target = Directory.CreateDirectory(Path.Combine(_tempDir, "target")).FullName;
        var junction = Path.Combine(_tempDir, "junction");
        var created = TryCreateJunction(junction, target);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(Marshal.SizeOf<ReparsePoint.Win32FindData>(), Is.EqualTo(592));
            Assert.That(Marshal.OffsetOf<ReparsePoint.Win32FindData>(nameof(ReparsePoint.Win32FindData.dwFileAttributes)).ToInt32(), Is.Zero);
            Assert.That(Marshal.OffsetOf<ReparsePoint.Win32FindData>(nameof(ReparsePoint.Win32FindData.dwReserved0)).ToInt32(), Is.EqualTo(36));
            Assert.That(ReparsePoint.TryGetTag(normalFile, out var fileTag), Is.False);
            Assert.That(fileTag, Is.Zero);
        }

        if (!created)
        {
            Assert.Ignore("Не удалось создать junction в тестовой среде.");
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ReparsePoint.TryGetTag(junction, out var junctionTag), Is.True);
            Assert.That(junctionTag, Is.EqualTo(0xA000_0003u));
            Assert.That(ReparsePoint.IsLink(junction), Is.True);
        }
    }

    [Test]
    public void FullScanExport_NestedDirectoriesAndUnicodeNames_WritesExactRelativePathsAndTotals()
    {
        var alpha = Directory.CreateDirectory(Path.Combine(_tempDir, "alpha")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(alpha, "вложенная")).FullName;
        var leaf = Directory.CreateDirectory(Path.Combine(nested, "leaf")).FullName;
        var empty = Directory.CreateDirectory(Path.Combine(_tempDir, "empty")).FullName;
        _ = empty;

        File.WriteAllBytes(Path.Combine(_tempDir, "root.bin"), new byte[120]);
        File.WriteAllBytes(Path.Combine(alpha, "a.bin"), new byte[300]);
        File.WriteAllBytes(Path.Combine(leaf, "deep.bin"), new byte[580]);

        var tree = BuildTree();
        using var stream = new MemoryStream();
        var progressValues = new List<int>();
        FullScanExport.WriteJson(
            stream,
            tree,
            _tempDir,
            new(ScanExport.DefaultDepth, true, 4),
            "2.8.90",
            null,
            new InlineIntProgress(progressValues.Add),
            CancellationToken.None);

        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(stream.ToArray()));
        var root = doc.RootElement;
        var dirs = root.GetProperty("directories").EnumerateArray().ToArray();
        var files = root.GetProperty("files").EnumerateArray().ToArray();
        var totals = root.GetProperty("totals");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                dirs.Select(static x => (x.GetProperty("path").GetString()!, x.GetProperty("depth").GetInt32())),
                Is.EqualTo(
                    new[]
                    {
                        ("alpha", 1),
                        (Path.Combine("alpha", "вложенная"), 2),
                        (Path.Combine("alpha", "вложенная", "leaf"), 3),
                        ("empty", 1),
                    }));

            Assert.That(
                files.Select(static x => (x.GetProperty("path").GetString()!, x.GetProperty("bytes").GetInt64())),
                Is.EquivalentTo(
                    new[]
                    {
                        ("root.bin", 120L),
                        (Path.Combine("alpha", "a.bin"), 300L),
                        (Path.Combine("alpha", "вложенная", "leaf", "deep.bin"), 580L),
                    }));

            Assert.That(totals.GetProperty("bytes").GetInt64(), Is.EqualTo(1000L));
            Assert.That(totals.GetProperty("files").GetInt32(), Is.EqualTo(3));
            Assert.That(totals.GetProperty("directories").GetInt32(), Is.EqualTo(4));
            Assert.That(totals.GetProperty("errors").GetInt32(), Is.Zero);
            Assert.That(progressValues[^1], Is.EqualTo(7));
        }
    }

    [Test]
    public void DirectoryComparer_PreservesHiddenSystemFilesAndContinuesAfterAccessError()
    {
        var left = Directory.CreateDirectory(Path.Combine(_tempDir, "left")).FullName;
        var right = Directory.CreateDirectory(Path.Combine(_tempDir, "right")).FullName;

        var hiddenPath = Path.Combine(left, "secret.sys");
        File.WriteAllText(hiddenPath, "hidden-system");
        File.SetAttributes(hiddenPath, FileAttributes.Hidden | FileAttributes.System);

        var hiddenDir = Directory.CreateDirectory(Path.Combine(left, ".hiddenDir")).FullName;
        File.SetAttributes(hiddenDir, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System);
        File.WriteAllText(Path.Combine(hiddenDir, "inside.txt"), "ok");

        var comparer = new DirectoryComparer(new("*.ignored"), NullLogger<DirectoryComparer>.Instance);
        File.WriteAllText(Path.Combine(left, "drop.ignored"), "skip");
        var basic = comparer.Compare(left, right, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(basic.Root.Files.Select(static f => f.Name), Is.EqualTo(new[] { "secret.sys" }));
            Assert.That(basic.Root.SubDirectories.Select(static d => d.Name), Is.EqualTo(new[] { ".hiddenDir" }));
            Assert.That(basic.Root.SubDirectories[0].Files.Select(static f => f.Name), Is.EqualTo(new[] { "inside.txt" }));
        }

        var locked = Directory.CreateDirectory(Path.Combine(left, "locked")).FullName;
        TestAcl.DenyEnumeration(locked);

        try
        {
            if (Readable(locked))
            {
                Assert.Ignore("Deny-ACE не действует на процесс администратора.");
            }

            var withError = comparer.Compare(left, right, CancellationToken.None);
            var lockedNode = withError.Root.SubDirectories.Single(static d => d.Name == "locked");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(lockedNode.LeftIncomplete, Is.True);
                Assert.That(withError.Root.Files.Select(static f => f.Name), Is.EqualTo(new[] { "secret.sys" }));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(locked);
        }
    }

    [Test]
    public void DirectoryComparer_TracksSkippedLinks_DetectsLinkConflicts_AndAppliesCaseRules()
    {
        var left = Directory.CreateDirectory(Path.Combine(_tempDir, "case-left")).FullName;
        var right = Directory.CreateDirectory(Path.Combine(_tempDir, "case-right")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(_tempDir, "outside-target")).FullName;

        File.WriteAllText(Path.Combine(left, "item.txt"), "left");
        File.WriteAllText(Path.Combine(right, "Item.txt"), "right");

        var comparer = new DirectoryComparer(new(""), NullLogger<DirectoryComparer>.Instance);
        var mixed = new PathCaseRules(StringComparer.Ordinal, StringComparer.OrdinalIgnoreCase);
        var mixedResult = comparer.Compare(left, right, CancellationToken.None, caseRules: mixed);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mixedResult.Root.Files, Has.Count.EqualTo(2));
            Assert.That(mixedResult.Root.Files.Select(static f => f.TypeConflict), Is.All.EqualTo(FileTypeConflict.CaseCollision));
        }

        var soloJunction = Path.Combine(left, "solo-link");
        var conflictJunction = Path.Combine(left, "shared-name");
        Directory.CreateDirectory(Path.Combine(right, "shared-name"));

        if (!TryCreateJunction(soloJunction, outside) || !TryCreateJunction(conflictJunction, outside))
        {
            Assert.Ignore("Не удалось создать junction в тестовой среде.");
        }

        var linkResult = comparer.Compare(left, right, CancellationToken.None);
        var conflictRow = linkResult.Root.Files.Single(static f => f.Name == "shared-name");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(linkResult.SkippedLinks(), Is.EquivalentTo(new[] { "shared-name", "solo-link" }));
            Assert.That(conflictRow.Status, Is.EqualTo(ComparisonStatus.Conflict));
            Assert.That(conflictRow.TypeConflict, Is.EqualTo(FileTypeConflict.LeftLinkRightObject));
            Assert.That(linkResult.Root.SubDirectories, Is.Empty);
        }
    }

    private void WriteBytes(string name, int size, byte fill)
    {
        var data = new byte[size];
        Array.Fill(data, fill);
        File.WriteAllBytes(Path.Combine(_tempDir, name), data);
    }

    private DirectorySpace BuildTree()
    {
        var info = new DirectoryInfo(_tempDir);
        var tree = new DiskSpaceCalculator().Calculate(info, CancellationToken.None);
        tree.FixAbsolutePath(info);
        return tree;
    }

    private bool TryCreateJunction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);
        process?.WaitForExit();

        if (process is not { ExitCode: 0 })
        {
            return false;
        }

        _junctions.Add(link);
        return true;
    }

    private static bool Readable(string path)
    {
        try
        {
            Directory.EnumerateFileSystemEntries(path).Any();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private sealed class InlineIntProgress(Action<int> report) : IProgress<int>
    {
        public void Report(int value)
        {
            report(value);
        }
    }
}
