using SpaceSnoop.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class PriorityRowPanelTests
{
    private const double NameMin = 96;
    private const double BarMin = 96;
    private const double BarMax = 172;
    private static readonly double[] Meta = [90, 75];

    [TestCase(433, 60, true, true, BarMax)]
    [TestCase(400, 60, false, true, BarMax)]
    [TestCase(300, 60, false, false, BarMax)]
    [TestCase(220, 60, false, false, 124)]
    [TestCase(180, 120, false, false, 120)]
    [TestCase(150, 60, false, false, BarMin)]
    [TestCase(80, 60, false, false, 80)]
    [Apartment(ApartmentState.STA)]
    public void При_сужении_строка_сначала_скрывает_счётчики_затем_сужает_полосу(double width, double sizeTextWidth, bool showsFiles, bool showsShare, double barWidth)
    {
        var name = new Border
        {
            MinWidth = NameMin,
        };

        var files = new Border
        {
            Width = Meta[0],
        };

        var share = new Border
        {
            Width = Meta[1],
        };

        var bar = new Border
        {
            MinWidth = BarMin,
            MaxWidth = BarMax,
            Child = new Border
            {
                Width = sizeTextWidth,
            },
        };

        var panel = new PriorityRowPanel
        {
            Children =
            {
                name,
                files,
                share,
                bar,
            },
        };

        panel.Measure(new(width, 20));
        panel.Arrange(new(0, 0, width, 20));

        var barSlot = LayoutInformation.GetLayoutSlot(bar);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(barSlot.Right, Is.LessThanOrEqualTo(width + 0.001));
            Assert.That(barSlot.Width, Is.EqualTo(barWidth).Within(0.001));
            Assert.That(LayoutInformation.GetLayoutSlot(files).Width > 0, Is.EqualTo(showsFiles));
            Assert.That(LayoutInformation.GetLayoutSlot(share).Width > 0, Is.EqualTo(showsShare));
        }
    }
}
