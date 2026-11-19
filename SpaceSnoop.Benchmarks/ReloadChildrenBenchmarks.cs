using BenchmarkDotNet.Attributes;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class ReloadChildrenBenchmarks
{
    private const int SampleCount = 256;

    private SampleFiles _samples = null!;
    private BenchShell _shell = null!;
    private ScanNodeViewModel _node = null!;

    [Params(1_000, 50_000)]
    public int Children { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _shell = new();
        _node = _shell.CreateNode(SyntheticTree.BuildFlatScan(_samples.Files, Children));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shell.Dispose();
        _samples.Dispose();
    }

    [Benchmark]
    public int ReloadChildren()
    {
        _node.ReloadChildren();
        return _node.Children.Count;
    }
}
