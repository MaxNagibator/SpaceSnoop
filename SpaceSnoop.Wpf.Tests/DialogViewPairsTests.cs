using KeepShell.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

public class DialogViewPairsTests
{
    public static IEnumerable<Type> DialogViewModels => typeof(ConfirmDialogViewModel).Assembly
        .GetTypes()
        .Where(x => !x.IsAbstract && x.Name.EndsWith("DialogViewModel", StringComparison.Ordinal));

    [TestCaseSource(nameof(DialogViewModels))]
    public void Каждой_модели_диалога_отвечает_представление(Type model)
    {
        var contract = typeof(IView<>).MakeGenericType(model);

        var view = model.Assembly.GetTypes().FirstOrDefault(x => !x.IsAbstract && contract.IsAssignableFrom(x));

        Assert.That(view, Is.Not.Null, $"Для {model.Name} нет представления: окно покажет имя типа вместо диалога.");
    }
}
