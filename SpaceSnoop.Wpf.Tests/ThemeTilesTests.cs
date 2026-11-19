using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCase(true, TestName = "Настройки показывают обе половины системной темы при светлом режиме Windows")]
    [TestCase(false, TestName = "Настройки показывают обе половины системной темы при тёмном режиме Windows")]
    public void Настройки_показывают_обе_половины_системной_темы(bool appsUseLightTheme)
    {
        var settings = _services.GetRequiredService<SettingsViewModel>();
        var sections = settings.Sections;
        var previous = sections.SelectedPath;

        try
        {
            AppThemes.RegisterSystem(appsUseLightTheme);
            sections.SelectCommand.Execute(sections["appearance"]);
            using var host = ShowAppearance(settings);
            var list = ThemeList(host);
            var option = list.Items.OfType<ThemeDefinition>().Single(item => item.Key == AppThemes.SystemKey);
            var tile = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(option);
            var preview = (ContentControl)tile.Template.FindName("Preview", tile);
            var darkHalf = (ContentControl)tile.Template.FindName("DarkHalf", tile);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(preview.IsVisible, Is.True);
                Assert.That(preview.Content, Is.EqualTo(ThemeManager.DefaultLight.Palette));
                Assert.That(darkHalf.IsVisible, Is.True);
                Assert.That(darkHalf.Content, Is.EqualTo(ThemeManager.DefaultDark.Palette));
                Assert.That(AutomationProperties.GetName(tile), Is.EqualTo($"Тема: {option.DisplayName}"));
            }
        }
        finally
        {
            AppThemes.RegisterSystem(SystemTheme.AppsUseLightTheme());
            sections.Restore(previous);
            Settle();
        }
    }

    [Test]
    public void Стрелка_вправо_применяет_соседнюю_тему_в_настройках()
    {
        var settings = _services.GetRequiredService<SettingsViewModel>();
        var sections = settings.Sections;
        var previousSection = sections.SelectedPath;
        var previousTheme = ThemeManager.Current!.Key;
        var store = _services.GetRequiredService<ISettingsStore>();
        var previousPreference = store.GetStringValue(SettingsKeys.Theme) ?? string.Empty;

        try
        {
            sections.SelectCommand.Execute(sections["appearance"]);
            settings.Theme.Select(AppThemes.SystemKey);
            using var host = ShowAppearance(settings, keyboardFocus: true);
            var list = ThemeList(host);
            Assert.That(list.SelectedIndex, Is.Zero);
            var next = (ThemeDefinition)list.Items[1];

            var first = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            first.Focus();
            first.RaiseEvent(
                new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(first)!, 0, Key.Right)
                {
                    RoutedEvent = Keyboard.KeyDownEvent,
                });

            host.Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(list.SelectedValue, Is.EqualTo(next.Key));
                Assert.That(ThemeManager.Current?.Key, Is.EqualTo(next.Key));
                Assert.That(store.GetStringValue(SettingsKeys.Theme), Is.EqualTo(next.Key));
            }
        }
        finally
        {
            ThemeManager.Apply(previousTheme);
            store.SetValue(SettingsKeys.Theme, previousPreference);
            sections.Restore(previousSection);
            Settle();
        }
    }

    private static VisualTestHost ShowAppearance(SettingsViewModel settings, bool keyboardFocus = false)
    {
        var host = VisualTestHost.Show(
            new Window
            {
                Content = new SettingsAppearanceCard { DataContext = settings },
            },
            900,
            550,
            keyboardFocus);
        host.Settle();

        return host;
    }

    private static ListBox ThemeList(VisualTestHost host)
    {
        return Descendants<ListBox>(host.Window).Single(list =>
            list.IsVisible && AutomationProperties.GetName(list) == "Тема оформления");
    }
}
