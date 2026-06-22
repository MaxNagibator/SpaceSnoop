using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Converters;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class HeatColorTests
{
    [Test]
    public void Нулевая_доля_даёт_преобладание_зелёного()
    {
        var color = HeatColor.From(0, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.G, Is.GreaterThan(color.R));
            Assert.That(color.G, Is.GreaterThan(color.B));
        }
    }

    [Test]
    public void Полная_доля_даёт_преобладание_красного()
    {
        var color = HeatColor.From(1, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.R, Is.GreaterThan(color.G));
            Assert.That(color.R, Is.GreaterThan(color.B));
        }
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
    public void Красный_канал_монотонно_растёт_с_долей()
    {
        var previous = -1;

        for (var fraction = 0.0; fraction <= 1.0; fraction += 0.1)
        {
            int red = HeatColor.From(fraction, AppDefaults.IntensityDefault).R;
            Assert.That(red, Is.GreaterThanOrEqualTo(previous));
            previous = red;
        }
    }

    [Test]
    public void Цвет_вычисляется_детерминированно()
    {
        Assert.That(HeatColor.From(0.42, 8), Is.EqualTo(HeatColor.From(0.42, 8)));
    }
}
