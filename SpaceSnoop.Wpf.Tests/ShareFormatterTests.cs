using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ShareFormatterTests
{
    [TestCase(0)]
    [TestCase(-0.5)]
    [TestCase(double.NaN)]
    public void Неположительная_доля_даёт_ноль(double fraction)
    {
        Assert.That(ShareFormatter.Format(fraction), Is.EqualTo("0 %"));
    }

    [TestCase(1)]
    [TestCase(1.5)]
    public void Целая_доля_даёт_сто(double fraction)
    {
        Assert.That(ShareFormatter.Format(fraction), Is.EqualTo("100 %"));
    }

    [Test]
    public void Ненулевая_мелочь_не_округляется_до_нуля()
    {
        Assert.That(ShareFormatter.Format(0.0004), Is.EqualTo($"<{0.1:0.#} %"));
    }

    [Test]
    public void Почти_целая_доля_не_выдаётся_за_сто()
    {
        Assert.That(ShareFormatter.Format(0.9996), Is.EqualTo($"{99.9:0.#} %"));
    }

    [TestCase(0.5, 50.0)]
    [TestCase(0.123, 12.3)]
    [TestCase(0.001, 0.1)]
    public void Обычная_доля_показывается_с_одним_знаком(double fraction, double percent)
    {
        Assert.That(ShareFormatter.Format(fraction), Is.EqualTo($"{percent:0.#} %"));
    }
}
