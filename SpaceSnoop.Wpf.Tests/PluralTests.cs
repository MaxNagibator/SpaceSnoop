using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PluralTests
{
    [TestCase(1, "1 объект")]
    [TestCase(2, "2 объекта")]
    [TestCase(4, "4 объекта")]
    [TestCase(5, "5 объектов")]
    [TestCase(11, "11 объектов")]
    [TestCase(14, "14 объектов")]
    [TestCase(21, "21 объект")]
    [TestCase(102, "102 объекта")]
    [TestCase(111, "111 объектов")]
    [TestCase(0, "0 объектов")]
    public void Счётное_слово_согласуется_с_числом(int count, string expected)
    {
        Assert.That(Plural.Format(count, "объект", "объекта", "объектов"), Is.EqualTo(expected));
    }
}
