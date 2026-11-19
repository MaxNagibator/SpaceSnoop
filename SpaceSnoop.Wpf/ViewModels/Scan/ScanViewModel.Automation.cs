using SpaceSnoop.Core.Export;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel : IScanAutomation
{
    internal TimeSpan LastScanElapsed { get; private set; }

    internal int LastScanParallelism { get; private set; } = 1;

    internal ScanNotes LastScanNotes { get; private set; }
    bool IScanAutomation.IsScanning => IsBusy;

    string IScanAutomation.ResultPath => Summary.ResultPath;

    string IScanAutomation.ResultSizeText => Summary.ResultSizeText;

    string IScanAutomation.ResultFileCountText => Summary.ResultFileCountText;

    string IScanAutomation.ResultDirCountText => Summary.ResultDirCountText;

    string IScanAutomation.ResultRateText => Summary.ResultRateText;

    TimeSpan IScanAutomation.LastScanElapsed => LastScanElapsed;

    internal Func<ScanExportModel>? CaptureExportBuilder(int depth, int entryLimit)
    {
        if (CurrentRoot is not { } root)
        {
            return null;
        }

        var notes = LastScanNotes;
        var engine = notes.Parallelism > 0 ? notes.Engine : "directories";
        var options = new ScanExportOptions(depth, LastScanParallelism > 1, LastScanParallelism, engine);
        var exportNotes = ScanExportNotes.From(
            notes.ExtraNameBytes,
            notes.DroppedObjects,
            notes.DroppedBytes,
            notes.UnknownSizeFiles,
            notes.PartialRecords);

        var path = root.AbsolutePath;

        return () => ScanExport.Build(root, path, options, AppInfo.Version, entryLimit) with
        {
            Notes = exportNotes,
        };
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

    internal void ApplyScanResult(DirectorySpace result, TimeSpan elapsed, PerformanceTraversal? traversal, ScanNotes notes)
    {
        _scanVersion++;
        var previous = ScanTreeEditor.RemoveRoot(Roots, result.AbsolutePath);
        var (transferred, lost) = ScanTreeEditor.TransferMarks(previous, result, Marks.HasMarked);

        var node = _nodeFactory.CreateRoot(result, Sort.State);
        node.IsExpanded = true;

        Roots.Insert(0, node);
        Treemap.SetRoot(node);

        LastScanElapsed = elapsed;
        LastScanParallelism = Math.Max(1, notes.Parallelism);
        LastScanNotes = notes;
        _performance.ReportRun(Summary.Apply(result, elapsed, traversal, node.Drive, notes));
        HasResult = true;
        Duplicates.Clear();

        Marks.RecountMarkedAfterScan(node, transferred);

        Drives.ReloadLabels(SelectedDrive);

        _logger.ScanCompleted(
            result.AbsolutePath,
            result.TotalSizeText,
            result.TotalFileCount,
            result.TotalDirectoryCount,
            (long)elapsed.TotalMilliseconds);

        if (traversal is { FailedDirectories: > 0 } unread)
        {
            _logger.ScanDirectoriesUnread(result.AbsolutePath, unread.FailedDirectories, AdminElevation.IsElevated);
        }

        if (notes.HasDrops)
        {
            _notifier.Notify(
                "Скан неполон: "
                + ScanDropNote.Explain(notes.DroppedObjects, notes.DroppedBytes, notes.UnknownSizeFiles, notes.PartialRecords),
                StatusSeverity.Warning);
        }

        if (lost > 0)
        {
            _notifier.Notify($"Не перенесено пометок на удаление: {lost} – этих путей нет в новом скане", StatusSeverity.Warning);
        }
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

    long IScanAutomation.MarkedBytes()
    {
        return MarkedBytes();
    }

    Func<ScanExportModel>? IScanAutomation.CaptureExportBuilder(int depth, int entryLimit)
    {
        return CaptureExportBuilder(depth, entryLimit);
    }

    DirectorySpace? IScanAutomation.CaptureScanRoot()
    {
        return CurrentRoot;
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

    void IScanAutomation.ReleaseBeforeScan(string path)
    {
        ReleaseBeforeScan(new(path));
    }

    void IScanAutomation.ApplyScanResult(DirectorySpace result, TimeSpan elapsed, PerformanceTraversal? traversal, ScanNotes notes)
    {
        ApplyScanResult(result, elapsed, traversal, notes);
    }

    Task IScanAutomation.ReturnScanMemoryAsync(DirectorySpace result, Action<string> reportPhase, CancellationToken cancellationToken)
    {
        return ReturnScanMemoryAsync(result, reportPhase, cancellationToken);
    }

    Task IScanAutomation.ScanFromAutomationAsync(string path, CancellationToken cancellationToken)
    {
        return ScanFromAutomationAsync(path, cancellationToken);
    }

    Task<ArchiveOutcome> IScanAutomation.ArchiveFromAutomationAsync(DirectorySpace dir, ArchiveRequest request, CancellationToken cancellationToken)
    {
        return _archive.ArchiveFromAutomationAsync(dir, request, cancellationToken);
    }
}
