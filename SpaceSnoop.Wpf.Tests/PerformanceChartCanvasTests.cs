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
    public void В_тесной_полосе_график_не_рисуется()
    {
        Assert.That(Drawings(Render(Sample(), 400, 40)), Has.Exactly(1).Items);
    }

    [Test]
    public void Без_кистей_остаётся_только_поле_для_курсора()
    {
        var canvas = new PerformanceChartCanvas { Data = Sample() };

        Assert.That(Drawings(Render(canvas)), Has.Exactly(1).Items);
    }

    [Test]
    public void Обе_шкалы_и_ось_времени_подписаны()
    {
        var labels = Drawings(Render(Sample())).OfType<GlyphRunDrawing>().Count();

        Assert.That(labels, Is.GreaterThanOrEqualTo(8));
    }

    [Test]
    public void Порог_просадки_рисуется_пунктиром_поперёк_поля()
    {
        var threshold = Lines(Render(Sample())).Single(static line => IsDashed(line, 2, 4));

        Assert.Multiple(() =>
        {
            Assert.That(threshold.Geometry.StartPoint.Y, Is.EqualTo(threshold.Geometry.EndPoint.Y));
            Assert.That(threshold.Geometry.StartPoint.X, Is.LessThan(threshold.Geometry.EndPoint.X));
        });
    }

    [Test]
    public void Порог_просадки_не_рисуется_если_он_выше_шкалы()
    {
        var quiet = PerformanceChartLayout.Build(new(DateTime.UnixEpoch, 1, 0, 0, 0, 0,
        [
            new(1000, 10, 100, 0, 0, 0, 0, null),
            new(500, 20, 200, 0, 0, 0, 0, null),
        ]));

        Assert.That(Lines(Render(quiet)).Any(static line => IsDashed(line, 2, 4)), Is.False);
    }

    [Test]
    public void Каждая_серия_идёт_в_своём_поле_своей_кистью()
    {
        var series = Drawings(Render(Sample()))
            .OfType<GeometryDrawing>()
            .Where(static drawing => drawing.Geometry is StreamGeometry)
            .ToList();

        var delay = series.Single(static drawing => ReferenceEquals(drawing.Pen.Brush, Brushes.OrangeRed));
        var memory = series.Single(static drawing => ReferenceEquals(drawing.Pen.Brush, Brushes.SteelBlue));

        Assert.Multiple(() =>
        {
            Assert.That(delay.Pen.Thickness, Is.GreaterThan(memory.Pen.Thickness));
            Assert.That(delay.Geometry.Bounds.Bottom, Is.LessThan(memory.Geometry.Bounds.Top));
        });
    }

    [Test]
    public void Курсор_выбирает_ближайший_к_мыши_замер()
    {
        var canvas = Canvas(Sample());
        Render(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.NearestIndex(new(canvas.ActualWidth - 4, canvas.ActualHeight / 2)), Is.EqualTo(3));
            Assert.That(canvas.NearestIndex(new(0, canvas.ActualHeight / 2)), Is.EqualTo(-1));
            Assert.That(canvas.NearestIndex(new(canvas.ActualWidth / 2, canvas.ActualHeight - 2)), Is.EqualTo(-1));
        });
    }

    [Test]
    public void Просадка_помечается_кружком_на_линии_отклика()
    {
        var markers = Drawings(Render(Sample()))
            .OfType<GeometryDrawing>()
            .Where(static drawing => drawing.Geometry is EllipseGeometry { RadiusX: 3.5 })
            .ToList();

        Assert.That(markers, Has.Exactly(1).Items);
    }

    [Test]
    public void Полоса_операции_лежит_лентой_под_полями()
    {
        var drawing = Render(Sample())!;
        var track = Drawings(drawing)
            .OfType<GeometryDrawing>()
            .Select(static item => item.Geometry)
            .OfType<RectangleGeometry>()
            .Single(static geometry => geometry.RadiusX > 0);

        var grid = Lines(drawing).Max(static line => line.Geometry.StartPoint.Y);

        Assert.Multiple(() =>
        {
            Assert.That(track.Rect.Height, Is.EqualTo(12));
            Assert.That(track.Rect.Top, Is.GreaterThan(grid));
        });
    }

    [Test]
    public void Без_курсора_вертикали_на_графике_нет()
    {
        Assert.That(Lines(Render(Sample())).Any(static line => Math.Abs(line.Geometry.StartPoint.X - line.Geometry.EndPoint.X) < 0.01), Is.False);
    }

    private static bool IsDashed((Pen Pen, LineGeometry Geometry) line, params double[] dashes)
    {
        return line.Pen.DashStyle.Dashes.SequenceEqual(dashes);
    }

    private static List<(Pen Pen, LineGeometry Geometry)> Lines(DrawingGroup? drawing)
    {
        return
        [
            .. Drawings(drawing)
                .OfType<GeometryDrawing>()
                .Where(static item => item is { Pen: not null, Geometry: LineGeometry })
                .Select(static item => (item.Pen, (LineGeometry)item.Geometry)),
        ];
    }

    private static IEnumerable<Drawing> Drawings(DrawingGroup? group)
    {
        if (group is null)
        {
            yield break;
        }

        foreach (var child in group.Children)
        {
            if (child is DrawingGroup nested)
            {
                foreach (var item in Drawings(nested))
                {
                    yield return item;
                }

                continue;
            }

            yield return child;
        }
    }

    private static DrawingGroup? Render(PerformanceChartData data, double width = 400, double height = 200)
    {
        return Render(Canvas(data), width, height);
    }

    private static DrawingGroup? Render(PerformanceChartCanvas canvas, double width = 400, double height = 200)
    {
        canvas.Measure(new(width, height));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();

        return VisualTreeHelper.GetDrawing(canvas);
    }

    private static PerformanceChartCanvas Canvas(PerformanceChartData data)
    {
        return new()
        {
            Data = data,
            DelayBrush = Brushes.OrangeRed,
            MemoryBrush = Brushes.SteelBlue,
            BandBrush = Brushes.Bisque,
            ThresholdBrush = Brushes.Goldenrod,
            BaselineBrush = Brushes.Gainsboro,
            LabelBrush = Brushes.Gray,
            SurfaceBrush = Brushes.White,
            TextBrush = Brushes.Black,
        };
    }

    private static PerformanceChartData Sample()
    {
        return PerformanceChartLayout.Build(new(DateTime.UnixEpoch, 2, 0, 0, 0, 0,
        [
            new(2000, 10, 40 * 1024 * 1024, 0, 0, 0, 0, null),
            new(1500, 900, 44 * 1024 * 1024, 0, 0, 0, 0, "Сканирование"),
            new(1000, 40, 46 * 1024 * 1024, 0, 0, 0, 0, "Сканирование"),
            new(500, 5, 42 * 1024 * 1024, 0, 0, 0, 0, null),
        ]));
    }
}
