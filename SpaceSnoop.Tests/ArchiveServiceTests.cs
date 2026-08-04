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

    private List<string> SourceFiles()
    {
        return
        [
            Path.Combine(_sourceDir, "a.txt"),
            Path.Combine(_sourceDir, "nested", "b.txt"),
        ];
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
            service.ZipFiles(_sourceDir, [bigFile], _zipPath, CompressionLevel.SmallestSize, null, cts.Token));

        poller.Wait();

        Assert.That(File.Exists(_zipPath), Is.False);
    }

    [Test]
    public void Zip_KeepsSourceModificationTime()
    {
        var service = new ArchiveService();
        var expected = new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Local);
        var file = Path.Combine(_sourceDir, "a.txt");
        File.SetLastWriteTime(file, expected);

        service.ZipFiles(_sourceDir, [file], _zipPath, CompressionLevel.Optimal, null, CancellationToken.None);

        using var zip = ZipFile.OpenRead(_zipPath);
        var entry = zip.GetEntry("a.txt");

        Assert.That(entry, Is.Not.Null);
        Assert.That((entry.LastWriteTime.DateTime - expected).Duration(),
            Is.LessThanOrEqualTo(DirectoryComparer.FatTimestampTolerance));
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
        cts.Cancel();
    }

    private static void WriteIncompressible(string path, int bytes)
    {
        var data = new byte[bytes];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(path, data);
    }

    private sealed class InlineProgress(Action<OperationProgress> onReport) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            onReport(value);
        }
    }
}
