namespace SpaceSnoop.Core.Mft;

internal sealed class MftLinks(int[] firstChild, int[] nextSibling)
{
    private const int MaxDepth = 512;
    private const int ParallelThreshold = 65_536;
    private const int MaxWorkers = 8;

    public int[] FirstChild { get; } = firstChild;

    public int[] NextSibling { get; } = nextSibling;

    public static MftLinks Build(MftTable table, CancellationToken cancel)
    {
        var entries = table.Entries;
        var firstChild = GC.AllocateUninitializedArray<int>(entries.Length);
        var nextSibling = GC.AllocateUninitializedArray<int>(entries.Length);
        var links = new MftLinks(firstChild, nextSibling);

        if (entries.Length < ParallelThreshold)
        {
            Array.Fill(firstChild, -1);
            Array.Fill(nextSibling, -1);

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
                    entry.ClearName();
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

                    entry.ClearName();
                    continue;
                }

                links.Attach(index, entry.Parent);
            }

            return links;
        }

        var chunkCount = Math.Min(MaxWorkers, Math.Max(1, Environment.ProcessorCount));
        var options = new ParallelOptions
        {
            CancellationToken = cancel,
            MaxDegreeOfParallelism = chunkCount,
        };

        Parallel.For(
            0,
            chunkCount,
            options,
            x =>
            {
                var start = (int)((long)entries.Length * x / chunkCount);
                var end = (int)((long)entries.Length * (x + 1) / chunkCount);
                firstChild.AsSpan(start, end - start).Fill(-1);
                nextSibling.AsSpan(start, end - start).Fill(-1);
            });

        var chunkStats = new MftStatistics[chunkCount];
        var chunkClear = new List<int>[chunkCount];
        var total = entries.Length - MftLayout.FirstUserRecord;

        Parallel.For(
            0,
            chunkCount,
            options,
            x =>
            {
                var start = MftLayout.FirstUserRecord + (int)((long)total * x / chunkCount);
                var end = MftLayout.FirstUserRecord + (int)((long)total * (x + 1) / chunkCount);
                var localStats = new MftStatistics();
                var toClear = new List<int>();
                chunkStats[x] = localStats;
                chunkClear[x] = toClear;

                for (var index = end - 1; index >= start; index--)
                {
                    if ((index & 0xFFFF) == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                    }

                    ref readonly var entry = ref entries[index];

                    if (!entry.Exists)
                    {
                        continue;
                    }

                    if (entry.IsLink)
                    {
                        localStats.SkippedLinks++;
                        toClear.Add(index);
                        continue;
                    }

                    if (!IsValidParent(entries, index, entry.Parent, entry.ParentSequence, localStats))
                    {
                        localStats.Detached++;

                        if (!entry.IsDirectory)
                        {
                            localStats.OrphanFiles++;
                            localStats.OrphanBytes += entry.Size;
                        }

                        toClear.Add(index);
                        continue;
                    }

                    nextSibling[index] = Interlocked.Exchange(ref firstChild[entry.Parent], index);
                }
            });

        for (var chunk = chunkCount - 1; chunk >= 0; chunk--)
        {
            table.Statistics.Add(chunkStats[chunk]);
        }

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            var toClear = chunkClear[chunk];

            for (var i = 0; i < toClear.Count; i++)
            {
                entries[toClear[i]].ClearName();
            }
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
                entry.SetName(candidate.NameId);
                Attach(index, candidate.Parent);
                table.Statistics.Rehomed++;
                break;
            }
        }
    }

    public void DropUnreachable(MftTable table, CancellationToken cancel)
    {
        var entries = table.Entries;
        var reachable = new ulong[(entries.Length + 63) >> 6];
        var stack = new Stack<int>();

        if (entries.Length > MftLayout.RootRecord)
        {
            SetReachable(reachable, MftLayout.RootRecord);
            stack.Push(MftLayout.RootRecord);
        }

        SeedSystemDirectories(entries, reachable, stack);
        MarkReachable(stack, reachable, cancel);
        SweepUnreachable(table, reachable, cancel);
    }

    internal static bool IsReachable(ulong[] reachable, int index)
    {
        return (reachable[index >> 6] & (1UL << (index & 63))) != 0;
    }

    internal static void SetReachable(ulong[] reachable, int index)
    {
        reachable[index >> 6] |= 1UL << (index & 63);
    }

    internal static bool TryMarkReachable(ulong[] reachable, int index)
    {
        var word = index >> 6;
        var mask = 1UL << (index & 63);

        return (Volatile.Read(ref reachable[word]) & mask) == 0
            && (Interlocked.Or(ref reachable[word], mask) & mask) == 0;
    }

    internal void FinishUnreachable(MftTable table, ulong[] reachable, CancellationToken cancel)
    {
        var entries = table.Entries;
        var stack = new Stack<int>();

        SeedSystemDirectories(entries, reachable, stack);
        MarkReachable(stack, reachable, cancel);
        SweepUnreachable(table, reachable, cancel);
    }

    private static void SeedSystemDirectories(MftEntry[] entries, ulong[] reachable, Stack<int> stack)
    {
        var limit = Math.Min(entries.Length, MftLayout.FirstUserRecord);

        for (var index = 0; index < limit; index++)
        {
            if (index != MftLayout.RootRecord
                && entries[index].Exists
                && entries[index].IsDirectory
                && !entries[index].IsLink
                && !IsReachable(reachable, index))
            {
                SetReachable(reachable, index);
                stack.Push(index);
            }
        }
    }

    private void MarkReachable(Stack<int> stack, ulong[] reachable, CancellationToken cancel)
    {
        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();

            for (var child = FirstChild[stack.Pop()]; child >= 0; child = NextSibling[child])
            {
                if (IsReachable(reachable, child))
                {
                    continue;
                }

                SetReachable(reachable, child);

                if (FirstChild[child] >= 0)
                {
                    stack.Push(child);
                }
            }
        }
    }

    private static void SweepUnreachable(MftTable table, ulong[] reachable, CancellationToken cancel)
    {
        var entries = table.Entries;

        if (entries.Length < ParallelThreshold)
        {
            for (var index = MftLayout.FirstUserRecord; index < entries.Length; index++)
            {
                if ((index & 0xFFFF) == 0)
                {
                    cancel.ThrowIfCancellationRequested();
                }

                ref var entry = ref entries[index];

                if (!entry.Exists || IsReachable(reachable, index))
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

                entry.ClearName();
            }

            return;
        }

        var chunkCount = Math.Min(MaxWorkers, Math.Max(1, Environment.ProcessorCount));
        var chunkStats = new MftStatistics[chunkCount];
        var total = entries.Length - MftLayout.FirstUserRecord;
        var options = new ParallelOptions
        {
            CancellationToken = cancel,
            MaxDegreeOfParallelism = chunkCount,
        };

        Parallel.For(
            0,
            chunkCount,
            options,
            x =>
            {
                var start = MftLayout.FirstUserRecord + (int)((long)total * x / chunkCount);
                var end = MftLayout.FirstUserRecord + (int)((long)total * (x + 1) / chunkCount);
                var localStats = new MftStatistics();
                chunkStats[x] = localStats;

                for (var index = start; index < end; index++)
                {
                    if ((index & 0xFFFF) == 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                    }

                    ref var entry = ref entries[index];

                    if (!entry.Exists || IsReachable(reachable, index))
                    {
                        continue;
                    }

                    localStats.Detached++;
                    localStats.Unreachable++;
                    localStats.RecordDetached(index, entry.Parent, "ветка");

                    if (!entry.IsDirectory)
                    {
                        localStats.OrphanFiles++;
                        localStats.OrphanBytes += entry.Size;
                    }

                    entry.ClearName();
                }
            });

        for (var chunk = 0; chunk < chunkCount; chunk++)
        {
            table.Statistics.Add(chunkStats[chunk]);
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
