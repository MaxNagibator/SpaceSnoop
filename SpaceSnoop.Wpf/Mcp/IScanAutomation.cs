using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.Mcp;

public interface IScanAutomation
{
    string SelectedDrive { get; }

    string ResultPath { get; }

    string ResultSizeText { get; }

    string ResultFileCountText { get; }

    string ResultDirCountText { get; }

    string ResultRateText { get; }

    bool IsScanning { get; }

    bool HasResult { get; }

    int MarkedCount { get; }

    TimeSpan LastScanElapsed { get; }

    long MarkedBytes();

    Func<ScanExportModel>? CaptureExportBuilder(int depth, int entryLimit);

    DirectorySpace? CaptureScanRoot();

    SpaceBase? FindForAutomation(string path);

    bool IsScanRoot(SpaceBase space);

    ArchiveRequest CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal);

    int MarkForAutomation(IReadOnlyList<SpaceBase> targets, bool mark);

    void SelectPathForAutomation(string path, bool remember = true);

    void ReleaseBeforeScan(string path);

    void ApplyScanResult(DirectorySpace result, TimeSpan elapsed, PerformanceTraversal? traversal, ScanNotes notes);

    void ApplyScanResult(DirectorySpace result, TimeSpan elapsed, PerformanceTraversal? traversal, ScanNotes notes, DriveCapacity? driveCapacity)
    {
        ApplyScanResult(result, elapsed, traversal, notes);
    }

    Task ReturnScanMemoryAsync(DirectorySpace result, Action<string> reportPhase, CancellationToken cancellationToken);

    Task ScanFromAutomationAsync(string path, CancellationToken cancellationToken);

    Task<ArchiveOutcome> ArchiveFromAutomationAsync(DirectorySpace dir, ArchiveRequest request, CancellationToken cancellationToken);
}
