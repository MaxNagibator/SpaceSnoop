using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Views.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class PerformanceChartCanvasTests
{
    [Test]
    public void Без_замеров_холст_ничего_не_рисует()
    {
        Assert.That(Render(PerformanceChartData.Empty), Is.Null);
    }

    [Test]
    public void Нулевой_размер_холста_не_роняет_отрисовку()
    {
        Assert.That(Render(Sample(), 0, 0), Is.Null);
    }

    [Test]
    public void График_рисует_полосу_основание_порог_и_обе_линии()
    {
        Assert.That(Render(Sample())?.Children, Has.Count.EqualTo(5));
    }

    [Test]
    public void Без_кистей_холст_ничего_не_рисует()
    {
        var canvas = new PerformanceChartCanvas { Data = Sample() };

        Assert.That(Render(canvas), Is.Null);
    }

    [Test]
    public void Порог_просадки_стоит_на_своей_доле_шкалы()
    {
        var line = (LineGeometry)((GeometryDrawing)Render(Sample())!.Children[2]).Geometry;
        var expected = 2 + ((1 - (AppDefaults.PerformanceHitchMs / 900d)) * (120 - 4));

        Assert.Multiple(() =>
        {
            Assert.That(line.StartPoint.Y, Is.EqualTo(expected).Within(0.001));
            Assert.That(line.EndPoint.Y, Is.EqualTo(expected).Within(0.001));
        });
    }

    [Test]
    public void Пунктир_достаётся_памяти_а_сплошная_отклику()
    {
        var children = Render(Sample())!.Children;
        var memory = ((GeometryDrawing)children[3]).Pen;
        var delay = ((GeometryDrawing)children[4]).Pen;

        Assert.Multiple(() =>
        {
            Assert.That(memory.DashStyle.Dashes, Is.Not.Empty);
            Assert.That(memory.Thickness, Is.EqualTo(1));
            Assert.That(delay.DashStyle.Dashes, Is.Empty);
            Assert.That(delay.Thickness, Is.EqualTo(1.5));
        });
    }

    [Test]
    public void Полоса_операции_рисуется_слева_направо_по_своим_границам()
    {
        var band = (RectangleGeometry)((GeometryDrawing)Render(Sample())!.Children[0]).Geometry;
        var data = Sample();

        Assert.Multiple(() =>
        {
            Assert.That(band.Rect.Left, Is.EqualTo(data.Bands[0].Start * 400).Within(0.001));
            Assert.That(band.Rect.Right, Is.EqualTo(data.Bands[0].End * 400).Within(0.001));
        });
    }

    [Test]
    public void Порог_просадки_не_рисуется_если_он_выше_шкалы()
    {
        var quiet = PerformanceChartLayout.Build(new(DateTime.UnixEpoch, 1, 0, 0, 0, 0,
        [
            new(1000, 0, 100, 0, 0, 0, 0, null),
            new(500, 0, 200, 0, 0, 0, 0, null),
        ]));

        Assert.That(Render(quiet)?.Children, Has.Count.EqualTo(3));
    }

    private static DrawingGroup? Render(PerformanceChartData data, double width = 400, double height = 120)
    {
        var canvas = new PerformanceChartCanvas
        {
            Data = data,
            DelayBrush = Brushes.OrangeRed,
            MemoryBrush = Brushes.SteelBlue,
            BandBrush = Brushes.Bisque,
            ThresholdBrush = Brushes.Goldenrod,
            BaselineBrush = Brushes.Gainsboro,
        };

        return Render(canvas, width, height);
    }

    private static DrawingGroup? Render(PerformanceChartCanvas canvas, double width = 400, double height = 120)
    {
        canvas.Measure(new(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        return VisualTreeHelper.GetDrawing(canvas);
    }

    private static PerformanceChartData Sample()
    {
        return PerformanceChartLayout.Build(new(DateTime.UnixEpoch, 2, 0, 0, 0, 0,
        [
            new(2000, 10, 100, 0, 0, 0, 0, null),
            new(1500, 900, 180, 0, 0, 0, 0, "Сканирование"),
            new(1000, 40, 260, 0, 0, 0, 0, "Сканирование"),
            new(500, 5, 140, 0, 0, 0, 0, null),
        ]));
    }
}
