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

    internal ScanArchiveViewModel(
        ArchiveProgressDialogFactory archiveDialogFactory,
        IDialogService dialogs,
        ScanNodeFactory nodeFactory,
        ObservableCollection<ScanNodeViewModel> roots,
        ScanTreemapViewModel treemap,
        ScanInspectorViewModel inspector,
        Func<ScanNodeViewModel?> selectedNode,
        ScanMarksViewModel marks)
    {
        _archiveDialogFactory = archiveDialogFactory;
        _dialogs = dialogs;
        _roots = roots;
        _treemap = treemap;
        _inspector = inspector;
        _selectedNode = selectedNode;
        _marks = marks;

        nodeFactory.ArchiveRequested += OnArchiveRequested;
    }

    internal ArchiveRequest CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal)
    {
        return _archiveDialogFactory.CreateRequest(dir, deleteOriginal, interactive: false);
    }

    internal async Task<ArchiveOutcome> ArchiveFromAutomationAsync(
        DirectorySpace dir,
        ArchiveRequest request,
        CancellationToken cancellationToken)
    {
        var dialog = _archiveDialogFactory.Create(request);

        using (cancellationToken.Register(dialog.RequestStop))
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }

        ApplyArchiveResult(dir, dialog);

        return new(dialog.CreatedArchivePath, dialog.OriginalDeleted, dialog.StatusText);
    }

    private async void OnArchiveRequested(ScanNodeViewModel node)
    {
        if (node.Space is not DirectorySpace dir)
        {
            return;
        }

        var dialog = _archiveDialogFactory.Create(dir);

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
        }
    }
}
