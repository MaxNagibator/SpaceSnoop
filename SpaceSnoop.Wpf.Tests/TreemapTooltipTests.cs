using SpaceSnoop.Wpf.Views.Scan;
using System.Windows;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class TreemapTooltipTests
{
    private static readonly Size Popup = new(300, 200);

    private static readonly Rect Room = new(0, 0, 1000, 800);

    [Test]
    public void При_запасе_места_подсказка_встаёт_под_курсором()
    {
        var placement = TreemapTooltip.Place(new(400, 300), Popup, Room, new(1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(placement.Side, Is.EqualTo(TooltipSide.Below));
            Assert.That(placement.DeviceTopLeft.X, Is.EqualTo(376));
            Assert.That(placement.DeviceTopLeft.Y, Is.EqualTo(304));
        });
    }

    [Test]
    public void У_нижнего_края_подсказка_уходит_вверх()
    {
        var placement = TreemapTooltip.Place(new(400, 700), Popup, Room, new(1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(placement.Side, Is.EqualTo(TooltipSide.Above));
            Assert.That(placement.DeviceTopLeft.Y, Is.EqualTo(496));
        });
    }

    [TestCase(400, 300, TestName = "в середине")]
    [TestCase(400, 700, TestName = "у нижнего края")]
    [TestCase(10, 700, TestName = "в левом нижнем углу")]
    [TestCase(990, 790, TestName = "в правом нижнем углу")]
    public void Курсор_никогда_не_попадает_внутрь_тела_подсказки(double x, double y)
    {
        var cursor = new Point(x, y);
        var placement = TreemapTooltip.Place(cursor, Popup, Room, new(1, 1));
        var body = new Rect(placement.DeviceTopLeft, Popup);

        Assert.That(body.Contains(cursor), Is.False, $"сторона {placement.Side}, тело {body}");
    }

    [Test]
    public void Точка_отдаётся_в_координатах_устройства_а_не_в_логических()
    {
        var dpi = new DpiScale(2.25, 2.25);
        var popupDevice = new Size(Popup.Width * dpi.DpiScaleX, Popup.Height * dpi.DpiScaleY);

        var placement = TreemapTooltip.Place(new(400, 300), popupDevice, Room, dpi);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Side, Is.EqualTo(TooltipSide.Below));
            Assert.That(placement.DeviceTopLeft.X, Is.EqualTo(376 * 2.25).Within(0.001));
            Assert.That(placement.DeviceTopLeft.Y, Is.EqualTo(304 * 2.25).Within(0.001));
        });
    }

    [Test]
    public void Размер_подсказки_приводится_к_логическим_единицам_до_выбора_стороны()
    {
        var dpi = new DpiScale(2.25, 2.25);
        var popupDevice = new Size(Popup.Width * dpi.DpiScaleX, Popup.Height * dpi.DpiScaleY);

        var placement = TreemapTooltip.Place(new(400, 700), popupDevice, Room, dpi);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Side, Is.EqualTo(TooltipSide.Above));
            Assert.That(placement.DeviceTopLeft.Y, Is.EqualTo(496 * 2.25).Within(0.001));
        });
    }
}
