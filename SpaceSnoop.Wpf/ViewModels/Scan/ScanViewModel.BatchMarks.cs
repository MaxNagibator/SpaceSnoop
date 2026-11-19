namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel
{
    [RelayCommand]
    public void MarkSelectedDeleted()
    {
        ApplyBatchMarkMutation(markDeleted: true);
    }

    [RelayCommand]
    public void UnmarkSelectedDeleted()
    {
        ApplyBatchMarkMutation(markDeleted: false);
    }

    [RelayCommand]
    public void ToggleSelectedDeleted()
    {
        var topNodes = ActiveTopLevelNodes();
        if (topNodes.Count == 0)
        {
            return;
        }

        ApplyBatchMarkMutation(topNodes.Any(static node => node.Space is { State: SpaceState.Added }), topNodes);
    }

    private void ApplyBatchMarkMutation(bool markDeleted, List<ScanNodeViewModel>? prefiltered = null)
    {
        var topNodes = prefiltered ?? ActiveTopLevelNodes();
        var targets = markDeleted
            ? topNodes.Where(static n => n.Space is { State: SpaceState.Added }).ToList()
            : topNodes.Where(static n => n.Space is not null && (n.Space.IsDeleted || ScanTreeEditor.HasMarkedSelfOrChild(n.Space))).ToList();

        if (targets.Count == 0)
        {
            return;
        }

        var topScopes = ScanTreeEditor.FilterTopLevelScopes(
            targets.Select(static n => (Root: n.Root ?? n.Space!, Scope: ScanTreeEditor.FindMarkScope(n.Root ?? n.Space!, n.Space!))));

        var beforeSnapshot = new ScanMarkSnapshot(topScopes.Select(item => Marks.CaptureScopeMarks(item.Root, item.Scope)).ToList());

        var previousByTarget = targets
            .Select(node => (
                Root: new WeakReference<SpaceBase>(node.Root ?? node.Space!),
                Target: new WeakReference<SpaceBase>(node.Space!),
                Marked: (_nodeFactory.MarksPresent || node.Space!.IsDeleted
                    ? ScanTreeEditor.CollectMarked(node.Space!)
                    : []).Select(static m => new WeakReference<SpaceBase>(m))
                .ToList()))
            .ToList();

        foreach (var space in targets.Select(static node => node.Space!))
        {
            if (markDeleted)
            {
                space.Delete();
            }
            else
            {
                ScanNodeViewModel.RestoreRecursive(space);
            }
        }

        RefreshScopesAndNotifyMarks(topScopes, beforeSnapshot);

        var count = targets.Count;
        var bytes = ScanInspectorViewModel.CalculateCombinedBytes(targets);
        var verb = markDeleted ? "Помечено на удаление" : "Снята пометка на удаление";
        _notifier.Notify(
            $"{verb}: {count:N0} {Plural.Word(count, "объект", "объекта", "объектов")} ({SizeFormatter.Format(bytes)})",
            StatusSeverity.Info,
            () => UndoBatchMark(markDeleted, previousByTarget));
    }

    private void UndoBatchMark(
        bool markDeleted,
        List<(WeakReference<SpaceBase> Root, WeakReference<SpaceBase> Target, List<WeakReference<SpaceBase>> Marked)> previousByTarget)
    {
        var activeRoots = ActiveRootSpaces();
        var activeTargets = new List<(SpaceBase Root, SpaceBase Target, List<SpaceBase> Marked)>();

        foreach (var (rootRef, targetRef, markedRefs) in previousByTarget)
        {
            if (!rootRef.TryGetTarget(out var root)
                || !targetRef.TryGetTarget(out var target)
                || !activeRoots.Contains(root)
                || !ScanNodeViewModel.IsAttachedToRoot(root, target))
            {
                continue;
            }

            var validMarked = markedRefs
                .Select(static r => r.TryGetTarget(out var item) ? item : null)
                .OfType<SpaceBase>()
                .Where(item => ScanNodeViewModel.IsAttachedToRoot(root, item))
                .ToList();

            activeTargets.Add((root, target, validMarked));
        }

        if (activeTargets.Count == 0)
        {
            return;
        }

        var undoRanges = ScanTreeEditor.FilterTopLevelScopes(activeTargets.Select(static item => (item.Root, Scope: ScanTreeEditor.FindMarkScope(item.Root, item.Target))));
        var beforeUndo = new ScanMarkSnapshot(undoRanges.Select(item => Marks.CaptureScopeMarks(item.Root, item.Scope)).ToList());

        if (markDeleted)
        {
            foreach (var (_, target, marked) in activeTargets.Where(static t => !ScanNodeViewModel.HasMarkedAncestor(t.Target)))
            {
                ScanNodeViewModel.RestoreRecursive(target);
                marked.ForEach(static item => item.Delete());
            }
        }
        else
        {
            activeTargets.SelectMany(static t => t.Marked).ToList().ForEach(static item => item.Delete());
        }

        RefreshScopesAndNotifyMarks(undoRanges, beforeUndo);
    }

    private void RefreshScopesAndNotifyMarks(List<(SpaceBase Root, SpaceBase Scope)> scopes, ScanMarkSnapshot before)
    {
        foreach (var (_, scope) in scopes)
        {
            foreach (var root in Roots)
            {
                ScanTreeEditor.RefreshNodeMarks(root, scope);
            }
        }

        _nodeFactory.RaiseMarksChanged(before);
        RefreshSelectedNodesState();
    }
}
