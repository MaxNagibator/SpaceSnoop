using SpaceSnoop.Wpf.Views;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class CompactWrapPanelTests
{
    [TestCase(double.PositiveInfinity, true, true, true)]
    [TestCase(310, true, true, true)]
    [TestCase(309, true, true, false)]
    [TestCase(239, false, true, false)]
    [TestCase(159, false, false, false)]
    public void В_одну_строку_первыми_уходят_необязательные_с_конца_потом_остальные_с_конца(
        double available, bool showsSecond, bool showsThird, bool showsFourth)
    {
        var panel = CreateSummary(out var first, out var second, out var third, out var fourth);
        CompactWrapPanel.SetOptional(fourth, true);

        Layout(panel, available);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsEnabled, Is.True);
            Assert.That(second.IsEnabled, Is.EqualTo(showsSecond));
            Assert.That(third.IsEnabled, Is.EqualTo(showsThird));
            Assert.That(fourth.IsEnabled, Is.EqualTo(showsFourth));
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(20));
        }
    }

    [TestCase(false, 2, true)]
    [TestCase(true, 1, false)]
    public void Не_влезшее_в_одну_строку_не_видно_и_недоступно(bool singleLine, int rows, bool lastEnabled)
    {
        var first = new Border
        {
            Width = 100,
            Height = 20,
        };

        var last = new Border
        {
            Width = 100,
            Height = 20,
        };

        var panel = new CompactWrapPanel
        {
            SingleLine = singleLine,
            Children =
            {
                first,
                last,
            },
        };

        panel.Measure(new(150, double.PositiveInfinity));
        panel.Arrange(new(panel.DesiredSize));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(20 * rows));
            Assert.That(last.IsEnabled, Is.EqualTo(lastEnabled));
            Assert.That(LayoutInformation.GetLayoutSlot(last).Width > 0, Is.EqualTo(lastEnabled));
        }
    }

    [Test]
    public void Первый_элемент_шире_панели_не_выходит_за_её_край()
    {
        var first = new TextBlock
        {
            Text = new('ж', 80),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        var panel = new CompactWrapPanel
        {
            SingleLine = true,
            Children =
            {
                first,
            },
        };

        panel.Measure(new(120, double.PositiveInfinity));
        panel.Arrange(new(new(120, panel.DesiredSize.Height)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Width, Is.LessThanOrEqualTo(120));
            Assert.That(LayoutInformation.GetLayoutSlot(first).Right, Is.LessThanOrEqualTo(120), "Первый элемент вышел за край панели.");
            Assert.That(first.RenderSize.Width, Is.LessThanOrEqualTo(120), "Первый элемент не ужат до ширины панели.");
        }
    }

    [Test]
    public void Предупреждение_в_сжатом_виде_стоит_второй_строкой_и_доступно()
    {
        var first = new Border
        {
            Width = 100,
            Height = 20,
        };

        var metric = new Border
        {
            Width = 60,
            Height = 20,
        };

        var warning = new Border
        {
            Width = 120,
            Height = 24,
        };

        CompactWrapPanel.SetKeep(warning, true);
        var panel = new CompactWrapPanel
        {
            SingleLine = true,
            Children =
            {
                first,
                metric,
                warning,
            },
        };

        panel.Measure(new(150, double.PositiveInfinity));
        panel.Arrange(new(panel.DesiredSize));

        var slot = LayoutInformation.GetLayoutSlot(warning);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(44), "Строк не две.");
            Assert.That(warning.IsEnabled, Is.True, "Предупреждение снято.");
            Assert.That(slot, Is.EqualTo(new Rect(0, 20, 120, 24)), "Предупреждение не на второй строке.");
            Assert.That(metric.IsEnabled, Is.False, "Метрика, не влезшая рядом с первой, осталась.");
        }
    }

    [TestCase(310, true, true, 0)]
    [TestCase(229, false, false, 0)]
    [TestCase(169, false, true, 20)]
    [TestCase(90, false, false, 20)]
    public void Предупреждение_не_снимается_а_если_не_влезает_уходит_на_вторую_строку(
        double available, bool showsSecond, bool showsThird, double warningTop)
    {
        var panel = CreateSummary(out var first, out var second, out var third, out var warning);
        CompactWrapPanel.SetKeep(warning, true);

        Layout(panel, available);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.IsEnabled, Is.True);
            Assert.That(second.IsEnabled, Is.EqualTo(showsSecond));
            Assert.That(third.IsEnabled, Is.EqualTo(showsThird));
            Assert.That(warning.IsEnabled, Is.True);
            Assert.That(LayoutInformation.GetLayoutSlot(warning).Top, Is.EqualTo(warningTop));
        }
    }

    [Test]
    public void Скрытое_предупреждение_второй_строки_не_заводит()
    {
        var panel = CreateSummary(out var first, out var second, out var third, out var warning);
        CompactWrapPanel.SetKeep(warning, true);
        warning.Visibility = Visibility.Collapsed;

        Layout(panel, 150);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(panel.DesiredSize.Height, Is.EqualTo(20));
            Assert.That(first.IsEnabled, Is.True);
            Assert.That(second.IsEnabled, Is.False);
            Assert.That(third.IsEnabled, Is.False);
        }
    }

    private static CompactWrapPanel CreateSummary(out Border first, out Border second, out Border third, out Border fourth)
    {
        first = new Border { Width = 100, Height = 20 };
        second = new Border { Width = 80, Height = 20 };
        third = new Border { Width = 60, Height = 20 };
        fourth = new Border { Width = 70, Height = 20 };
        CompactWrapPanel.SetOptional(second, true);

        return new CompactWrapPanel
        {
            SingleLine = true,
            Children =
            {
                first,
                second,
                third,
                fourth,
            },
        };
    }

    private static void Layout(CompactWrapPanel panel, double available)
    {
        panel.Measure(new(available, double.PositiveInfinity));
        panel.Arrange(new(panel.DesiredSize));
    }
}
