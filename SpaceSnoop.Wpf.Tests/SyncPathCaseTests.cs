using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncPathCaseTests
{
    private const string Left = @"C:\Sync\Left";
    private const string Right = @"C:\Sync\Right";

    [TestCase(@"C:\Sync\Left ", true)]
    [TestCase(@"C:\Sync\left", false)]
    public void Сравнение_переживает_только_ту_правку_пути_которая_не_меняет_ни_одного_символа(string entered, bool kept)
    {
        var page = CreatePage();
        page.Setup.LeftPath = Left;
        page.Setup.RightPath = Right;
        page.Operations.AdoptComparison(new(Left, Right, new(string.Empty, string.Empty)));

        page.Setup.LeftPath = entered;

        Assert.That(page.Operations.Result, kept ? Is.Not.Null : Is.Null);
    }

    [TestCase("bin ", true)]
    [TestCase("bin,obj", false)]
    [TestCase("", false)]
    public void Сравнение_сбрасывается_правкой_исключений_после_него(string entered, bool kept)
    {
        var page = CreatePage();
        page.Setup.LeftPath = Left;
        page.Setup.RightPath = Right;
        page.Setup.Exclusions = "bin";
        page.Operations.AdoptComparison(new(Left, Right, new(string.Empty, string.Empty)));

        page.Setup.Exclusions = entered;

        Assert.That(page.Operations.Result, kept ? Is.Not.Null : Is.Null);
    }

    private static SyncViewModel CreatePage()
    {
        var settings = new MemorySettings();
        var dialogs = new NoopDialogs();
        var shell = new ShellPreferences(settings);

        return new(
            settings,
            dialogs,
            new(settings),
            new(settings),
            NullLogger<SyncViewModel>.Instance,
            new(NullLogger<DirectoryComparer>.Instance),
            new(NullLogger<SyncEngine>.Instance),
            new(new(), shell),
            new(TestDiagnostics.Monitor()),
            new FakeFilePicker(),
            new FakeUiDispatcher(),
            new FakeAppNavigator());
    }
}
