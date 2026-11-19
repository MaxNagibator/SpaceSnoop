using SpaceSnoop.Core;

namespace SpaceSnoop.Tests;

[TestFixture]
public class UncPathTests
{
    [TestCase(@"\\", true, true, false, null, null)]
    [TestCase(@"//", true, true, false, null, null)]
    [TestCase(@"\\srv", true, true, false, "srv", null)]
    [TestCase(@"//srv", true, true, false, "srv", null)]
    [TestCase(@"\\srv\", true, true, false, "srv", null)]
    [TestCase(@"//srv/", true, true, false, "srv", null)]
    [TestCase(@"\\srv\share", true, false, true, "srv", "share")]
    [TestCase(@"\\srv\share\", true, false, true, "srv", "share")]
    [TestCase(@"//srv/share/", true, false, true, "srv", "share")]
    [TestCase(@"\\srv\share\sub", true, false, false, "srv", "share")]
    [TestCase(@"//srv/share/sub/dir", true, false, false, "srv", "share")]
    [TestCase(@"\\?\UNC\srv\share", true, false, true, "srv", "share")]
    [TestCase(@"\\?\UNC\srv\share\sub", true, false, false, "srv", "share")]
    [TestCase(@"\\?\UNC\", true, true, false, null, null)]
    [TestCase(@"\\?\UNC\srv", true, true, false, "srv", null)]
    [TestCase(@"C:\Windows", false, false, false, null, null)]
    [TestCase(@"\\?\C:\Windows", false, false, false, null, null)]
    [TestCase(@"\\.\C:", false, false, false, null, null)]
    [TestCase(@"\??\C:\Windows", false, false, false, null, null)]
    [TestCase(@"\", false, false, false, null, null)]
    [TestCase("", false, false, false, null, null)]
    public void Разбор_UNC_определяет_признаки_хост_и_шару(
        string path,
        bool expectedUnc,
        bool expectedIncomplete,
        bool expectedShareRoot,
        string? expectedHost,
        string? expectedShare)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(UncPath.IsUnc(path), Is.EqualTo(expectedUnc));
            Assert.That(UncPath.IsIncompleteUnc(path), Is.EqualTo(expectedIncomplete));
            Assert.That(UncPath.IsShareRoot(path), Is.EqualTo(expectedShareRoot));
            Assert.That(UncPath.GetHost(path), Is.EqualTo(expectedHost));
            Assert.That(UncPath.GetShare(path), Is.EqualTo(expectedShare));
        }
    }

    [TestCase(@"\\srv\share\sub\file.txt", true, @"\\srv\share")]
    [TestCase(@"//srv/share/", true, @"\\srv\share")]
    [TestCase(@"\\?\UNC\srv\share\sub", true, @"\\srv\share")]
    [TestCase(@"\\srv\", false, "")]
    [TestCase(@"\\srv", false, "")]
    [TestCase(@"C:\dir\sub", false, "")]
    public void Выделение_корня_шары_возвращает_единый_формат(string path, bool expectedOk, string expectedRoot)
    {
        var ok = UncPath.TryParseShareRoot(path, out var shareRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ok, Is.EqualTo(expectedOk));
            Assert.That(shareRoot, Is.EqualTo(expectedRoot));
        }
    }

    [TestCase(@"//srv/share/sub", @"\\srv\share\sub")]
    [TestCase(@"\\?\UNC\srv\share\sub", @"\\srv\share\sub")]
    [TestCase(@"//srv//share//sub", @"\\srv\share\sub")]
    [TestCase(@"//srv/", @"\\srv\")]
    [TestCase(@"//", @"\\")]
    [TestCase(@"\\?\UNC", @"\\")]
    public void Нормализация_приводит_UNC_к_обратным_слэшам(string input, string expected)
    {
        Assert.That(UncPath.NormalizeUnc(input), Is.EqualTo(expected));
    }
}
