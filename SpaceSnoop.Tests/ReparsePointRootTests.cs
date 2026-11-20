using SpaceSnoop.Core;
using System.Diagnostics;

namespace SpaceSnoop.Tests;

[TestFixture]
public class ReparsePointRootTests
{
    private readonly List<string> _links = [];

    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopReparse_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var link in _links.Where(static x => Directory.Exists(x)))
        {
            Directory.Delete(link, false);
        }

        _links.Clear();

        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Test]
    public void TryGetTagByHandle_читает_тег_точки_через_FileAttributeTagInfo()
    {
        var target = Directory.CreateDirectory(Path.Combine(_tempDir, "target")).FullName;
        var link = Path.Combine(_tempDir, "junction");
        CreateJunction(link, target);

        var found = ReparsePoint.TryGetTagByHandle(link, out var tag);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.True);
            Assert.That(tag, Is.EqualTo(0xA000_0003u));
        }
    }

    [Test]
    public void TryGetTag_на_корне_диска_не_ломается_через_фолбэк_дескриптора()
    {
        var root = Path.GetPathRoot(_tempDir)!;
        var found = ReparsePoint.TryGetTag(root, out var tag);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(found, Is.False);
            Assert.That(tag, Is.EqualTo(0u));
        }
    }

    [Test]
    public void DescribeRootRefusal_разрешает_корень_UNC_шары_без_проверки_FindFirstFile()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(@"\\server\share")), Is.Null);
            Assert.That(DiskSpaceCalculator.DescribeRootRefusal(new(@"\\server\share\")), Is.Null);
        }
    }

    [Test]
    public void Обход_дерева_пропускает_вложенную_ссылку_но_сканирует_остальное()
    {
        var outside = Directory.CreateDirectory(Path.Combine(_tempDir, "outside")).FullName;
        File.WriteAllBytes(Path.Combine(outside, "external.bin"), new byte[1024]);

        var scanRoot = Directory.CreateDirectory(Path.Combine(_tempDir, "root")).FullName;
        File.WriteAllBytes(Path.Combine(scanRoot, "local.bin"), new byte[256]);
        CreateJunction(Path.Combine(scanRoot, "linked"), outside);

        var result = new DiskSpaceCalculator().Calculate(new(scanRoot), CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.TotalSize, Is.EqualTo(256));
            Assert.That(result.TotalFileCount, Is.EqualTo(1));
        }
    }

    private void CreateJunction(string link, string target)
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
            Assert.Ignore("не удалось создать junction");
        }

        _links.Add(link);
    }
}
