using BenchmarkDotNet.Attributes;
using SpaceSnoop.Wpf.Views.Scan;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class TreemapLayoutBenchmarks
{
    private const int Seed = 20260805;
    private const double CanvasWidth = 1280;
    private const double CanvasHeight = 720;

    private double[] _weights = [];

    [Params(50, 500, 5000)]
    public int Tiles { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(Seed);

        _weights = new double[Tiles];

        for (var i = 0; i < Tiles; i++)
        {
            _weights[i] = Math.Pow(10, random.NextDouble() * 6);
        }
    }

    [Benchmark]
    public LayoutRect[] Squarify()
    {
        return TreemapLayout.Squarify(_weights, CanvasWidth, CanvasHeight);
    }
}
