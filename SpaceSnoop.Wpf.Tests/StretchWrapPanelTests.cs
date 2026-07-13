using SpaceSnoop.Wpf.Views;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class StretchWrapPanelTests
{
    [Test]
    public void Всё_помещается_в_одну_строку()
    {
        var rows = StretchWrapPanel.PackRows([100, 100, 100], 400);

        Assert.That(rows, Is.EqualTo([(0, 3)]));
    }

    [Test]
    public void Переполнение_переносит_на_следующую_строку()
    {
        var rows = StretchWrapPanel.PackRows([300, 50, 300], 360);

        Assert.That(rows, Is.EqualTo([(0, 2), (2, 1)]));
    }

    [Test]
    public void Элемент_шире_строки_занимает_свою_строку_без_зацикливания()
    {
        var rows = StretchWrapPanel.PackRows([500, 100], 360);

        Assert.That(rows, Is.EqualTo([(0, 1), (1, 1)]));
    }

    [TestCase(new[] { 400d, 34d, 400d }, 880d, 1)]
    [TestCase(new[] { 400d, 34d, 400d }, 620d, 2)]
    public void Раскладка_путей_одна_строка_на_широком_две_на_узком(double[] widths, double line, int expectedRows)
    {
        var rows = StretchWrapPanel.PackRows(widths, line);

        Assert.That(rows, Has.Count.EqualTo(expectedRows));
        Assert.That(rows.Sum(r => r.Count), Is.EqualTo(widths.Length));
    }
}
