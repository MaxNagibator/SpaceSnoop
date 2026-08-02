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
        return _archive.CreateArchiveRequest(dir, deleteOriginal);
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
        return _archive.ArchiveFromAutomationAsync(dir, request, cancellationToken);
    }

    internal Func<ScanExportModel>? CaptureExportBuilder(int depth, int entryLimit)
    {
        if (CurrentRoot is not { } root)
        {
            return null;
        }

        var options = new ScanExportOptions(depth, Preferences.UseMultithreading, Preferences.MaxParallelism);
        var path = root.AbsolutePath;

        return () => ScanExport.Build(root, path, options, AppInfo.Version, entryLimit);
    }

    internal void SelectPathForAutomation(string path)
    {
        if (!Drives.HasDrive(path))
        {
            Drives.AddDrive(path);
            Drives.LoadDriveLabels();
        }

        SelectedDrive = path;
    }

    internal Task ScanFromAutomationAsync(string path, CancellationToken cancellationToken)
    {
        SelectPathForAutomation(path);

        return ScanAsync(path, cancellationToken);
    }

    internal void ApplyScanResult(string path, DirectorySpace result, TimeSpan elapsed)
    {
        ScanTreeEditor.RemoveRoot(Roots, path);

        var node = _nodeFactory.CreateRoot(result, _sortState);
        node.IsExpanded = true;

        Roots.Insert(0, node);
        Treemap.SetRoot(node);

        LastScanElapsed = elapsed;
        Summary.Apply(result, elapsed);
        HasResult = true;
        Marks.RecountMarked();

        _logger.ScanCompleted(result.AbsolutePath,
            result.TotalSizeText,
            result.TotalFileCount,
            result.TotalDirectoryCount,
            (long)elapsed.TotalMilliseconds);
    }

    internal SpaceBase? FindForAutomation(string path)
    {
        return ScanLookup.Find(Roots.Select(static root => root.Space).OfType<SpaceBase>(), path);
    }

    internal bool IsScanRoot(SpaceBase space)
    {
        return Roots.Any(root => ReferenceEquals(root.Space, space));
    }

    internal int MarkForAutomation(IReadOnlyList<SpaceBase> targets, bool mark)
    {
        var changed = 0;

        foreach (var space in targets)
        {
            if (mark)
            {
                if (space.IsDeleted)
                {
                    continue;
                }

                space.Delete();
            }
            else
            {
                if (!ScanTreeEditor.HasMarkedSelfOrChild(space))
                {
                    continue;
                }

                ScanNodeViewModel.RestoreRecursive(space);
            }

            changed++;
        }

        if (changed > 0)
        {
            foreach (var root in Roots)
            {
                root.RefreshMarks();
            }

            Marks.RecountMarked();
        }

        return changed;
    }

    internal long MarkedBytes()
    {
        return ScanTreeEditor.CollectMarked(Roots).Sum(static item => item.TotalSize);
    }
}
