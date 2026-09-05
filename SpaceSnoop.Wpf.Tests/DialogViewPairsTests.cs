using KeepShell.Testing.Rules;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

public class DialogViewPairsTests
{
    [Test]
    public void Каждой_модели_диалога_отвечает_представление()
    {
        var offenders = ViewPairRule.MissingViews(typeof(ConfirmDialogViewModel).Assembly, ViewPairRule.IsDialogViewModel);

        Assert.That(offenders,
            Is.Empty,
            () => $"Без пары модальный хост покажет имя типа вместо диалога:{Environment.NewLine}{RuleViolation.Report(offenders)}");
    }
}
