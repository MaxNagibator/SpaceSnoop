using SpaceSnoop.Wpf.Views.Scan;
using System.Windows;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class TreemapTooltipTests
{
    private static readonly Size Popup = new(300, 200);

    private static readonly Rect Room = new(0, 0, 1000, 800);

    [TestCase(10, 700, TestName = "в левом нижнем углу")]
    [TestCase(990, 790, TestName = "в правом нижнем углу")]
    public void Курсор_никогда_не_попадает_внутрь_тела_подсказки(double x, double y)
    {
        var cursor = new Point(x, y);
        var placement = TreemapTooltip.Place(cursor, Popup, Room, new(1, 1));
        var body = new Rect(placement.DeviceTopLeft, Popup);

        Assert.That(body.Contains(cursor), Is.False, $"сторона {placement.Side}, тело {body}");
    }

    [TestCase(1, 300, false)]
    [TestCase(1, 700, true)]
    [TestCase(2.25, 300, false)]
    [TestCase(2.25, 700, true)]
    public void Подсказка_умещается_у_курсора_с_учётом_масштаба(double scale, double y, bool above)
    {
        var dpi = new DpiScale(scale, scale);
        var cursor = new Point(400, y);
        var popupDevice = new Size(Popup.Width * scale, Popup.Height * scale);
        var deviceRoom = new Rect(0, 0, Room.Width * scale, Room.Height * scale);
        var deviceCursor = new Point(cursor.X * scale, cursor.Y * scale);
        var placement = TreemapTooltip.Place(cursor, popupDevice, Room, dpi);
        var body = new Rect(placement.DeviceTopLeft, popupDevice);
        var gap = above ? deviceCursor.Y - body.Bottom : body.Top - deviceCursor.Y;

        Assert.Multiple(() =>
        {
            Assert.That(placement.Side, Is.EqualTo(above ? TooltipSide.Above : TooltipSide.Below));
            Assert.That(deviceRoom.Contains(body), Is.True);
            Assert.That((deviceCursor.X - body.Left) / scale, Is.EqualTo(24).Within(0.001));
            Assert.That(gap / scale, Is.EqualTo(4).Within(0.001));
        });
    }
}
