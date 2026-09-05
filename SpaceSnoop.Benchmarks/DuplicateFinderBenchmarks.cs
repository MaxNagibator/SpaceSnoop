using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Duplicates;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class DuplicateFinderBenchmarks
{
    private const int Files = 512;
    private const int FileSize = 32 * 1024;

    private readonly DuplicateFinder _finder = new();

    private string _root = null!;
    private DirectorySpace _tree = null!;
    private DuplicateOptions _options;

    [Params(4, 16)]
    public int GroupSize { get; set; }

    [Params(1, 8)]
    public int Parallelism { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), $"SpaceSnoopDupBench_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);

        var random = new Random(SyntheticTree.Seed);
        var content = Array.Empty<byte>();

        for (var index = 0; index < Files; index++)
        {
            if (index % GroupSize == 0)
            {
                content = new byte[FileSize + (index / GroupSize)];
                random.NextBytes(content);
            }

            File.WriteAllBytes(Path.Combine(_root, $"file{index}.bin"), content);
        }

        var directory = new DirectoryInfo(_root);
        _tree = new DiskSpaceCalculator().Calculate(directory, CancellationToken.None);
        _tree.FixAbsolutePath(directory);
        _options = DuplicateOptions.Default with { MaxParallelism = Parallelism };
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Benchmark]
    public DuplicateReport Find()
    {
        return _finder.Find(_tree, _options, null, CancellationToken.None);
    }
}
