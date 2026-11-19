using KeepShell.Services.Modal;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Windows;

namespace SpaceSnoop.Wpf.Tests;

public partial class BindingSmokeTests
{
    [TestCaseSource(nameof(DialogModels))]
    public void Модальная_модель_имеет_загружаемое_представление(Type model)
    {
        var template = _window.TryFindResource(new DataTemplateKey(model)) as DataTemplate;

        Assert.That(template, Is.Not.Null, "Модальный хост покажет имя типа вместо диалога.");
        Assert.That(template!.LoadContent(), Is.InstanceOf<FrameworkElement>());
    }

    private static IEnumerable<Type> DialogModels()
    {
        return typeof(ConfirmDialogViewModel).Assembly.GetTypes()
            .Where(static type => type.IsClass && !type.IsAbstract && typeof(IDialogViewModel).IsAssignableFrom(type));
    }
}
