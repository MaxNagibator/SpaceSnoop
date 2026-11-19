using KeepShell.Bootstrap;
using KeepShell.Testing;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class RescanPromptTests
{
    private static readonly string Scanned = Path.Combine(Path.GetTempPath(), "SpaceSnoopRescan", "scanned");
    private static readonly string Other = Path.Combine(Path.GetTempPath(), "SpaceSnoopRescan", "other");

    [Test]
    public void Диалог_ставит_отказ_первым_а_Enter_сканирует_заново()
    {
        var confirm = RescanPrompt.BuildConfirm(Scanned);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(confirm.Choices[0].Kind, Is.EqualTo(ConfirmChoiceKind.Dismissive));
            Assert.That(confirm.Choices.Any(static choice => choice.IsDestructive), Is.False);
            Assert.That(confirm.TryAccept(), Is.True);
            Assert.That(confirm.Chosen?.Caption, Is.EqualTo("Сканировать заново"));
            Assert.That(confirm.Summary, Is.EqualTo(Scanned));
        }
    }

    [TestCase(0, false, true, TestName = "Отказ в диалоге – скан не стартует")]
    [TestCase(2, true, true, TestName = "Согласие – скан стартует, предупреждение остаётся")]
    [TestCase(1, true, false, TestName = "Больше не спрашивать – скан стартует, предупреждение выключено")]
    public async Task Ответ_в_диалоге_решает_старт_и_судьбу_предупреждения(int choiceIndex, bool expectProceed, bool expectConfirmAfter)
    {
        var (preferences, settings) = Preferences(true, true);
        var roots = Roots(preferences, false);
        var dialogs = new NoopDialogs(
            show: viewModel =>
            {
                var confirm = (ConfirmDialogViewModel)viewModel;
                var choice = confirm.Choices[choiceIndex];
                confirm.ChooseCommand.Execute(choice);

                return !choice.IsDismissive;
            });

        var proceed = await new RescanPrompt(dialogs, preferences).ConfirmAsync(roots, Scanned, false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(proceed, Is.EqualTo(expectProceed));
            Assert.That(preferences.ConfirmRescan, Is.EqualTo(expectConfirmAfter));
            Assert.That(((ISettingsStore)settings).GetBool(SettingsKeys.ScanConfirmRescan, true), Is.EqualTo(expectConfirmAfter));
        }
    }

    [Test]
    public async Task Пометки_в_другом_дереве_не_гасят_предупреждение()
    {
        var (preferences, _) = Preferences(true, true);
        var factory = new ScanNodeFactory(preferences, new(new MemorySettings()), new FakeShellLauncher());
        var marked = new DirectorySpace(Other, null, DateTime.Now, DateTime.Now);
        marked.Delete();
        var roots = new[]
        {
            factory.CreateRoot(new DirectorySpace(Scanned, null, DateTime.Now, DateTime.Now), new()), factory.CreateRoot(marked, new()),
        };

        var dialogs = new NoopDialogs(showResult: true);

        await new RescanPrompt(dialogs, preferences).ConfirmAsync(roots, Scanned, true);

        Assert.That(dialogs.ShowCount, Is.EqualTo(1));
    }

    [TestCase(false, false, false, true, true, true, TestName = "Тот же путь без пометок – предупреждение")]
    [TestCase(true, false, false, true, true, true, TestName = "Тот же путь с разделителем на конце – предупреждение")]
    [TestCase(false, true, false, true, true, false, TestName = "Другой путь – без предупреждения")]
    [TestCase(false, false, true, true, true, false, TestName = "Пометки в дереве этой папки – без предупреждения")]
    [TestCase(false, false, false, false, true, false, TestName = "Снятие прежнего результата выключено – без предупреждения")]
    [TestCase(false, false, false, true, false, false, TestName = "Предупреждение выключено в настройках – без предупреждения")]
    public async Task Предупреждение_только_когда_прежний_результат_будет_снят(
        bool trailingSeparator,
        bool otherPath,
        bool markedRoot,
        bool release,
        bool confirm,
        bool expectAsk)
    {
        var (preferences, _) = Preferences(release, confirm);
        var roots = Roots(preferences, markedRoot);
        var dialogs = new NoopDialogs(showResult: true);
        var prompt = new RescanPrompt(dialogs, preferences);
        var path = (otherPath, trailingSeparator) switch
        {
            (true, _) => Other,
            (_, true) => Scanned + Path.DirectorySeparatorChar,
            _ => Scanned,
        };

        var proceed = await prompt.ConfirmAsync(roots, path, markedRoot);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialogs.ShowCount, Is.EqualTo(expectAsk ? 1 : 0));
            Assert.That(proceed, Is.True);
        }
    }

    private static (ScanPreferences Preferences, MemorySettings Settings) Preferences(bool release, bool confirm)
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.ScanReleaseBeforeRescan, release ? "true" : "false");
        settings.SetValue(SettingsKeys.ScanConfirmRescan, confirm ? "true" : "false");

        return (new(settings), settings);
    }

    private static List<ScanNodeViewModel> Roots(ScanPreferences preferences, bool markedRoot)
    {
        var factory = new ScanNodeFactory(preferences, new(new MemorySettings()), new FakeShellLauncher());
        var space = new DirectorySpace(Scanned, null, DateTime.Now, DateTime.Now);

        if (markedRoot)
        {
            space.Delete();
        }

        return [factory.CreateRoot(space, new())];
    }
}
