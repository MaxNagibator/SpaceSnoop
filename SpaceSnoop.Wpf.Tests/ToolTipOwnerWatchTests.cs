using KeepShell.Testing;
using SpaceSnoop.Wpf.Views.Behaviors;
using System.Windows;
using System.Windows.Controls;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ToolTipOwnerWatchTests
{
    [Test]
    public void Подсказка_кнопки_пропавшей_до_открытия_сразу_закрывается()
    {
        ToolTipOwnerWatch.Install();

        var toolTip = new ToolTip
        {
            Content = "Сканировать заново",
        };

        var button = new Button
        {
            Content = "Сканировать снова",
            ToolTip = toolTip,
        };

        var card = new Border
        {
            Child = button,
            Visibility = Visibility.Collapsed,
        };

        using var host = VisualTestHost.Show(
            new()
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        card,
                    },
                },
            },
            320,
            200);

        var opened = false;
        toolTip.Opened += (_, _) => opened = true;

        toolTip.PlacementTarget = button;
        toolTip.IsOpen = true;
        host.Settle();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(opened, Is.True, "Подсказка не открывалась – кейс ничего не проверяет.");
            Assert.That(toolTip.IsOpen, Is.False);
        }
    }

    [TestCase(Visibility.Collapsed, TestName = "Подсказка закрывается, когда её кнопка пропадает вместе с карточкой")]
    [TestCase(Visibility.Hidden, TestName = "Подсказка закрывается, когда её кнопка становится невидимой")]
    public void Подсказка_не_висит_над_исчезнувшей_кнопкой(Visibility gone)
    {
        ToolTipOwnerWatch.Install();

        var toolTip = new ToolTip
        {
            Content = "Сканировать заново",
        };

        var button = new Button
        {
            Content = "Сканировать снова",
            ToolTip = toolTip,
        };

        var card = new Border
        {
            Child = button,
        };

        using var host = VisualTestHost.Show(
            new()
            {
                Content = new StackPanel
                {
                    Children =
                    {
                        card,
                    },
                },
            },
            320,
            200);

        toolTip.PlacementTarget = button;
        toolTip.IsOpen = true;
        host.Settle();

        Assert.That(toolTip.IsOpen, Is.True, "Подсказка не открылась – кейс ничего не проверяет.");

        card.Visibility = gone;
        host.Settle();

        Assert.That(toolTip.IsOpen, Is.False);
    }
}
