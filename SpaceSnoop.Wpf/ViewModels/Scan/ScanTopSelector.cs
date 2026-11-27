namespace SpaceSnoop.Wpf.ViewModels.Scan;

internal static class ScanTopSelector
{
    internal static ScanNodeViewModel[] SelectTopWeightedNodes(
        IReadOnlyList<ScanNodeViewModel> nodes,
        int limit,
        out int positiveCount)
    {
        positiveCount = 0;

        if (limit <= 0 || nodes.Count == 0)
        {
            return [];
        }

        var queue = new PriorityQueue<ScanNodeViewModel, (double Weight, int Index)>(
            Math.Min(nodes.Count, limit),
            MinTopWeightComparer.Instance);

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            if (node.Space is null)
            {
                continue;
            }

            var weight = node.Weight;

            if (weight <= 0)
            {
                continue;
            }

            positiveCount++;

            if (queue.Count < limit)
            {
                queue.Enqueue(node, (weight, i));
            }
            else if (queue.TryPeek(out _, out var minKey) && weight > minKey.Weight)
            {
                queue.DequeueEnqueue(node, (weight, i));
            }
        }

        var result = new ScanNodeViewModel[queue.Count];

        for (var i = result.Length - 1; i >= 0; i--)
        {
            result[i] = queue.Dequeue();
        }

        return result;
    }

    internal static ScanNodeViewModel[] SelectTopChildNodes(
        ScanNodeViewModel owner,
        ScanSortState? sort,
        ScanNodeFactory? factory,
        int limit,
        out int positiveCount)
    {
        if (owner.IsLoaded)
        {
            return SelectTopWeightedNodes(owner.Children, limit, out positiveCount);
        }

        positiveCount = 0;

        if (limit <= 0 || owner.Space is not DirectorySpace dir || sort is null || factory is null)
        {
            return [];
        }

        var top = SelectTopWeightedChildren(dir, owner, limit, out positiveCount);

        if (top.Length == 0)
        {
            return [];
        }

        var localMax = (double)top[0].Size;
        var parentWeight = owner.Weight;
        var root = owner.Root!;
        var result = new ScanNodeViewModel[top.Length];

        for (var i = 0; i < top.Length; i++)
        {
            var item = top[i];
            var space = item.Space ?? dir.GetFile(item.FileIndex);
            result[i] = factory.Create(space, localMax, parentWeight, root, sort, owner);
        }

        return result;
    }

    internal static (SpaceBase? Space, int FileIndex, long Size)[] SelectTopWeightedChildren(
        DirectorySpace dir,
        ScanNodeViewModel owner,
        int limit,
        out int positiveCount)
    {
        positiveCount = 0;

        if (limit <= 0)
        {
            return [];
        }

        var subs = dir.SubDirectoriesArray;
        var files = dir.FileEntries;
        var totalRaw = subs.Length + files.Length;

        if (totalRaw == 0)
        {
            return [];
        }

        var queue = new PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)>(
            Math.Min(totalRaw, limit),
            MinTopWeightComparer.Instance);
        var index = 0;

        for (var i = 0; i < subs.Length; i++)
        {
            ConsiderChild(subs[i], owner, limit, queue, ref positiveCount, ref index);
        }

        if (files.Length > 0)
        {
            if (owner.HasIgnoredItems)
            {
                if (owner.IsVisible(dir))
                {
                    if (owner.HasIgnoredFileIn(dir))
                    {
                        for (var i = 0; i < files.Length; i++)
                        {
                            ConsiderChild(dir.GetFile(i), owner, limit, queue, ref positiveCount, ref index);
                        }
                    }
                    else
                    {
                        for (var i = 0; i < files.Length; i++)
                        {
                            ConsiderFile(i, files[i].Size, limit, queue, ref positiveCount, ref index);
                        }
                    }
                }
            }
            else
            {
                for (var i = 0; i < files.Length; i++)
                {
                    ConsiderFile(i, files[i].Size, limit, queue, ref positiveCount, ref index);
                }
            }
        }

        var result = new (SpaceBase? Space, int FileIndex, long Size)[queue.Count];

        for (var i = result.Length - 1; i >= 0; i--)
        {
            result[i] = queue.Dequeue();
        }

        return result;
    }

    private static void ConsiderChild(
        SpaceBase candidate,
        ScanNodeViewModel owner,
        int limit,
        PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)> queue,
        ref int positiveCount,
        ref int index)
    {
        var currentIndex = index++;

        if (!owner.IsVisible(candidate))
        {
            return;
        }

        var size = owner.VisibleSize(candidate);

        if (size <= 0)
        {
            return;
        }

        positiveCount++;

        if (queue.Count < limit)
        {
            queue.Enqueue((candidate, -1, size), (size, currentIndex));
        }
        else if (queue.TryPeek(out _, out var minKey) && size > minKey.Weight)
        {
            queue.DequeueEnqueue((candidate, -1, size), (size, currentIndex));
        }
    }

    private static void ConsiderFile(
        int fileIndex,
        long size,
        int limit,
        PriorityQueue<(SpaceBase? Space, int FileIndex, long Size), (double Weight, int Index)> queue,
        ref int positiveCount,
        ref int index)
    {
        var currentIndex = index++;

        if (size <= 0)
        {
            return;
        }

        positiveCount++;

        if (queue.Count < limit)
        {
            queue.Enqueue((null, fileIndex, size), (size, currentIndex));
        }
        else if (queue.TryPeek(out _, out var minKey) && size > minKey.Weight)
        {
            queue.DequeueEnqueue((null, fileIndex, size), (size, currentIndex));
        }
    }

    internal sealed class MinTopWeightComparer : IComparer<(double Weight, int Index)>
    {
        internal static readonly MinTopWeightComparer Instance = new();

        public int Compare((double Weight, int Index) x, (double Weight, int Index) y)
        {
            var weightComparison = x.Weight.CompareTo(y.Weight);
            return weightComparison != 0 ? weightComparison : y.Index.CompareTo(x.Index);
        }
    }
}
