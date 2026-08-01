using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Converters;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class HeatColorTests
{
    [TestCase(0.0)]
    [TestCase(0.25)]
    [TestCase(0.5)]
    [TestCase(0.75)]
    [TestCase(1.0)]
    public void Тон_остаётся_тёплым_на_всей_шкале(double fraction)
    {
        var color = HeatColor.From(fraction, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.R, Is.GreaterThan(color.G));
            Assert.That(color.G, Is.GreaterThan(color.B));
        }
    }

    [Test]
    public void Мелкий_узел_почти_нейтрален_а_крупный_насыщен()
    {
        var cool = HeatColor.From(0, AppDefaults.IntensityDefault);
        var hot = HeatColor.From(1, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(cool.R - cool.B, Is.LessThan(48));
            Assert.That(hot.R - hot.B, Is.GreaterThan(96));
        }
    }

    [Test]
    public void Насыщенность_монотонно_растёт_с_долей()
    {
        var previous = -1;

        for (var fraction = 0.0; fraction <= 1.0; fraction += 0.1)
        {
            var color = HeatColor.From(fraction, AppDefaults.IntensityDefault);
            int spread = color.R - color.B;

            Assert.That(spread, Is.GreaterThanOrEqualTo(previous));
            previous = spread;
        }
    }

    [Test]
    public void Крупный_узел_темнее_мелкого()
    {
        var cool = HeatColor.From(0, AppDefaults.IntensityDefault);
        var hot = HeatColor.From(1, AppDefaults.IntensityDefault);

        Assert.That(hot.R, Is.LessThan(cool.R));
    }

    [Test]
    public void Доля_зажимается_в_диапазон_от_нуля_до_единицы()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeatColor.From(5, 8), Is.EqualTo(HeatColor.From(1, 8)));
            Assert.That(HeatColor.From(-5, 8), Is.EqualTo(HeatColor.From(0, 8)));
        }
    }

    [Test]
    public void Интенсивность_зажимается_в_заданный_диапазон()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeatColor.From(0.5, 999), Is.EqualTo(HeatColor.From(0.5, AppDefaults.IntensityMax)));
            Assert.That(HeatColor.From(0.5, -1), Is.EqualTo(HeatColor.From(0.5, AppDefaults.IntensityMin)));
        }
    }

    [Test]
    public void Цвет_вычисляется_детерминированно()
    {
        Assert.That(HeatColor.From(0.42, 8), Is.EqualTo(HeatColor.From(0.42, 8)));
    }
}
