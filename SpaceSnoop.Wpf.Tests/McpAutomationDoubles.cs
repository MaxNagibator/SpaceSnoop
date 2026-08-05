using SpaceSnoop.Core.Domain;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.Mcp;
using SpaceSnoop.Wpf.ViewModels.Sync;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.Tests;

internal sealed class ScanAutomationDouble : IScanAutomation
{
    public Func<bool> ScanningProbe { get; set; } = static () => false;

    public string SelectedDrive { get; set; } = string.Empty;

    public string ResultPath { get; set; } = string.Empty;

    public string ResultSizeText { get; set; } = string.Empty;

    public string ResultFileCountText { get; set; } = string.Empty;

    public string ResultDirCountText { get; set; } = string.Empty;

    public string ResultRateText { get; set; } = string.Empty;

    public bool HasResult { get; set; }

    public int MarkedCount { get; set; }

    public TimeSpan LastScanElapsed { get; set; }

    public long MarkedByteCount { get; set; }

    public Func<ScanExportModel>? ExportBuilder { get; set; }

    public SpaceBase? Found { get; set; }

    public bool RootMatches { get; set; }

    public int MarkCalls { get; private set; }

    public int SelectCalls { get; private set; }

    public int ApplyCalls { get; private set; }

    public PerformanceTraversal? AppliedTraversal { get; private set; }

    public int ScanCalls { get; private set; }

    public int ArchiveCalls { get; private set; }

    public bool IsScanning => ScanningProbe();

    public long MarkedBytes()
    {
        return MarkedByteCount;
    }

    public Func<ScanExportModel>? CaptureExportBuilder(int depth, int entryLimit)
    {
        return ExportBuilder;
    }

    public SpaceBase? FindForAutomation(string path)
    {
        return Found;
    }

    public bool IsScanRoot(SpaceBase space)
    {
        return RootMatches;
    }

    public ArchiveRequest CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal)
    {
        return new(dir.AbsolutePath, dir.AbsolutePath + ".zip", 0, 0, deleteOriginal, CompressionLevel.Fastest, false);
    }

    public int MarkForAutomation(IReadOnlyList<SpaceBase> targets, bool mark)
    {
        MarkCalls++;
        return targets.Count;
    }

    public void SelectPathForAutomation(string path)
    {
        SelectCalls++;
    }

    public void ApplyScanResult(string path, DirectorySpace result, TimeSpan elapsed, PerformanceTraversal? traversal)
    {
        ApplyCalls++;
        AppliedTraversal = traversal;
    }

    public Task ScanFromAutomationAsync(string path, CancellationToken cancellationToken)
    {
        ScanCalls++;
        return Task.CompletedTask;
    }

    public Task<ArchiveOutcome> ArchiveFromAutomationAsync(DirectorySpace dir, ArchiveRequest request, CancellationToken cancellationToken)
    {
        ArchiveCalls++;
        return Task.FromResult(new ArchiveOutcome(request.TargetPath, false, "Готово"));
    }
}

internal sealed class CleanupAutomationDouble : ICleanupAutomation
{
    public bool IsBusy { get; set; }

    public bool IsModalBusy { get; set; }

    public CleanupOutcome Outcome { get; set; } = new(CleanupConsent.Granted, 1024, 3, 0, false, "Готово");

    public int CleanCalls { get; private set; }

    public IReadOnlyList<string> LastIds { get; private set; } = [];

    public Task<CleanupOutcome> CleanFromAutomationAsync(IReadOnlyList<string> targetIds, CancellationToken cancellationToken)
    {
        CleanCalls++;
        LastIds = targetIds;

        return Task.FromResult(Outcome);
    }
}

internal sealed class SyncAutomationDouble : ISyncAutomation
{
    public string LeftPath { get; set; } = string.Empty;

    public string RightPath { get; set; } = string.Empty;

    public SyncMode Mode { get; set; }

    public SyncWinner Winner { get; set; }

    public bool Mirror { get; set; }

    public string Exclusions { get; set; } = string.Empty;

    public bool IsBusy { get; set; }

    public bool HasComparison { get; set; }

    public IReadOnlyList<string> IncompleteDirectories { get; set; } = [];

    public bool HasPendingConflicts { get; set; }

    public int IdenticalCount { get; set; }

    public int LeftOnlyCount { get; set; }

    public int RightOnlyCount { get; set; }

    public int ModifiedCount { get; set; }

    public int ConflictCount { get; set; }

    public string SummaryText { get; set; } = string.Empty;

    public Func<ComparisonExportModel>? ExportBuilder { get; set; }

    public Func<SyncPlanExportModel>? PlanBuilder { get; set; }

    public SyncRunResult? RunResult { get; set; }

    public int CompareCalls { get; private set; }

    public int SyncCalls { get; private set; }

    public Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit)
    {
        return ExportBuilder;
    }

    public Func<SyncPlanExportModel>? CapturePlanBuilder(int entryLimit)
    {
        return PlanBuilder;
    }

    public Task CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        CompareCalls++;
        return Task.CompletedTask;
    }

    public Task<SyncRunResult?> SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        SyncCalls++;
        return Task.FromResult(RunResult);
    }
}
