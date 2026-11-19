using KeepShell.Testing;
using KeepShell.ViewModels;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AdminRestartPromptTests
{
    [TestCase(0, false, true, 0, 1, 0, true, TestName = "Без пометок перезапуск идёт без вопроса")]
    [TestCase(3, false, true, 1, 0, 0, false, TestName = "С пометками и отказом в диалоге перезапуска нет")]
    [TestCase(3, true, true, 1, 1, 0, true, TestName = "С пометками и согласием в диалоге перезапуск идёт")]
    [TestCase(0, false, false, 0, 1, 1, false, TestName = "Отказ в окне UAC показывает тост")]
    public async Task Команда_перезапуска_спрашивает_только_при_пометках_и_сообщает_об_отказе_UAC(
        int marked,
        bool confirmed,
        bool elevationGranted,
        int expectedDialogs,
        int expectedRestarts,
        int expectedToasts,
        bool expectedRestarted)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.EnableToastNotifications, "true");
        var toasts = new ToastHostViewModel();
        var dialogs = new NoopDialogs(showResult: confirmed);
        var restarts = 0;
        var prompt = new AdminRestartPrompt(
            dialogs,
            new(toasts, new(settings)),
            () => marked,
            () =>
            {
                restarts++;
                return elevationGranted;
            });

        bool? restarted = null;
        var summary = new ScanSummaryViewModel(false, async () => restarted = await prompt.RunAsync());

        await summary.RestartAsAdminCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(restarted, Is.EqualTo(expectedRestarted));
            Assert.That(dialogs.ShowCount, Is.EqualTo(expectedDialogs));
            Assert.That(restarts, Is.EqualTo(expectedRestarts));
            Assert.That(toasts.Toasts, Has.Count.EqualTo(expectedToasts));
        }
    }

    [Test]
    public void Подтверждение_ставит_отказ_первым_и_предупреждает_о_пропаже_пометок()
    {
        var confirm = AdminRestartPrompt.BuildConfirm(3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(confirm.Choices[0].Kind, Is.EqualTo(ConfirmChoiceKind.Dismissive));
            Assert.That(confirm.Choices[1].Kind, Is.EqualTo(ConfirmChoiceKind.Destructive));
            Assert.That(confirm.TryAccept(), Is.False);
            Assert.That(confirm.Warning, Does.Contain("3 объекта не уйдут в корзину"));
        }
    }
}
