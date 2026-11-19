using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Mft;

namespace SpaceSnoop.Tests;

[TestFixture]
public class MftProgressPathTests
{
    [Test]
    public void Путь_прогресса_корня_равен_пути_цели()
    {
        var root = new DirectorySpace("Windows", null, default, default);

        Assert.That(new MftProgressPath(root, @"C:\Windows").For(root), Is.EqualTo(@"C:\Windows"));
    }

    [TestCase(@"C:\", @"C:\System32\drivers")]
    [TestCase(@"C:\Windows", @"C:\Windows\System32\drivers")]
    public void Путь_прогресса_указывает_на_вложенный_каталог(string rootPath, string expected)
    {
        var root = new DirectorySpace(Path.GetFileName(rootPath), null, default, default);
        var system = new DirectorySpace("System32", root, default, default);
        var drivers = new DirectorySpace("drivers", system, default, default);

        var path = new MftProgressPath(root, rootPath).For(drivers);

        Assert.That(path, Is.EqualTo(expected));
    }
}
