using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class ScanExportBenchmarks
{
    private const int SampleCount = 256;
    private const int Parallelism = 16;
    private const string RootPath = "bench-root";

    private SampleFiles _samples = null!;
    private DirectorySpace _root = null!;
    private ScanExportOptions _options = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [Params(ScanExport.DefaultDepth, ScanExport.MaxDepth)]
    public int Depth { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _samples = SampleFiles.Create(SampleCount);
        _root = SyntheticTree.BuildScan(_samples.Files, Files);
        _options = new(Depth, true, Parallelism);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _samples.Dispose();
    }

    [Benchmark]
    public ScanExportModel Build()
    {
        return ScanExport.Build(_root, RootPath, _options, entryLimit: ScanExport.DefaultEntryLimit);
    }
}
