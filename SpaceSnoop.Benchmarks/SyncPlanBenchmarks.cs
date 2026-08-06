using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class SyncPlanBenchmarks
{
    private ComparisonResult _result = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [Params(SyncWinner.Newest, SyncWinner.Right)]
    public SyncWinner Winner { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _result = SyntheticTree.BuildComparison(Files);
        _result.ApplyMode(SyncMode.Bidirectional, mirror: true, Winner);
    }

    [Benchmark]
    public void ReapplyMode()
    {
        _result.ApplyMode(SyncMode.Bidirectional, mirror: true, Winner);
    }

    [Benchmark]
    public PlannedActions CountPlannedActions()
    {
        return _result.CountPlannedActions();
    }
}
