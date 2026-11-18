using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using KeepShell.Bootstrap;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ThemeTilesTests
{
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);
        AppThemes.Register();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        AppThemes.RegisterSystem(SystemTheme.AppsUseLightTheme());
    }

    [TestCase(true, TestName = "Превью «Как в системе» – светлая и тёмная половины при светлом режиме Windows")]
    [TestCase(false, TestName = "Превью «Как в системе» – светлая и тёмная половины при тёмном режиме Windows")]
    public void Превью_системной_темы_не_зависит_от_режима_Windows(bool appsUseLightTheme)
    {
        AppThemes.RegisterSystem(appsUseLightTheme);
        using var host = Show(out var list);

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < list.Items.Count; i++)
            {
                var option = (ThemeDefinition)list.Items[i];
                var tile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
                var preview = (ContentControl)tile.Template.FindName("Preview", tile);
                var darkHalf = (ContentControl)tile.Template.FindName("DarkHalf", tile);
                var isSystem = option.Key == AppThemes.SystemKey;

                Assert.That(preview.Content, Is.EqualTo(isSystem ? ThemeManager.DefaultLight.Palette : option.Palette), option.DisplayName);
                Assert.That(darkHalf.IsVisible, Is.EqualTo(isSystem), option.DisplayName);
                Assert.That(darkHalf.Content, Is.EqualTo(isSystem ? ThemeManager.DefaultDark.Palette : null), option.DisplayName);
                Assert.That(AutomationProperties.GetName(tile), Is.EqualTo($"Тема: {option.DisplayName}"));
            }
        }
    }

    [Test]
    public void Стрелка_вправо_выбирает_соседнюю_плитку()
    {
        using var host = Show(out var list);
        list.SelectedIndex = 0;
        host.Settle();

        var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        first.Focus();
        first.RaiseEvent(
            new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first)!, 0, Key.Right)
            {
                RoutedEvent = Keyboard.KeyDownEvent,
            });

        host.Settle();

        Assert.That(list.SelectedIndex, Is.EqualTo(1));
    }

    private static VisualTestHost Show(out ListBox list)
    {
        list = new()
        {
            Style = (Style)Application.Current.FindResource("ListBox.ThemeTiles"),
            ItemsSource = ThemeManager.Themes,
        };

        var host = VisualTestHost.Show(
            new()
            {
                Content = list,
            },
            640,
            240,
            true);

        host.Settle();

        return host;
    }
}
