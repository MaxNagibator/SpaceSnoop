using KeepShell.Services;
using System.Collections.ObjectModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanMarksViewModel : ObservableObject
{
    private readonly IDialogService _dialogs;
    private readonly DeleteProgressDialogFactory _deleteDialogFactory;
    private readonly OperationPreferences _operations;
    private readonly ILogger _logger;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly ObservableCollection<ScanNodeViewModel> _roots;
    private readonly ScanSummaryViewModel _summary;
    private readonly ScanTreemapViewModel _treemap;
    private readonly Func<ScanNodeViewModel?> _selectedNode;
    private readonly Action<ScanNodeViewModel?> _setSelectedNode;
    private readonly Action _clearHasResult;
    private readonly Func<bool> _isScanning;
    private readonly Action _reloadDrives;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarked))]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMarkedCommand))]
    private int _markedCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteHint))]
    private string _markedSizeText = string.Empty;

    internal ScanMarksViewModel(
        IDialogService dialogs,
        DeleteProgressDialogFactory deleteDialogFactory,
        OperationPreferences operations,
        ILogger logger,
        ScanNodeFactory nodeFactory,
        ObservableCollection<ScanNodeViewModel> roots,
        ScanSummaryViewModel summary,
        ScanTreemapViewModel treemap,
        Func<ScanNodeViewModel?> selectedNode,
        Action<ScanNodeViewModel?> setSelectedNode,
        Action clearHasResult,
        Func<bool> isScanning,
        Action reloadDrives)
    {
        _dialogs = dialogs;
        _deleteDialogFactory = deleteDialogFactory;
        _operations = operations;
        _logger = logger;
        _nodeFactory = nodeFactory;
        _roots = roots;
        _summary = summary;
        _treemap = treemap;
        _selectedNode = selectedNode;
        _setSelectedNode = setSelectedNode;
        _clearHasResult = clearHasResult;
        _isScanning = isScanning;
        _reloadDrives = reloadDrives;

        nodeFactory.MarksChanged += RecountMarked;
    }

    public bool HasMarked => MarkedCount > 0;

    public string DeleteHint => HasMarked
        ? $"Переместить в корзину: {MarkedCount} {Plural.Word(MarkedCount, "объект", "объекта", "объектов")}, ≈{MarkedSizeText}"
        : "Пометьте узлы дерева (Ctrl + правый клик), чтобы удалить их в корзину";

    internal void RecountMarked()
    {
        var marked = ScanTreeEditor.CollectMarked(_roots);

        _nodeFactory.MarksPresent = marked.Count > 0;
        MarkedCount = marked.Count;
        MarkedSizeText = marked.Count > 0 ? SizeFormatter.Format(marked.Sum(item => item.TotalSize)) : string.Empty;
    }

    internal void ApplyDeletionResult(IReadOnlyList<SpaceBase> deletedItems)
    {
        if (deletedItems.Count == 0)
        {
            return;
        }

        _logger.DeletionResultApplied(deletedItems.Count);

        var deletedSet = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        foreach (var item in deletedItems)
        {
            deletedSet.Add(item);
        }

        var selected = _selectedNode();

        if (selected?.Space is not null && deletedSet.Contains(selected.Space))
        {
            _setSelectedNode(null);
        }

        foreach (var item in deletedItems)
        {
            if (item.Parent is DirectorySpace parentDir)
            {
                parentDir.Remove(item);
            }
        }

        var rootsToRemove = new List<ScanNodeViewModel>();

        foreach (var rootVm in _roots)
        {
            if (rootVm.Space is not null && deletedSet.Contains(rootVm.Space))
            {
                rootsToRemove.Add(rootVm);
            }
            else
            {
                ScanTreeEditor.RefreshNodeAfterDeletion(rootVm, deletedSet);
            }
        }

        foreach (var rootVm in rootsToRemove)
        {
            _roots.Remove(rootVm);
        }

        var resultRoot = _roots.FirstOrDefault(r =>
            string.Equals(ScanTreeEditor.NormalizePath(r.AbsolutePath), ScanTreeEditor.NormalizePath(_summary.ResultPath), StringComparison.OrdinalIgnoreCase));

        if (resultRoot?.Space is DirectorySpace resultDir)
        {
            _summary.Refresh(resultDir);
        }
        else if (rootsToRemove.Any(r => string.Equals(ScanTreeEditor.NormalizePath(r.AbsolutePath), ScanTreeEditor.NormalizePath(_summary.ResultPath), StringComparison.OrdinalIgnoreCase)))
        {
            _clearHasResult();
        }

        _treemap.RefreshAfterDeletion(deletedSet);
        RecountMarked();
        _reloadDrives();
    }

    private bool CanDeleteMarked()
    {
        return !_isScanning() && MarkedCount > 0;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteMarked))]
    private async Task DeleteMarkedAsync()
    {
        var marked = ScanTreeEditor.CollectMarked(_roots);

        if (marked.Count == 0)
        {
            _logger.NothingMarkedForDeletion();
            _dialogs.Info("Удаление", "Нет элементов, помеченных на удаление. Пометьте их через контекстное меню узла.");
            return;
        }

        var permanent = _operations.DeleteMode == DeleteMode.Permanent;
        _logger.DeletionRequested(marked.Count, permanent);
        var dialog = _deleteDialogFactory.Create(marked, permanent);

        if (!_operations.ConfirmBeforeDelete)
        {
            dialog.StartCommand.Execute(null);
        }

        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            await dialog.StopAsync();
        }

        ApplyDeletionResult(dialog.DeletedItems);
    }
}
