using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel : IScanAutomation
{
    string IScanAutomation.ResultPath => Summary.ResultPath;

    string IScanAutomation.ResultSizeText => Summary.ResultSizeText;

    string IScanAutomation.ResultFileCountText => Summary.ResultFileCountText;

    string IScanAutomation.ResultDirCountText => Summary.ResultDirCountText;

    string IScanAutomation.ResultRateText => Summary.ResultRateText;

    TimeSpan IScanAutomation.LastScanElapsed => LastScanElapsed;

    long IScanAutomation.MarkedBytes()
    {
        return MarkedBytes();
    }

    Func<ScanExportModel>? IScanAutomation.CaptureExportBuilder(int depth, int entryLimit)
    {
        return CaptureExportBuilder(depth, entryLimit);
    }

    SpaceBase? IScanAutomation.FindForAutomation(string path)
    {
        return FindForAutomation(path);
    }

    bool IScanAutomation.IsScanRoot(SpaceBase space)
    {
        return IsScanRoot(space);
    }

    ArchiveRequest IScanAutomation.CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal)
    {
        return CreateArchiveRequest(dir, deleteOriginal);
    }

    int IScanAutomation.MarkForAutomation(IReadOnlyList<SpaceBase> targets, bool mark)
    {
        return MarkForAutomation(targets, mark);
    }

    void IScanAutomation.SelectPathForAutomation(string path)
    {
        SelectPathForAutomation(path);
    }

    void IScanAutomation.ApplyScanResult(string path, DirectorySpace result, TimeSpan elapsed)
    {
        ApplyScanResult(path, result, elapsed);
    }

    Task IScanAutomation.ScanFromAutomationAsync(string path, CancellationToken cancellationToken)
    {
        return ScanFromAutomationAsync(path, cancellationToken);
    }

    Task<ArchiveOutcome> IScanAutomation.ArchiveFromAutomationAsync(DirectorySpace dir, ArchiveRequest request, CancellationToken cancellationToken)
    {
        return ArchiveFromAutomationAsync(dir, request, cancellationToken);
    }
}
