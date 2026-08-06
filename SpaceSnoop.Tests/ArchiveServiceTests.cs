using SpaceSnoop.Core;
using System.IO.Compression;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ArchiveServiceTests
{
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
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private string _tempDir = null!;
    private string _sourceDir = null!;
    private string _zipPath = null!;
    private long _sizeAtCancel;

    private static ArchiveContent Content(params string[] files)
    {
        return new(files, [], [], 0);
    }

    private ArchiveContent SourceFiles()
    {
        return Content(Path.Combine(_sourceDir, "a.txt"), Path.Combine(_sourceDir, "nested", "b.txt"));
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
    public void Verify_FailsWhenExpectationsDoNotMatchArchive()
    {
        var service = new ArchiveService();
        var stats = service.ZipFiles(_sourceDir, SourceFiles(), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        var verify = service.VerifyZip(_zipPath, stats with { Count = stats.Count + 1 });

        Assert.That(verify.Ok, Is.False);
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
            Assert.That(verified, Is.EqualTo(new[] { "a.txt", "nested/b.txt" }));
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
        Assert.That((entry.LastWriteTime.DateTime - expected).Duration(),
            Is.LessThanOrEqualTo(DirectoryComparer.FatTimestampTolerance));
    }

    [TestCase(1900, 1980, 1, 1, 0, 0, 0)]
    [TestCase(2200, 2107, 12, 31, 23, 59, 58)]
    public void Zip_ClampsModificationTimeToZipRange(int sourceYear, int year, int month, int day, int hour, int minute, int second)
    {
        var service = new ArchiveService();
        var file = Path.Combine(_sourceDir, "a.txt");
        File.SetLastWriteTime(file, new DateTime(sourceYear, 5, 6, 7, 8, 9, DateTimeKind.Local));

        service.ZipFiles(_sourceDir, Content(file), _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(_zipPath);
        var entry = zip.GetEntry("a.txt");

        Assert.That(entry, Is.Not.Null);
        Assert.That((entry.LastWriteTime.DateTime - new DateTime(year, month, day, hour, minute, second)).Duration(),
            Is.LessThanOrEqualTo(DirectoryComparer.FatTimestampTolerance));
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
            Assert.That(content.EmptyDirectories, Is.EqualTo(new[] { hollow }));
            Assert.That(content.Files, Has.Count.EqualTo(2));
            Assert.That(stats.Count, Is.EqualTo(3));
            Assert.That(service.VerifyZip(_zipPath, stats).Ok, Is.True);
            Assert.That(service.VerifyCoverage(_sourceDir, _zipPath, CancellationToken.None).Ok, Is.True);
        }

        using var zip = ZipFile.OpenRead(_zipPath);
        Assert.That(zip.GetEntry("hollow/"), Is.Not.Null);
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
                Assert.That(blocked.Unreadable, Is.EqualTo(new[] { locked }));
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

    [Test]
    public void VerifyZip_ContentCheckAcceptsDirectoryEntries()
    {
        var service = new ArchiveService();
        Directory.CreateDirectory(Path.Combine(_sourceDir, "hollow"));

        var content = service.Collect(_sourceDir, CancellationToken.None);
        var stats = service.ZipFiles(_sourceDir, content, _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        Assert.That(service.VerifyZip(_zipPath, stats, true, null, CancellationToken.None).Ok, Is.True);
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
}
