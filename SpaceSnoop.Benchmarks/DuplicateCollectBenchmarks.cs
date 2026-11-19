using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;
using SpaceSnoop.Wpf.Bootstrap;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class DuplicateCollectBenchmarks
{
    private const long AppMinSize = AppDefaults.ScanDuplicatesMinSizeMbDefault * 1024L * 1024L;

    private DirectorySpace _root = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [Params(1, AppMinSize)]
    public long MinSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = SyntheticTree.BuildSizedScan(Files);
    }

    [Benchmark]
    public int Collect()
    {
        var bySize = new Dictionary<long, List<FileSpace>>();
        var unreadable = DuplicateFinder.Collect(_root, MinSize, bySize, CancellationToken.None);

        return bySize.Count + unreadable;
    }
}
