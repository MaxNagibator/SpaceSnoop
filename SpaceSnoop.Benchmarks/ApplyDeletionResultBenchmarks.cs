using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
[InvocationCount(1)]
public class ApplyDeletionResultBenchmarks
{
    private const int SampleCount = 256;

    private SampleFiles _samples = null!;
    private BenchShell _shell = null!;
    private List<SpaceBase> _marked = null!;
    private ScanMarksViewModel _marks = null!;

    [Params(200_000)]
    public int Files { get; set; }

    [Params(4_314)]
    public int Marked { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _shell = new();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shell.Dispose();
        _samples.Dispose();
    }

    [IterationSetup]
    public void BuildTree()
    {
        var root = SyntheticTree.BuildScan(_samples.Files, Files);
        _marked = SyntheticTree.MarkEvenly(root, Marked);

        var factory = new ScanNodeFactory(new(_shell.Settings), new(_shell.Settings), new BenchShellLauncher());
        var rootNode = factory.Create(root, root.TotalSize, root.TotalSize, root, _shell.Sort);
        rootNode.ReloadChildren();

        foreach (var child in rootNode.Children)
        {
            child.ReloadChildren();
        }

        var roots = new ObservableCollection<ScanNodeViewModel>
        {
            rootNode,
        };

        var summary = new ScanSummaryViewModel();
        summary.Apply(root, TimeSpan.Zero);

        var treemap = new ScanTreemapViewModel(roots);
        treemap.SetRoot(rootNode);

        _marks = new(
            new BenchDialogService(),
            new(
                _shell.Monitor,
                _shell.Operations,
                _shell.Preferences,
                _shell.Dispatcher,
                NullLogger<DeleteProgressDialogViewModel>.Instance),
            new(_shell.Settings),
            NullLogger.Instance,
            factory,
            roots,
            summary,
            treemap,
            static () => null,
            static _ =>
            {
            },
            static () =>
            {
            },
            static () => false,
            static () =>
            {
            });
    }

    [Benchmark(Baseline = true)]
    public void RemoveMarked()
    {
        foreach (var item in _marked)
        {
            if (item.Parent is DirectorySpace parent)
            {
                parent.Remove(item);
            }
        }
    }

    [Benchmark]
    public void ApplyDeletionResult()
    {
        _marks.ApplyDeletionResult(_marked);
    }
}
