using SpaceSnoop.Wpf.Views.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class TreemapLayoutTests
{
    private static readonly double[][] WeightSets =
    [
        [3, 2, 1],
        [10, 10, 10, 10],
        [100, 1, 1, 1, 1],
        [5],
        [7, 7, 7, 7, 7, 7, 7],
    ];

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var weights in WeightSets)
        {
            yield return new(weights, 320d, 200d);
            yield return new(weights, 100d, 400d);
        }
    }

    [TestCaseSource(nameof(Cases))]
    public void Плитки_покрывают_всю_площадь(double[] weights, double width, double height)
    {
        var rects = TreemapLayout.Squarify(weights, width, height);

        var covered = rects.Sum(static r => r.Width * r.Height);

        Assert.That(covered, Is.EqualTo(width * height).Within(0.5));
    }

    [TestCaseSource(nameof(Cases))]
    public void Плитки_остаются_внутри_границ(double[] weights, double width, double height)
    {
        var rects = TreemapLayout.Squarify(weights, width, height);

        Assert.That(rects, Has.Length.EqualTo(weights.Length));

        using (Assert.EnterMultipleScope())
        {
            foreach (var rect in rects)
            {
                Assert.That(rect.X, Is.GreaterThanOrEqualTo(-0.001));
                Assert.That(rect.Y, Is.GreaterThanOrEqualTo(-0.001));
                Assert.That(rect.X + rect.Width, Is.LessThanOrEqualTo(width + 0.001));
                Assert.That(rect.Y + rect.Height, Is.LessThanOrEqualTo(height + 0.001));
            }
        }
    }

    [Test]
    public void Площадь_плитки_пропорциональна_весу()
    {
        double[] weights = [4, 2, 1, 1];
        var rects = TreemapLayout.Squarify(weights, 200, 200);

        var total = weights.Sum();
        var area = 200d * 200d;

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < weights.Length; i++)
            {
                var expected = weights[i] / total * area;
                Assert.That(rects[i].Width * rects[i].Height, Is.EqualTo(expected).Within(0.5));
            }
        }
    }

    [TestCase(0, 0)]
    [TestCase(100, 0)]
    [TestCase(0, 100)]
    public void Неположительные_границы_дают_пустые_прямоугольники(double width, double height)
    {
        var rects = TreemapLayout.Squarify([3, 2, 1], width, height);

        Assert.That(rects, Has.All.EqualTo(default(LayoutRect)));
    }

    [Test]
    public void Пустой_список_весов_даёт_пустой_результат()
    {
        Assert.That(TreemapLayout.Squarify([], 100, 100), Is.Empty);
    }

    [Test]
    public void Все_нулевые_веса_дают_пустые_прямоугольники()
    {
        var rects = TreemapLayout.Squarify([0, 0, 0], 100, 100);

        Assert.That(rects, Has.All.EqualTo(default(LayoutRect)));
    }
}
