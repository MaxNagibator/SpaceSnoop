using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class AggregateTotalsBenchmarks
{
    private const int SampleCount = 256;

    private SampleFiles _samples = null!;
    private DirectorySpace _root = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _root = SyntheticTree.BuildScan(_samples.Files, Files);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _samples.Dispose();
    }

    [Benchmark]
    public long Aggregate()
    {
        _root.AggregateTotals();
        return _root.TotalSize;
    }
}
