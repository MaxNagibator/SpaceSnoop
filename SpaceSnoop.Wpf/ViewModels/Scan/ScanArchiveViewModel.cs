using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal sealed class ScanArchiveViewModel
{
    private readonly ArchiveProgressDialogFactory _archiveDialogFactory;
    private readonly IDialogService _dialogs;
    private readonly ObservableCollection<ScanNodeViewModel> _roots;
    private readonly ScanTreemapViewModel _treemap;
    private readonly ScanInspectorViewModel _inspector;
    private readonly Func<ScanNodeViewModel?> _selectedNode;
    private readonly ScanMarksViewModel _marks;
    private readonly Action _reloadDrives;
    private bool _archiveRequested;

    internal ScanArchiveViewModel(
        ArchiveProgressDialogFactory archiveDialogFactory,
        IDialogService dialogs,
        ScanNodeFactory nodeFactory,
        ObservableCollection<ScanNodeViewModel> roots,
        ScanTreemapViewModel treemap,
        ScanInspectorViewModel inspector,
        Func<ScanNodeViewModel?> selectedNode,
        ScanMarksViewModel marks,
        Action reloadDrives)
    {
        _archiveDialogFactory = archiveDialogFactory;
        _dialogs = dialogs;
        _roots = roots;
        _treemap = treemap;
        _inspector = inspector;
        _selectedNode = selectedNode;
        _marks = marks;
        _reloadDrives = reloadDrives;

        nodeFactory.ArchiveRequested += OnArchiveRequested;
    }

    internal ArchiveRequest CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal)
    {
        return _archiveDialogFactory.CreateRequest(dir, deleteOriginal, false);
    }

    internal async Task<ArchiveOutcome> ArchiveFromAutomationAsync(
        DirectorySpace dir,
        ArchiveRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = _archiveDialogFactory.Create(request);
        var run = dialog.StartCommand.ExecuteAsync(null);

        using (cancellationToken.Register(dialog.RequestStop))
        {
            await run;
        }

        ApplyArchiveResult(dir, dialog);

        return new(dialog.CreatedArchivePath, dialog.OriginalDeleted, dialog.StatusText);
    }

    private async void OnArchiveRequested(ScanNodeViewModel node)
    {
        if (_archiveRequested || node.Space is not DirectorySpace dir)
        {
            return;
        }

        _archiveRequested = true;
        try
        {
            var dialog = await _archiveDialogFactory.CreateAsync(dir);
            var root = dir;
            while (root.Parent is DirectorySpace parent)
            {
                root = parent;
            }

            if (_roots.Any(item => ReferenceEquals(item.Space, root)))
            {
                try
                {
                    await _dialogs.ShowAsync(dialog);
                }
                finally
                {
                    await dialog.StopAsync();
                }

                ApplyArchiveResult(dir, dialog);
            }
        }
        finally
        {
            _archiveRequested = false;
        }
    }

    private void ApplyArchiveResult(DirectorySpace dir, ArchiveProgressDialogViewModel dialog)
    {
        if (dialog.CreatedArchivePath is { } archivePath && ScanTreeEditor.AddArchiveToTree(_roots, dir, archivePath))
        {
            if (_treemap.TreemapRoot is not null)
            {
                _treemap.RebuildTiles();
            }

            if (_selectedNode() is { } selected)
            {
                _inspector.Show(selected);
            }
        }

        if (dialog.OriginalDeleted)
        {
            _marks.ApplyDeletionResult([dir]);
            return;
        }

        _reloadDrives();
    }
}
