using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class DeleteDialogRowsBenchmarks
{
    private const int SampleCount = 256;

    private SampleFiles _samples = null!;
    private BenchShell _shell = null!;
    private List<SpaceBase> _items = null!;

    [Params(4_314, 50_000)]
    public int Items { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _shell = new();
        _items = SyntheticTree.CollectFiles(SyntheticTree.BuildScan(_samples.Files, Items));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shell.Dispose();
        _samples.Dispose();
    }

    [Benchmark]
    public int CreateDialog()
    {
        var dialog = new DeleteProgressDialogViewModel(
            _items,
            false,
            _shell.Monitor,
            _shell.Operations,
            _shell.Preferences,
            _shell.Dispatcher,
            NullLogger.Instance);

        return dialog.Items.Count;
    }
}
