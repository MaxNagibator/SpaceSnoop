using BenchmarkDotNet.Attributes;
using SpaceSnoop.Wpf.Diff;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class TextDiffBenchmarks
{
    private const int Seed = 20260805;

    private string[] _left = [];
    private string[] _right = [];

    [Params(1_000, 10_000)]
    public int Lines { get; set; }

    [Params(10, 200)]
    public int Changed { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(Seed);

        _left = new string[Lines];

        for (var i = 0; i < Lines; i++)
        {
            _left[i] = $"    var value{i} = Compute({random.Next(1_000)}, \"{Guid.Empty}\");";
        }

        _right = [.. _left];

        var step = Math.Max(1, Lines / Changed);

        for (var i = 0; i < Lines; i += step)
        {
            _right[i] = _right[i].Replace("Compute(", "Recompute(", StringComparison.Ordinal);
        }
    }

    [Benchmark]
    public int Compute()
    {
        return TextDiff.Compute(_left, _right).Count;
    }
}
