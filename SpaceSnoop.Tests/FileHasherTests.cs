using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class FileHasherTests
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
    public void IdenticalFiles_ProduceSameHash()
    {
        var file1 = Path.Combine(_tempDir, "a.txt");
        var file2 = Path.Combine(_tempDir, "b.txt");
        File.WriteAllText(file1, "hello world");
        File.WriteAllText(file2, "hello world");

        var hash1 = FileHasher.ComputeHash(file1, CancellationToken.None);
        var hash2 = FileHasher.ComputeHash(file2, CancellationToken.None);

        Assert.That(hash2, Is.EqualTo(hash1));
    }

    [Test]
    public void DifferentFiles_ProduceDifferentHash()
    {
        var file1 = Path.Combine(_tempDir, "a.txt");
        var file2 = Path.Combine(_tempDir, "b.txt");
        File.WriteAllText(file1, "hello");
        File.WriteAllText(file2, "world");

        var hash1 = FileHasher.ComputeHash(file1, CancellationToken.None);
        var hash2 = FileHasher.ComputeHash(file2, CancellationToken.None);

        Assert.That(hash2, Is.Not.EqualTo(hash1));
    }

    [Test]
    public void Hash_IsHexString()
    {
        var file = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(file, "test");

        var hash = FileHasher.ComputeHash(file, CancellationToken.None);

        Assert.That(hash, Has.Length.EqualTo(64));
        Assert.That(hash, Does.Match("^[0-9a-f]+$"));
    }

    [Test]
    public void ComputeHash_RespectsCancellation()
    {
        var file = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(file, "test");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileHasher.ComputeHash(file, cts.Token));
    }
}
