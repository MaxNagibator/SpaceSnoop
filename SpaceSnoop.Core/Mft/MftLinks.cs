namespace SpaceSnoop.Core.Mft;

internal sealed class MftLinks(int[] firstChild, int[] nextSibling)
{
    private const int MaxDepth = 512;

    public int[] FirstChild { get; } = firstChild;

    public int[] NextSibling { get; } = nextSibling;

    public static MftLinks Build(MftTable table, CancellationToken cancel)
    {
        var entries = table.Entries;
        var firstChild = new int[entries.Length];
        var nextSibling = new int[entries.Length];

        Array.Fill(firstChild, -1);
        Array.Fill(nextSibling, -1);

        var links = new MftLinks(firstChild, nextSibling);

        for (var index = entries.Length - 1; index >= MftLayout.FirstUserRecord; index--)
        {
            if ((index & 0xFFFF) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            ref var entry = ref entries[index];

            if (!entry.Exists)
            {
                continue;
            }

            if (entry.IsLink)
            {
                table.Statistics.SkippedLinks++;
                entry.Name = null;
                continue;
            }

            if (!IsValidParent(entries, entry.Parent, entry.ParentSequence, table.Statistics))
            {
                table.Statistics.Detached++;

                if (!entry.IsDirectory)
                {
                    table.Statistics.OrphanFiles++;
                    table.Statistics.OrphanBytes += entry.Size;
                }

                entry.Name = null;
                continue;
            }

            links.Attach(index, entry.Parent);
        }

        return links;
    }

    public void Rehome(MftTable table, int start, CancellationToken cancel)
    {
        var entries = table.Entries;

        foreach (var (index, names) in table.Alternates)
        {
            cancel.ThrowIfCancellationRequested();

            if (index < 0 || index >= entries.Length || !entries[index].Exists || IsUnder(entries, index, start))
            {
                continue;
            }

            foreach (var candidate in names)
            {
                if (!IsValidParent(entries, candidate.Parent, candidate.ParentSequence, null)
                    || !IsUnder(entries, candidate.Parent, start))
                {
                    continue;
                }

                ref var entry = ref entries[index];
                Detach(index, entry.Parent);
                entry.Parent = candidate.Parent;
                entry.ParentSequence = candidate.ParentSequence;
                entry.Name = candidate.Name;
                Attach(index, candidate.Parent);
                table.Statistics.Rehomed++;
                break;
            }
        }
    }

    public void DropUnreachable(MftTable table, CancellationToken cancel)
    {
        var entries = table.Entries;
        var reachable = new bool[entries.Length];
        var stack = new Stack<int>();
        stack.Push(MftLayout.RootRecord);

        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();

            for (var child = FirstChild[stack.Pop()]; child >= 0; child = NextSibling[child])
            {
                reachable[child] = true;
                stack.Push(child);
            }
        }

        for (var index = MftLayout.FirstUserRecord; index < entries.Length; index++)
        {
            if ((index & 0xFFFF) == 0)
            {
                cancel.ThrowIfCancellationRequested();
            }

            ref var entry = ref entries[index];

            if (!entry.Exists || reachable[index])
            {
                continue;
            }

            table.Statistics.Detached++;

            if (!entry.IsDirectory)
            {
                table.Statistics.OrphanFiles++;
                table.Statistics.OrphanBytes += entry.Size;
            }

            entry.Name = null;
        }
    }

    private static bool IsValidParent(MftEntry[] entries, int parent, ushort parentSequence, MftStatistics? statistics)
    {
        if (parent < 0 || parent >= entries.Length || !entries[parent].Exists || !entries[parent].IsDirectory)
        {
            return false;
        }

        var sequence = entries[parent].Sequence;

        if (parentSequence == 0 || sequence == 0 || parentSequence == sequence)
        {
            return true;
        }

        if (statistics is not null)
        {
            statistics.StaleParents++;
        }

        return false;
    }

    private static bool IsUnder(MftEntry[] entries, int node, int start)
    {
        for (var depth = 0; depth < MaxDepth; depth++)
        {
            if (node == start)
            {
                return true;
            }

            if (node < 0 || node >= entries.Length)
            {
                return false;
            }

            var parent = entries[node].Parent;

            if (parent == node)
            {
                return false;
            }

            node = parent;
        }

        return false;
    }

    private void Attach(int index, int parent)
    {
        NextSibling[index] = FirstChild[parent];
        FirstChild[parent] = index;
    }

    private void Detach(int index, int parent)
    {
        if (parent < 0 || parent >= FirstChild.Length)
        {
            return;
        }

        if (FirstChild[parent] == index)
        {
            FirstChild[parent] = NextSibling[index];
            NextSibling[index] = -1;
            return;
        }

        for (var child = FirstChild[parent]; child >= 0; child = NextSibling[child])
        {
            if (NextSibling[child] != index)
            {
                continue;
            }

            NextSibling[child] = NextSibling[index];
            NextSibling[index] = -1;
            return;
        }
    }
}
