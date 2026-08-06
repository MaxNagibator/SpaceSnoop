using SpaceSnoop.Wpf.Views.Behaviors;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PathSuggestTests
{
    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpaceSnoopSuggest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tempDir, "Projects"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Pictures"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Music"));
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

    [TestCase("Mu", "Music")]
    [TestCase("mus", "Music")]
    public void Единственное_совпадение_по_префиксу_без_учёта_регистра(string typed, string expected)
    {
        var matches = PathSuggest.Matches(Path.Combine(_tempDir, typed));

        Assert.That(matches, Is.EqualTo([Path.Combine(_tempDir, expected)]));
    }

    [Test]
    public void Несколько_совпадений_возвращаются_по_порядку_для_перебора()
    {
        var matches = PathSuggest.Matches(Path.Combine(_tempDir, "P"));

        Assert.That(matches, Is.EqualTo([Path.Combine(_tempDir, "Pictures"), Path.Combine(_tempDir, "Projects")]));
    }

    [TestCase("nosep")]
    [TestCase("emptyprefix")]
    [TestCase("nomatch")]
    public void Совпадений_нет(string scenario)
    {
        var input = scenario switch
        {
            "nosep" => "Music",
            "emptyprefix" => _tempDir + Path.DirectorySeparatorChar,
            _ => Path.Combine(_tempDir, "zzz"),
        };

        Assert.That(PathSuggest.Matches(input), Is.Empty);
    }

    [TestCase(@"\\server\", ExpectedResult = "server")]
    [TestCase("//server/", ExpectedResult = "server")]
    [TestCase(@"\\server\share\", ExpectedResult = null)]
    [TestCase(@"C:\", ExpectedResult = null)]
    [TestCase(@"\\", ExpectedResult = null)]
    public string? Корень_сервера_отличается_от_обычного_каталога(string directory)
    {
        return PathSuggest.ServerName(directory);
    }

    [Test]
    public void Первый_запрос_к_недоступному_серверу_не_блокирует_ввод()
    {
        Assert.That(PathSuggest.Matches($@"\\{Guid.NewGuid():N}\sh"), Is.Empty);
    }
}
