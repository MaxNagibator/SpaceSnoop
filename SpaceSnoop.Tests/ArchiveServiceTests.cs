using SpaceSnoop.Core;
using System.IO.Compression;

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
}
