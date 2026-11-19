using Microsoft.Win32.SafeHandles;
using SpaceSnoop.Core;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ArchiveServiceTests
{
    private string _tempDir = null!;
    private string _sourceDir = null!;
    private string _zipPath = null!;
    private long _sizeAtCancel;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopArchive_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "nested"));
        File.WriteAllText(Path.Combine(_sourceDir, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(_sourceDir, "nested", "b.txt"), "bravo-bravo");
        _zipPath = Path.Combine(_tempDir, "out.zip");
    }

    [TearDown]
    public void TearDown()
    {
        RemoveTree(_tempDir);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Удаление_покрытого_архивом_каталога_сохраняет_добавленные_или_переписанные_файлы(bool rewrite)
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        var stats = service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var item = Path.Combine(_sourceDir, rewrite ? "a.txt" : "new.txt");

        Assert.Throws<IOException>(() => service.VerifyCoverageAndDeleteSource(
            _sourceDir, _zipPath, stats, null, null, static () => { }, null,
            () => File.WriteAllText(item, "delta"), CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(item), Is.EqualTo("delta"));
            Assert.That(File.Exists(_zipPath), Is.True);
        }
    }

    [Test]
    public void CheckContent_CancelledThrowsInsteadOfReportingVerdict()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var index = ArchiveIndex.Read(_zipPath, StringComparer.OrdinalIgnoreCase);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => service.CheckContent(_sourceDir, content, index, cts.Token));
    }

    [TestCase(FileAccess.Read, FileShare.None)]
    [TestCase(FileAccess.Write, FileShare.ReadWrite)]
    public void CheckContent_FailsWhenFileHeldByAnotherHandle(FileAccess access, FileShare share)
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var index = ArchiveIndex.Read(_zipPath, StringComparer.OrdinalIgnoreCase);

        var held = Path.Combine(_sourceDir, "a.txt");

        using (new FileStream(held, FileMode.Open, access, share))
        {
            var coverage = service.CheckContent(_sourceDir, content, index, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(coverage.Ok, Is.False);
                Assert.That(coverage.Detail, Does.StartWith($"файл не прочитан для сверки с архивом «{held}»"));
            }
        }

        Assert.That(service.CheckContent(_sourceDir, content, index, CancellationToken.None).Ok, Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CheckCoverage_CollectedEmptyDirectoriesDifferOnlyInCase_RefusesCoverage(bool caseSensitive)
    {
        var service = new ArchiveService();
        var hollow = Path.Combine(_sourceDir, "Hollow");
        var twin = Path.Combine(_sourceDir, "hollow");
        Directory.CreateDirectory(hollow);
        WriteSingleEntryArchive("hollow/", string.Empty, DateTime.Now);

        var index = ArchiveIndex.Read(_zipPath, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var coverage = service.CheckCoverage(_sourceDir, new([], [hollow, twin], [], 0), index);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(
                coverage.Detail,
                Is.EqualTo(
                    caseSensitive
                        ? $"в архив не попал пустой каталог «{hollow}»"
                        : $"в архиве не отличить по регистру путь «{twin}»"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CheckCoverage_CollectedFilesDifferOnlyInCase_RefusesCoverage(bool caseSensitive)
    {
        var service = new ArchiveService();
        var file = Path.Combine(_sourceDir, "Foo.txt");
        var twin = Path.Combine(_sourceDir, "foo.txt");
        File.WriteAllText(file, "alpha");
        WriteSingleEntryArchive("foo.txt", "alpha", File.GetLastWriteTime(file));

        var index = ArchiveIndex.Read(_zipPath, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var coverage = service.CheckCoverage(_sourceDir, Content(file, twin), index);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(
                coverage.Detail,
                Is.EqualTo(
                    caseSensitive
                        ? $"в архив не попал файл «{file}»"
                        : $"в архиве не отличить по регистру путь «{twin}»"));
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void CheckCoverage_EntryDiffersFromEmptyDirectoryOnlyInCase_CoveredOnlyUnderCaseInsensitivePolicy(bool caseSensitive, bool covered)
    {
        var service = new ArchiveService();
        var hollow = Path.Combine(_sourceDir, "Hollow");
        Directory.CreateDirectory(hollow);
        WriteSingleEntryArchive("hollow/", string.Empty, DateTime.Now);

        var index = ArchiveIndex.Read(_zipPath, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var coverage = service.CheckCoverage(_sourceDir, new([], [hollow], [], 0), index);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.EqualTo(covered));
            Assert.That(coverage.Detail, Is.EqualTo(covered ? string.Empty : $"в архив не попал пустой каталог «{hollow}»"));
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void CheckCoverage_EntryDiffersFromFileOnlyInCase_CoveredOnlyUnderCaseInsensitivePolicy(bool caseSensitive, bool covered)
    {
        var service = new ArchiveService();
        var file = Path.Combine(_sourceDir, "Foo.txt");
        File.WriteAllText(file, "alpha");
        WriteSingleEntryArchive("foo.txt", "alpha", File.GetLastWriteTime(file));

        var index = ArchiveIndex.Read(_zipPath, caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
        var coverage = service.CheckCoverage(_sourceDir, Content(file), index);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.EqualTo(covered));
            Assert.That(coverage.Detail, Is.EqualTo(covered ? string.Empty : $"в архив не попал файл «{file}»"));
        }
    }

    [Test]
    public void Collect_EmptyDirectorySurvivesRoundTrip()
    {
        var service = new ArchiveService();
        var hollow = Path.Combine(_sourceDir, "hollow");
        Directory.CreateDirectory(hollow);

        var content = service.Collect(_sourceDir, CancellationToken.None);
        var stats = service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                content.EmptyDirectories,
                Is.EqualTo(
                    new[]
                    {
                        hollow,
                    }));

            Assert.That(content.Files, Has.Count.EqualTo(2));
            Assert.That(stats.Count, Is.EqualTo(3));
            Assert.That(service.VerifyZip(_zipPath, stats, true).Ok, Is.True);
            Assert.That(service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None).Ok, Is.True);
        }

        using var zip = ZipFile.OpenRead(_zipPath);
        Assert.That(zip.GetEntry("hollow/"), Is.Not.Null);
    }

    [Test]
    public void VerifyCoverage_AcceptsDirectoryThatLostItsFilesAfterPacking()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        foreach (var file in Directory.GetFiles(Path.Combine(_sourceDir, "nested")))
        {
            File.Delete(file);
        }

        Assert.That(service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None).Ok, Is.True);
    }

    [Test]
    public void VerifyCoverage_CancelledThrowsInsteadOfReportingVerdict()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => service.VerifyCoverage(_sourceDir, _zipPath, cts.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_zipPath), Is.True);
            Assert.That(Directory.Exists(_sourceDir), Is.True);
        }
    }

    [Test]
    public void VerifyCoverage_ReportsMonotonicFileAndByteProgressAndTotals()
    {
        var largeFile = Path.Combine(_sourceDir, "a.txt");
        WriteIncompressible(largeFile, 3 * 1024 * 1024);
        var expectedBytes = new FileInfo(largeFile).Length + new FileInfo(Path.Combine(_sourceDir, "nested", "b.txt")).Length;

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.NoCompression, null, CancellationToken.None);

        var reports = new List<OperationProgress>();
        (int Files, long Bytes) totals = default;

        var coverage = service.VerifyCoverage(
            _sourceDir,
            _zipPath,
            new InlineProgress(reports.Add),
            (files, bytes) => totals = (files, bytes),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.True);
            Assert.That(totals.Files, Is.EqualTo(2));
            Assert.That(totals.Bytes, Is.EqualTo(expectedBytes));
            Assert.That(reports.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(reports.Select(static x => x.Completed), Is.Ordered.Ascending);
            Assert.That(reports.Select(static x => x.Bytes), Is.Ordered.Ascending);
            Assert.That(reports[^1].Completed, Is.EqualTo(2));
            Assert.That(reports[^1].Bytes, Is.EqualTo(expectedBytes));
        }
    }

    [Test]
    public void VerifyCoverage_CancelledMidContentCheck_ThrowsAndPreservesSourceAndArchive()
    {
        WriteIncompressible(Path.Combine(_sourceDir, "a.txt"), 2 * 1024 * 1024);

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.NoCompression, null, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var reports = 0;
        var progress = new InlineProgress(
            _ =>
            {
                reports++;
                cts.Cancel();
            });

        Assert.Throws<OperationCanceledException>(() =>
            service.VerifyCoverage(_sourceDir, _zipPath, progress, cts.Token));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reports, Is.EqualTo(1));
            Assert.That(File.Exists(_zipPath), Is.True);
            Assert.That(Directory.Exists(_sourceDir), Is.True);
        }
    }

    [Test]
    public void VerifyCoverage_CancelledOnSingleSmallFileProgress_Throws()
    {
        File.Delete(Path.Combine(_sourceDir, "nested", "b.txt"));
        Directory.Delete(Path.Combine(_sourceDir, "nested"));

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.NoCompression, null, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() =>
            service.VerifyCoverage(_sourceDir, _zipPath, progress, cts.Token));
    }

    [Test]
    public void VerifyCoverage_FailsWhenAlternateStreamAppearsAfterPacking()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var file = Path.Combine(_sourceDir, "a.txt");
        var modified = File.GetLastWriteTime(file);
        WriteAlternateStream(file, "late", "added-after-pack");
        File.SetLastWriteTime(file, modified);

        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попал альтернативный поток NTFS «{file}:late»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenContentReplacedWithSameLengthAndTime()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var changed = Path.Combine(_sourceDir, "a.txt");
        var modified = File.GetLastWriteTime(changed);
        File.WriteAllText(changed, "omega");
        File.SetLastWriteTime(changed, modified);

        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"после упаковки изменилось содержимое файла «{changed}»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenDirectoryCannotBeEnumerated()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var locked = Path.Combine(_sourceDir, "locked");
        Directory.CreateDirectory(locked);
        TestAcl.DenyEnumeration(locked);

        try
        {
            if (Readable(locked))
            {
                Assert.Ignore("Deny-ACE не действует на этот процесс (запуск от администратора) – ветку нечем воспроизвести.");
            }

            var blocked = service.Collect(_sourceDir, CancellationToken.None);
            var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    blocked.Unreadable,
                    Is.EqualTo(
                        new[]
                        {
                            locked,
                        }));

                Assert.That(coverage.Ok, Is.False);
                Assert.That(coverage.Detail, Does.Contain("locked"));
            }
        }
        finally
        {
            TestAcl.AllowEnumeration(locked);
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenDirectoryContainsDataReparseFile()
    {
        var reparseFile = Path.Combine(_sourceDir, "cloud.txt");
        File.WriteAllText(reparseFile, "placeholder-bytes");
        Assert.That(TryTagAsDataReparsePoint(reparseFile), Is.True);

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        var stats = service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.Count, Is.EqualTo(2));
            Assert.That(
                content.ReparsePoints,
                Is.EqualTo(
                    new[]
                    {
                        reparseFile,
                    }));

            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попала точка повторного разбора «{reparseFile}»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenDirectoryContainsJunctionAndDoesNotTreatParentAsEmpty()
    {
        var target = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "external.txt"), "external");

        var linksOnly = Path.Combine(_sourceDir, "links-only");
        Directory.CreateDirectory(linksOnly);
        var junction = Path.Combine(linksOnly, "link");
        Assert.That(TryCreateJunction(junction, target), Is.True);

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.EmptyDirectories, Does.Not.Contain(linksOnly));
            Assert.That(
                content.ReparsePoints,
                Is.EqualTo(
                    new[]
                    {
                        junction,
                    }));

            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попала точка повторного разбора «{junction}»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenDirectoryHasAlternateDataStream()
    {
        var nested = Path.Combine(_sourceDir, "nested");
        WriteAlternateStream(nested, "meta", "dir-stream");

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                content.AlternateStreams,
                Is.EqualTo(
                    new[]
                    {
                        $"{nested}:meta",
                    }));

            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попал альтернативный поток NTFS «{nested}:meta»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenFileHasAlternateDataStream()
    {
        var file = Path.Combine(_sourceDir, "a.txt");
        WriteAlternateStream(file, "secret", "hidden-payload");

        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        var stats = service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(stats.Count, Is.EqualTo(2));
            Assert.That(
                content.AlternateStreams,
                Is.EqualTo(
                    new[]
                    {
                        $"{file}:secret",
                    }));

            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попал альтернативный поток NTFS «{file}:secret»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenRootDirectoryIsJunctionWithoutTraversingTarget()
    {
        var junctionRoot = Path.Combine(_tempDir, "root-link");
        Assert.That(TryCreateJunction(junctionRoot, _sourceDir), Is.True);

        var service = new ArchiveService();
        var content = service.Collect(junctionRoot, CancellationToken.None);
        service.ZipFiles(junctionRoot, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var coverage = service.VerifyCoverage(junctionRoot, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(content.Files, Is.Empty);
            Assert.That(
                content.ReparsePoints,
                Is.EqualTo(
                    new[]
                    {
                        junctionRoot,
                    }));

            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Is.EqualTo($"в архив не попала точка повторного разбора «{junctionRoot}»"));
        }
    }

    [Test]
    public void VerifyCoverage_FailsWhenSourceFileChangedAfterPacking()
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var changed = content.Files[0];
        File.WriteAllText(changed, "переписано другим процессом, длина другая");
        File.SetLastWriteTime(changed, DateTime.Now.AddMinutes(5));

        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Does.Contain(Path.GetFileName(changed)));
        }
    }

    [TestCase(true, "late.txt")]
    [TestCase(false, "late")]
    public void VerifyCoverage_FailsWhenSourceGrewAfterCollect(bool file, string name)
    {
        var service = new ArchiveService();
        var content = service.Collect(_sourceDir, CancellationToken.None);
        service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var path = Path.Combine(_sourceDir, "nested", name);

        if (file)
        {
            File.WriteAllText(path, "late");
        }
        else
        {
            Directory.CreateDirectory(path);
        }

        var coverage = service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.Ok, Is.False);
            Assert.That(coverage.Detail, Does.Contain(name));
        }
    }

    [Test]
    public void Verify_CancelledBeforeStart_ThrowsEvenWithoutEntries()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, Content(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => service.VerifyZip(_zipPath, stats, true, null, cts.Token));
    }

    [Test]
    public void Verify_CancelledMidwayThrowsInsteadOfReportingSuccess()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var progress = new InlineProgress(_ => cts.Cancel());

        Assert.Throws<OperationCanceledException>(() =>
            service.VerifyZip(_zipPath, stats, true, progress, cts.Token));

        Assert.That(File.Exists(_zipPath), Is.True);
    }

    [Test]
    public void Verify_ContentCheckPassesOnIntactArchive()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var verified = new List<string>();

        var verify = service.VerifyZip(_zipPath, stats, true, new InlineProgress(update => verified.Add(update.Current)), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(verify.Ok, Is.True);
            Assert.That(verify.Detail, Is.Empty);
            Assert.That(
                verified,
                Is.EquivalentTo(
                    new[]
                    {
                        "a.txt", "nested/b.txt",
                    }));
        }
    }

    [Test]
    public void Verify_FailsWhenExpectationsDoNotMatchArchive()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var verify = service.VerifyZip(
            _zipPath,
            stats with
            {
                Count = stats.Count + 1,
            });

        Assert.That(verify.Ok, Is.False);
    }

    [Test]
    public void Verify_StructuralPassesButContentCatchesCorruptedEntry()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.NoCompression, null, CancellationToken.None);

        CorruptStoredContent("bravo-bravo");

        var structural = service.VerifyZip(_zipPath, stats);
        var content = service.VerifyZip(_zipPath, stats, true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(structural.Ok, Is.True);
            Assert.That(content.Ok, Is.False);
            Assert.That(content.Detail, Does.Contain("nested/b.txt"));
        }
    }

    [Test]
    public void ZipThenVerify_RoundTripsAllFiles()
    {
        var service = new ArchiveService();

        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);
        var verify = service.VerifyZip(_zipPath, stats);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(_zipPath), Is.True);
            Assert.That(stats.Count, Is.EqualTo(2));
            Assert.That(stats.Bytes, Is.EqualTo("alpha".Length + "bravo-bravo".Length));
            Assert.That(verify.Ok, Is.True);
        }

        using var zip = ZipFile.OpenRead(_zipPath);
        Assert.That(zip.GetEntry("nested/b.txt"), Is.Not.Null);
    }

    [Test]
    public void Zip_CancelledInsideLargeFileLeavesNoOrphanArchive()
    {
        var service = new ArchiveService();
        var bigFile = Path.Combine(_sourceDir, "big.bin");
        WriteIncompressible(bigFile, 32 * 1024 * 1024);

        using var cts = new CancellationTokenSource();
        var poller = Task.Run(() => CancelOnceArchiveGrows(cts));

        Assert.Throws<OperationCanceledException>(() =>
            service.ZipFiles(_sourceDir, Content(bigFile), _zipPath, CompressionLevel.SmallestSize, null, cts.Token));

        poller.Wait();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_sizeAtCancel, Is.GreaterThan(0), "отмена пришла раньше, чем упаковка успела что-то записать");
            Assert.That(File.Exists(_zipPath), Is.False);
        }
    }

    [Test]
    public void Zip_CancelledMidwayLeavesNoOrphanArchive()
    {
        var service = new ArchiveService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, cts.Token));

        Assert.That(File.Exists(_zipPath), Is.False);
    }

    [TestCase(1900, 1980, 1, 1, 0, 0, 0)]
    [TestCase(2200, 2107, 12, 31, 23, 59, 58)]
    public void Zip_ClampsModificationTimeToZipRange(int sourceYear, int year, int month, int day, int hour, int minute, int second)
    {
        var service = new ArchiveService();
        var file = Path.Combine(_sourceDir, "a.txt");
        File.SetLastWriteTime(file, new(sourceYear, 5, 6, 7, 8, 9, DateTimeKind.Local));

        service.ZipFiles(_sourceDir, Content(file), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(_zipPath);
        var entry = zip.GetEntry("a.txt");

        Assert.That(entry, Is.Not.Null);
        Assert.That(
            (entry.LastWriteTime.DateTime - new DateTime(year, month, day, hour, minute, second)).Duration(),
            Is.LessThanOrEqualTo(DirectoryComparer.FatTimestampTolerance));
    }

    [Test]
    public void Zip_KeepsSourceModificationTime()
    {
        var service = new ArchiveService();
        var expected = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Local);
        var file = Path.Combine(_sourceDir, "a.txt");
        File.SetLastWriteTime(file, expected);

        service.ZipFiles(_sourceDir, Content(file), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(_zipPath);
        var entry = zip.GetEntry("a.txt");

        Assert.That(entry, Is.Not.Null);
        Assert.That(
            (entry.LastWriteTime.DateTime - expected).Duration(),
            Is.LessThanOrEqualTo(DirectoryComparer.FatTimestampTolerance));
    }

    [Test]
    public void Zip_OccupiedNameKeepsForeignFile()
    {
        File.WriteAllText(_zipPath, "чужой архив");

        var service = new ArchiveService();

        var failure = Assert.Throws<IOException>(() =>
            service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Message, Does.Contain(Path.GetFileName(_zipPath)).And.Contain("занято"));
            Assert.That(File.ReadAllText(_zipPath), Is.EqualTo("чужой архив"));
        }
    }

    private static ArchiveContent Content(params string[] files)
    {
        return new(files, [], [], 0);
    }

    private ArchiveContent SourceFiles()
    {
        return Content(Path.Combine(_sourceDir, "a.txt"), Path.Combine(_sourceDir, "nested", "b.txt"));
    }

    private void WriteSingleEntryArchive(string entryName, string text, DateTime modified)
    {
        using var zip = ZipFile.Open(_zipPath, ZipArchiveMode.Create);

        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        entry.LastWriteTime = modified;

        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static bool Readable(string path)
    {
        try
        {
            new DirectoryInfo(path).GetFiles();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void CorruptStoredContent(string marker)
    {
        var data = File.ReadAllBytes(_zipPath);
        var index = data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(marker));

        Assert.That(index, Is.GreaterThanOrEqualTo(0), "содержимое записи не найдено в архиве");

        data[index] ^= 0xFF;
        File.WriteAllBytes(_zipPath, data);
    }

    private void CancelOnceArchiveGrows(CancellationTokenSource cts)
    {
        for (var attempt = 0; attempt < 500 && !File.Exists(_zipPath); attempt++)
        {
            Thread.Sleep(1);
        }

        Thread.Sleep(100);
        _sizeAtCancel = File.Exists(_zipPath) ? new FileInfo(_zipPath).Length : 0;
        cts.Cancel();
    }

    private static void WriteIncompressible(string path, int bytes)
    {
        var data = new byte[bytes];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
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

    private static bool TryTagAsDataReparsePoint(string path)
    {
        const uint fsctlSetReparsePoint = 0x0009_00A4;
        const uint thirdPartyReparseTag = 0x0000_0042;
        var buffer = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, thirdPartyReparseTag);
        Guid.NewGuid().TryWriteBytes(buffer.AsSpan(8));

        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.ReadWrite);

            if (!DeviceIoControl(handle, fsctlSetReparsePoint, buffer, buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
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

    private static void WriteAlternateStream(string path, string streamName, string text)
    {
        using var handle = CreateFile(
            $"{path}:{streamName}",
            0x4000_0000u,
            0x7u,
            IntPtr.Zero,
            2u,
            0x0200_0000u,
            IntPtr.Zero);

        Assert.That(handle.IsInvalid, Is.False);

        using var stream = new FileStream(handle, FileAccess.Write);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        IntPtr lpOutBuffer,
        int nOutBufferSize,
        out int lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);
}
