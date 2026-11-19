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

            if (!IsValidParent(entries, index, entry.Parent, entry.ParentSequence, table.Statistics))
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

            if (index < 0 || index >= entries.Length || !entries[index].Present || entries[index].IsLink)
            {
                continue;
            }

            var dropped = !entries[index].Exists;

            if (!dropped && IsUnder(entries, index, start))
            {
                continue;
            }

            foreach (var candidate in names)
            {
                if (!IsValidParent(entries, index, candidate.Parent, candidate.ParentSequence, null)
                    || !IsUnder(entries, candidate.Parent, start)
                    || (entries[index].IsDirectory && !CanAttachDirectory(entries, index, candidate.Parent)))
                {
                    continue;
                }

                ref var entry = ref entries[index];

                if (dropped)
                {
                    Restore(table.Statistics, index, in entry);
                }

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

        for (var index = 0; index < Math.Min(entries.Length, MftLayout.FirstUserRecord); index++)
        {
            if (index != MftLayout.RootRecord
                && entries[index].Exists
                && entries[index].IsDirectory
                && !entries[index].IsLink)
            {
                stack.Push(index);
            }
        }

        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();

            for (var child = FirstChild[stack.Pop()]; child >= 0; child = NextSibling[child])
            {
                if (reachable[child])
                {
                    continue;
                }

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
            table.Statistics.Unreachable++;
            table.Statistics.RecordDetached(index, entry.Parent, "ветка");

            if (!entry.IsDirectory)
            {
                table.Statistics.OrphanFiles++;
                table.Statistics.OrphanBytes += entry.Size;
            }

            entry.Name = null;
        }
    }

    private static bool CanAttachDirectory(MftEntry[] entries, int index, int parent)
    {
        var visited = new HashSet<int>();

        while (parent >= 0 && parent < entries.Length)
        {
            if (parent == index || !visited.Add(parent))
            {
                return false;
            }

            var next = entries[parent].Parent;

            if (next == parent)
            {
                return true;
            }

            parent = next;
        }

        return false;
    }

    private static void Restore(MftStatistics statistics, int record, in MftEntry entry)
    {
        statistics.Detached--;
        statistics.RemoveDetached(record);

        if (entry.IsDirectory)
        {
            return;
        }

        statistics.OrphanFiles--;
        statistics.OrphanBytes -= entry.Size;
    }

    private static bool IsValidParent(MftEntry[] entries, int record, int parent, ushort parentSequence, MftStatistics? statistics)
    {
        if (parent < 0 || parent >= entries.Length || !entries[parent].Present)
        {
            if (statistics is not null)
            {
                statistics.MissingParents++;
                statistics.RecordDetached(record, parent, "свободна");
            }

            return false;
        }

        if (!MftLayout.SameGeneration(entries[parent].Sequence, parentSequence))
        {
            if (statistics is not null)
            {
                statistics.StaleParents++;
                statistics.RecordDetached(record, parent, $"поколение {parentSequence}!={entries[parent].Sequence}");
            }

            return false;
        }

        if (entries[parent].Exists && entries[parent].IsDirectory)
        {
            return true;
        }

        if (statistics is not null)
        {
            statistics.Unreachable++;
            statistics.RecordDetached(record, parent, entries[parent].IsDirectory ? "ветка" : "не каталог");
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
