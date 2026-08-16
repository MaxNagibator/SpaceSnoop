namespace SpaceSnoop.Core.Mft;

internal sealed record MftLinks(int[] FirstChild, int[] NextSibling)
{
    public static MftLinks Build(MftTable table, CancellationToken cancel)
    {
        var entries = table.Entries;
        var firstChild = new int[entries.Length];
        var nextSibling = new int[entries.Length];

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
                entry.Name = null;
                continue;
            }

            var parent = entry.Parent;

            if (parent < 0 || parent >= entries.Length || !entries[parent].Exists || !entries[parent].IsDirectory)
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

            nextSibling[index] = firstChild[parent];
            firstChild[parent] = index;
        }

        return new(firstChild, nextSibling);
    }
}
