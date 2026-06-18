using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Converters;

namespace SpaceSnoop.Tests;

[TestFixture]
public class HeatColorTests
{
    [Test]
    public void From_ZeroFraction_IsGreenDominant()
    {
        var color = HeatColor.From(0, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.G, Is.GreaterThan(color.R));
            Assert.That(color.G, Is.GreaterThan(color.B));
        }
    }

    [Test]
    public void From_FullFraction_IsRedDominant()
    {
        var color = HeatColor.From(1, AppDefaults.IntensityDefault);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(color.R, Is.GreaterThan(color.G));
            Assert.That(color.R, Is.GreaterThan(color.B));
        }
    }

    [Test]
    public void From_ClampsFractionToUnitRange()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeatColor.From(5, 8), Is.EqualTo(HeatColor.From(1, 8)));
            Assert.That(HeatColor.From(-5, 8), Is.EqualTo(HeatColor.From(0, 8)));
        }
    }

    [Test]
    public void From_ClampsIntensityToConfiguredRange()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(HeatColor.From(0.5, 999), Is.EqualTo(HeatColor.From(0.5, AppDefaults.IntensityMax)));
            Assert.That(HeatColor.From(0.5, -1), Is.EqualTo(HeatColor.From(0.5, AppDefaults.IntensityMin)));
        }
    }

    [Test]
    public void From_RedChannelIsMonotonicAcrossFraction()
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
    public void From_IsDeterministic()
    {
        Assert.That(HeatColor.From(0.42, 8), Is.EqualTo(HeatColor.From(0.42, 8)));
    }
}
