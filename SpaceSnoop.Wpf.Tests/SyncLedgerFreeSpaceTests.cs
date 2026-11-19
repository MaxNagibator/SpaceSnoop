using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class SyncLedgerFreeSpaceTests
{
    [Test]
    public async Task Свободное_место_приёмника_приходит_в_подсказку_после_чтения_в_фоне()
    {
        var page = CreatePage();
        var root = Path.GetPathRoot(Path.GetTempPath())!;
        var left = Path.Combine(root, "SpaceSnoopMissing", "Left");
        var right = Path.Combine(root, "SpaceSnoopMissing", "Right");
        var comparison = new DirectoryComparison(string.Empty, string.Empty);
        comparison.Files.Add(
            new("a.bin", "a.bin")
            {
                Status = ComparisonStatus.LeftOnly,
                LeftSize = 1024,
            });

        var hinted = new TaskCompletionSource();
        page.Ledger.PropertyChanged += OnLedgerChanged;
        page.Setup.LeftPath = left;
        page.Setup.RightPath = right;
        page.Operations.AdoptComparison(new(left, right, comparison));

        await hinted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.That(page.Ledger.PlanVolumeHint, Does.Contain("Свободно").And.Not.Contain("неизвестно"));

        void OnLedgerChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SyncLedgerViewModel.PlanVolumeHint) && !page.Ledger.PlanVolumeHint.Contains("неизвестно"))
            {
                hinted.TrySetResult();
            }
        }
    }

    private static SyncViewModel CreatePage()
    {
        var settings = new MemorySettings();
        var shell = new ShellPreferences(settings);

        return new(
            settings,
            new NoopDialogs(),
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
