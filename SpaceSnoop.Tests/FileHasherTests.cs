using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class FileHasherTests
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
    public void ComputeHash_RespectsCancellation()
    {
        var file = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(file, "test");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => FileHasher.ComputeHash(file, cts.Token));
    }

    [TestCase("hello", "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824")]
    [TestCase("world", "486ea46224d1bb4fb680f34f7c9ad96a8f24ec88be73ea8e5a6c65260e9cb8a7")]
    public void ComputeHash_ReturnsSha256OfFileContents(string contents, string expected)
    {
        var file = Path.Combine(_tempDir, "a.txt");
        File.WriteAllText(file, contents);

        var hash = FileHasher.ComputeHash(file, CancellationToken.None);

        Assert.That(hash, Is.EqualTo(expected));
    }
}
