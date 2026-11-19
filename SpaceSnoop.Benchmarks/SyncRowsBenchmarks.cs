using BenchmarkDotNet.Attributes;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class SyncRowsBenchmarks
{
    private readonly BenchSyncRowHost _host = new();
    private readonly HashSet<DirectoryComparison> _collapsed = [];

    private ComparisonResult _result = null!;
    private Dictionary<DirectoryComparison, (long Left, long Right)> _dirSizes = null!;

    [Params(10_000, 200_000)]
    public int Files { get; set; }

    [Params(false, true)]
    public bool FlatView { get; set; }

    [Params(false, true)]
    public bool CollapseAll { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _result = SyntheticTree.BuildComparison(Files);
        _result.ApplyMode(SyncMode.Bidirectional, true);
        _dirSizes = SyncRowsProjector.BuildDirSizeCache(_result.Root);
    }

    [Benchmark]
    public int Build()
    {
        if (CollapseAll)
        {
            SyncRowsProjector.CollapseAllDirectories(_collapsed, _result.Root);
        }

        var request = new SyncRowsRequest
        {
            Result = _result,
            FlatView = FlatView,
            ShowIdentical = true,
            DirSizeCache = _dirSizes,
            Collapsed = _collapsed,
            GroupFolders = AppDefaults.SyncGroupFoldersDefault,
        };

        return SyncRowsProjector.Build(request, _host).Count;
    }
}
