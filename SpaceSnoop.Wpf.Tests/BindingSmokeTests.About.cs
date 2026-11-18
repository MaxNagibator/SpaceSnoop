using KeepShell.Bootstrap;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Gallery;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.About;
using SpaceSnoop.Wpf.Views.Dialogs;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCase(1100d, 900d, 1d)]
    [TestCase(880d, 800d, 1.6d)]
    public void Страница_О_программе_и_модалка_обновления_помещаются_и_переходят_в_разделы(double width, double height, double fontScale)
    {
        var preferences = _services.GetRequiredService<ShellPreferences>();
        var updater = _services.GetRequiredService<AppUpdateViewModel>();
        var (oldWidth, oldHeight, oldScale) = (_window.Width, _window.Height, preferences.FontScale);

        try
        {
            _window.Width = width;
            _window.Height = height;
            preferences.FontScale = fontScale;

            updater.ApplyPendingForAutomation(
                "v99.9.0",
                "SpaceSnoop-v99.9.0.exe",
                "https://github.com/x/y/releases/download/v99.9.0/SpaceSnoop-v99.9.0.exe",
                "https://github.com/x/y/releases/tag/v99.9.0",
                GalleryFixtures.Changelog());

            Assert.That(_shell.TryNavigate(SectionKey.About), Is.True, "Страница «О программе» не открылась.");
            Settle();

            var aboutView = Descendant<AboutView>(_window);
            Assert.That(aboutView, Is.Not.Null);

            var scrollViewers = Descendants<ScrollViewer>(aboutView!).ToArray();
            var changelogCard = Descendant<AboutChangelogCard>(aboutView!);
            var featureButtons = Descendants<Button>(Descendant<AboutFeaturesCard>(aboutView!)!).ToArray();

            _ = _shell.Modal.ShowAsync(updater.BuildUpdateDialog());
            Settle();

            var modalView = Descendant<ConfirmDialogView>(_window);
            Assert.That(modalView, Is.Not.Null, "Модальное окно обновления не открылось в ModalHost.");

            var choiceCaptions = Descendants<Button>(modalView!).Where(static b => b.IsVisible).Select(static b => b.Content).ToArray();

            _shell.Modal.RequestCancel();
            Settle();

            var cleanupTile = featureButtons.First(static b => AutomationProperties.GetName(b) == "Очистка и разработка");
            cleanupTile.Command.Execute(cleanupTile.CommandParameter);
            Settle();

            using (Assert.EnterMultipleScope())
            {
                Assert.That(scrollViewers, Has.Length.EqualTo(1), "На странице «О программе» остался вложенный ScrollViewer.");
                Assert.That(changelogCard, Is.Not.Null);
                Assert.That(featureButtons, Has.Length.EqualTo(8));
                Assert.That(choiceCaptions, Is.EqualTo(["Не сейчас", "Открыть на GitHub", "Скачать"]));
                Assert.That(_shell.CurrentSectionKey, Is.EqualTo(SectionKey.Cleanup));
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            _shell.Modal.RequestCancel();
            updater.DismissCommand.Execute(null);
            preferences.FontScale = oldScale;
            _window.Width = oldWidth;
            _window.Height = oldHeight;
            Settle();
        }
    }

    [TestCase(AppThemes.LightKey)]
    [TestCase(AppThemes.DarkKey)]
    [TestCase(AppThemes.TarkovKey)]
    [TestCase(AppThemes.SublimeKey)]
    public void Плитка_возможностей_О_программе_разрешает_токен_фона_при_наведении(string theme)
    {
        try
        {
            ThemeManager.Apply(theme);
            Assert.That(_shell.TryNavigate(SectionKey.About), Is.True);
            Settle();

            var aboutView = Descendant<AboutView>(_window)!;
            var tile = Descendants<Button>(Descendant<AboutFeaturesCard>(aboutView)!).First();
            var hoverTrigger = tile.Template.Triggers
                .OfType<Trigger>()
                .Single(static trigger => trigger.Property == UIElement.IsMouseOverProperty && Equals(trigger.Value, true));
            var backgroundSetter = hoverTrigger.Setters
                .OfType<Setter>()
                .Single(static setter => setter.TargetName == "CardBorder" && setter.Property == Border.BackgroundProperty);

            Assert.That(backgroundSetter.Value, Is.InstanceOf<DynamicResourceExtension>());
            var resourceKey = ((DynamicResourceExtension)backgroundSetter.Value).ResourceKey;
            var hoverBrush = tile.TryFindResource(resourceKey) as SolidColorBrush;
            var expectedBrush = tile.TryFindResource("Bg.Surface") as SolidColorBrush;
            var parentCard = Ancestor<Border>(tile) ?? throw new InvalidOperationException("Родительская карточка возможностей не найдена.");
            var parentBrush = parentCard.Background as SolidColorBrush;
            var restingBrush = tile.Background as SolidColorBrush;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(resourceKey, Is.EqualTo("Bg.Surface"));
                Assert.That(hoverBrush, Is.Not.Null, "Токен фона наведения отсутствует в активной теме – плитка теряет заливку.");
                Assert.That(expectedBrush, Is.Not.Null);
                Assert.That(parentBrush, Is.Not.Null);
                Assert.That(restingBrush, Is.Not.Null);
                Assert.That(hoverBrush!.Color, Is.EqualTo(expectedBrush!.Color));
                Assert.That(hoverBrush.Color, Is.Not.EqualTo(restingBrush!.Color));
                Assert.That(hoverBrush.Color, Is.Not.EqualTo(parentBrush!.Color), "Фон наведения плитки не должен сливаться с родительской карточкой Card.Inset.");
                Assert.That(_sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, _sink.Errors));
            }
        }
        finally
        {
            ThemeManager.Apply(AppThemes.LightKey);
            Settle();
        }
    }
}
