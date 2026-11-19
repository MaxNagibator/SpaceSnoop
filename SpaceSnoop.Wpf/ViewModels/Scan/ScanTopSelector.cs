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
