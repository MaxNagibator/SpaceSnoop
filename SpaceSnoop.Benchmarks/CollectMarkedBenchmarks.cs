using BenchmarkDotNet.Attributes;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class CollectMarkedBenchmarks
{
    private const int SampleCount = 256;
    private const int Marked = 4_314;

    private SampleFiles _samples = null!;
    private BenchShell _shell = null!;
    private ScanNodeViewModel[] _roots = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _shell = new();

        var root = SyntheticTree.BuildScan(_samples.Files, Files);
        SyntheticTree.MarkEvenly(root, Marked);
        _roots = [_shell.CreateNode(root)];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shell.Dispose();
        _samples.Dispose();
    }

    [Benchmark]
    public int CollectMarked()
    {
        return ScanTreeEditor.CollectMarked(_roots).Count;
    }
}
