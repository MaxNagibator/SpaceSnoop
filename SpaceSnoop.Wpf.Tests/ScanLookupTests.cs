using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.IO;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ScanLookupTests
{
    private const string Root = @"C:\Data";

    [TestCase(@"C:\Data\projects\node_modules")]
    [TestCase(@"C:\Data\projects\node_modules\")]
    [TestCase(@"c:\data\PROJECTS\Node_Modules")]
    [TestCase("  C:\\Data\\projects\\node_modules  ")]
    public void Вложенный_каталог_находится_независимо_от_регистра_и_хвостового_разделителя(string path)
    {
        var root = BuildTree();

        Assert.That(ScanLookup.Find([root], path)?.Name, Is.EqualTo("node_modules"));
    }

    [TestCase(@"C:\Data")]
    [TestCase(@"C:\Data\projects")]
    public void Сам_корень_и_промежуточный_каталог_тоже_находятся(string path)
    {
        var root = BuildTree();

        Assert.That(ScanLookup.Find([root], path), Is.Not.Null);
    }

    [TestCase(@"C:\Data2\projects")]
    [TestCase(@"C:\Datafiles")]
    [TestCase(@"D:\Data\projects")]
    [TestCase(@"C:\Data\projects\отсутствует")]
    [TestCase("")]
    [TestCase("   ")]
    public void Путь_вне_дерева_не_находится(string path)
    {
        var root = BuildTree();

        Assert.That(ScanLookup.Find([root], path), Is.Null);
    }

    [Test]
    public void Файл_в_поддереве_находится_по_полному_пути()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-lookup");

        try
        {
            var filePath = Path.Combine(directory.FullName, "dump.bin");
            File.WriteAllText(filePath, "x");

            var root = new DirectorySpace(directory.FullName, null, DateTime.Now, DateTime.Now);
            root.AddFile(new(filePath));

            Assert.That(ScanLookup.Find([root], filePath), Is.TypeOf<FileSpace>());
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Test]
    public void Поиск_идёт_по_всем_корням()
    {
        var first = new DirectorySpace(@"C:\First", null, DateTime.Now, DateTime.Now);
        var second = BuildTree();

        Assert.That(ScanLookup.Find([first, second], @"C:\Data\projects"), Is.Not.Null);
    }

    [TestCase(@"C:\Data\projects\foo", "foo")]
    [TestCase(@"C:\Data\projects\Foo", "Foo")]
    [TestCase(@"C:\Data\projects\FOO", null)]
    public void Каталоги_различающиеся_регистром_берутся_только_точным_совпадением(string path, string? expected)
    {
        var root = BuildTree();
        var projects = root.SubDirectories[0];
        projects.Add(new("Foo", projects, DateTime.Now, DateTime.Now));
        projects.Add(new("foo", projects, DateTime.Now, DateTime.Now));

        Assert.That(ScanLookup.Find([root], path)?.Name, Is.EqualTo(expected));
    }

    [Test]
    public void Файлы_различающиеся_регистром_берутся_только_точным_совпадением()
    {
        var directory = Directory.CreateTempSubdirectory("spacesnoop-lookup");

        try
        {
            var upper = Directory.CreateDirectory(Path.Combine(directory.FullName, "upper"));
            var lower = Directory.CreateDirectory(Path.Combine(directory.FullName, "lower"));
            File.WriteAllText(Path.Combine(upper.FullName, "Dump.bin"), "x");
            File.WriteAllText(Path.Combine(lower.FullName, "dump.bin"), "x");

            var root = new DirectorySpace(directory.FullName, null, DateTime.Now, DateTime.Now);
            root.AddFile(new(Path.Combine(upper.FullName, "Dump.bin")));
            root.AddFile(new(Path.Combine(lower.FullName, "dump.bin")));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ScanLookup.Find([root], Path.Combine(directory.FullName, "dump.bin"))?.Name, Is.EqualTo("dump.bin"));
                Assert.That(ScanLookup.Find([root], Path.Combine(directory.FullName, "DUMP.BIN")), Is.Null);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static DirectorySpace BuildTree()
    {
        var root = new DirectorySpace(Root, null, DateTime.Now, DateTime.Now);
        var projects = new DirectorySpace("projects", root, DateTime.Now, DateTime.Now);
        var modules = new DirectorySpace("node_modules", projects, DateTime.Now, DateTime.Now);

        projects.Add(modules);
        root.Add(projects);

        return root;
    }
}
