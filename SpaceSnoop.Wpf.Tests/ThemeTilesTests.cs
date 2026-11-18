using KeepShell.Testing;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Converters;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ThemeTilesTests
{
    private static readonly string[] PreviewKeys = ["Bg.Base", "Bg.Surface", "Border.Default", "Fg.Muted", "Accent.Primary"];

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

    [TestCase(AppTheme.Light)]
    [TestCase(AppTheme.Dark)]
    [TestCase(AppTheme.Tarkov)]
    [TestCase(AppTheme.System)]
    [TestCase(AppTheme.Sublime)]
    public void Палитра_темы_несёт_все_цвета_превью(AppTheme theme)
    {
        var palette = ThemeSwatchBrushConverter.PaletteOf(theme);
        var missing = PreviewKeys.Where(key => palette?[key] is not Brush).ToArray();

        Assert.That(missing, Is.Empty, $"В палитре «{theme}» нет кистей превью: {string.Join(", ", missing)}.");
    }

    [TestCase(true, TestName = "Превью «Как в системе» – светлая и тёмная половины при светлом режиме Windows")]
    [TestCase(false, TestName = "Превью «Как в системе» – светлая и тёмная половины при тёмном режиме Windows")]
    public void Превью_системной_темы_не_зависит_от_режима_Windows(bool appsUseLightTheme)
    {
        AppThemes.RegisterSystem(appsUseLightTheme);
        using var host = Show(out var list);

        using (Assert.EnterMultipleScope())
        {
            for (var i = 0; i < SettingsOptions.Themes.Count; i++)
            {
                var option = SettingsOptions.Themes[i];
                var tile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i);
                var preview = (ContentControl)tile.Template.FindName("Preview", tile);
                var darkHalf = (ContentControl)tile.Template.FindName("DarkHalf", tile);
                var isSystem = option.Value == AppTheme.System;

                Assert.That(preview.Content, Is.EqualTo(isSystem ? AppTheme.Light : option.Value), option.Label);
                Assert.That(darkHalf.IsVisible, Is.EqualTo(isSystem), option.Label);
                Assert.That(darkHalf.Content, Is.EqualTo(AppTheme.Dark), option.Label);
                Assert.That(AutomationProperties.GetName(tile), Is.EqualTo($"Тема: {option.Label}"));
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
            ItemsSource = SettingsOptions.Themes,
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
