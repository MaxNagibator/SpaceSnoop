using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using KeepShell.Views.Controls;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Platform;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Settings;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [Test]
    public void Настройки_находятся_по_ключам_и_именам_зарегистрированных_тем()
    {
        var appearance = _services.GetRequiredService<SettingsViewModel>().Sections["appearance"];
        foreach (var theme in ThemeManager.Themes)
        {
            Assert.That(appearance.Matches(SettingsSection.ParseQuery(theme.Key)), Is.True);
            Assert.That(appearance.Matches(SettingsSection.ParseQuery(theme.DisplayName)), Is.True);
        }
    }

    [Test]
    public void Кнопки_тем_в_шапке_показывают_темы_из_реестра_и_сохраняют_выбор()
    {
        var store = _services.GetRequiredService<ISettingsStore>();
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var (width, height, scale) = (_window.Width, _window.Height, preferences.FontScale);
        var previousTheme = ThemeManager.Current!.Key;
        var previousPreference = store.GetStringValue(SettingsKeys.Theme) ?? string.Empty;
        var title = Descendants<TitleBarView>(_window).Single();
        var switcher = Descendants<ThemeSwitcher>(title).Single();
        var list = Descendants<ListBox>(switcher).Single();

        try
        {
            _window.Width = SmallWindowWidth;
            _window.Height = SmallWindowHeight;
            preferences.FontScale = FontScaleManager.MaxScale;
            _shell.Theme.Select(AppThemes.SystemKey);
            Settle();

            var options = list.Items.Cast<ThemeDefinition>().ToArray();
            Assert.That(options.Select(option => option.Key), Is.EqualTo(ThemeManager.Themes.Select(theme => theme.Key)));
            foreach (var option in options)
            {
                var button = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(option);
                var icon = Descendants<PackIconLucide>(button).Single();
                var right = button.TransformToAncestor(title).Transform(new Point(button.ActualWidth, 0)).X;

                Assert.That(button.IsVisible, Is.True);
                Assert.That(right, Is.LessThanOrEqualTo(title.ActualWidth));
                Assert.That(AutomationProperties.GetName(button), Is.EqualTo(option.DisplayName));
                Assert.That(icon.IsVisible, Is.True);
                Assert.That(icon.Kind, Is.EqualTo(option.Icon));
            }

            var darkOption = options.Single(option => option.Key == AppThemes.DarkKey);
            var dark = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(darkOption);
            dark.SetCurrentValue(ListBoxItem.IsSelectedProperty, true);
            Settle();

            Assert.That(ThemeManager.Current?.Key, Is.EqualTo(AppThemes.DarkKey));
            Assert.That(store.GetStringValue(SettingsKeys.Theme), Is.EqualTo(AppThemes.DarkKey));

            dark.SetCurrentValue(ListBoxItem.IsSelectedProperty, false);
            Settle();
            Assert.That(dark.IsSelected, Is.True);

            _shell.Theme.Select(AppThemes.LightKey);
            Settle();
            Assert.That(list.SelectedValue, Is.EqualTo(AppThemes.LightKey));
        }
        finally
        {
            ThemeManager.Apply(previousTheme);
            store.SetValue(SettingsKeys.Theme, previousPreference);
            preferences.FontScale = scale;
            _window.Width = width;
            _window.Height = height;
            Settle();
        }
    }

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
