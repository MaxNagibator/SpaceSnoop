using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class ScanExportTopKBenchmarks
{
    private DirectorySpace _root = null!;

    [Params(200_000, 3_500_000)]
    public int Files { get; set; }

    [Params(20, ScanExport.DefaultEntryLimit)]
    public int Limit { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = SyntheticTree.BuildSizedScan(Files);
        _root.AggregateTotals();
    }

    [Benchmark]
    public ScanExportModel Build()
    {
        return ScanExport.Build(_root, _root.AbsolutePath, new(ScanExport.MaxDepth, true, 16), entryLimit: Limit);
    }
}
