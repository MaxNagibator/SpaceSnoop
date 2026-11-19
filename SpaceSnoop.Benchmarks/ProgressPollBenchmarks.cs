using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Benchmarks;

[MemoryDiagnoser]
public class ProgressPollBenchmarks
{
    private const int ReportsPerTick = 1_000;
    private const int PathCount = 256;
    private const long BytesPerReport = 64 * 1024;
    private const string Caption = "Вычисление хешей:";

    private static readonly TimeSpan Elapsed = TimeSpan.FromSeconds(30);

    private BenchShell _shell = null!;
    private SyncSessionViewModel _session = null!;
    private string[] _paths = null!;

    [Params(10_000, 500_000)]
    public int Reports { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _shell = new();
        _session = new(
            new BenchDialogService(),
            NullLogger.Instance,
            new(new(), _shell.Preferences),
            _shell.Operations,
            _shell.Dispatcher,
            static _ =>
            {
            });

        _paths = new string[PathCount];

        for (var i = 0; i < _paths.Length; i++)
        {
            _paths[i] = $@"D:\bench\dir{i % 6:D2}\sub{i % 11:D2}\file{i:D4}.bin";
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _shell.Dispose();
    }

    [Benchmark]
    public string? ReportAndPoll()
    {
        var state = new OperationProgressState();
        var totalBytes = Reports * BytesPerReport;

        for (var i = 1; i <= Reports; i++)
        {
            state.Report(new(i, _paths[i % _paths.Length], i * BytesPerReport));

            if (i % ReportsPerTick == 0)
            {
                _session.ShowBusyForAutomation(Caption, state.CreateSnapshot(), Elapsed, Reports, totalBytes);
            }
        }

        _session.ShowBusyForAutomation(Caption, state.CreateSnapshot(), Elapsed, Reports, totalBytes);

        return _session.ProgressDetail;
    }
}
