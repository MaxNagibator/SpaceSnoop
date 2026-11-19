namespace SpaceSnoop.Core.Mft;

internal sealed class MftStatistics
{
    public const int MaxDetachedSamples = 5;

    public long RecordsScanned { get; set; }

    public long RecordsInUse { get; set; }

    public long Extensions { get; set; }

    public long Nameless { get; set; }

    public long NamelessBytes { get; set; }

    public long Detached { get; set; }

    public long Damaged { get; set; }

    public long Partial { get; set; }

    public long MissingParents { get; set; }

    public long StaleParents { get; set; }

    public long Unreachable { get; set; }

    public long Rehomed { get; set; }

    public long HardLinkedFiles { get; set; }

    public long OrphanFiles { get; set; }

    public long OrphanBytes { get; set; }

    public long SkippedLinks { get; set; }

    public List<(int Record, string Text)> DetachedSamples { get; } = [];

    public void RecordDetached(int record, int parent, string reason)
    {
        if (DetachedSamples.Count < MaxDetachedSamples)
        {
            DetachedSamples.Add((record, $"{record}->{parent}[{reason}]"));
        }
    }

    public void RemoveDetached(int record)
    {
        for (var i = DetachedSamples.Count - 1; i >= 0; i--)
        {
            if (DetachedSamples[i].Record == record)
            {
                DetachedSamples.RemoveAt(i);
            }
        }
    }

    public void Add(MftStatistics other)
    {
        RecordsScanned += other.RecordsScanned;
        RecordsInUse += other.RecordsInUse;
        Extensions += other.Extensions;
        Nameless += other.Nameless;
        NamelessBytes += other.NamelessBytes;
        Detached += other.Detached;
        Damaged += other.Damaged;
        Partial += other.Partial;
        MissingParents += other.MissingParents;
        StaleParents += other.StaleParents;
        Unreachable += other.Unreachable;
        Rehomed += other.Rehomed;
        HardLinkedFiles += other.HardLinkedFiles;
        OrphanFiles += other.OrphanFiles;
        OrphanBytes += other.OrphanBytes;
        SkippedLinks += other.SkippedLinks;

        foreach (var sample in other.DetachedSamples)
        {
            if (DetachedSamples.Count >= MaxDetachedSamples)
            {
                break;
            }

            DetachedSamples.Add(sample);
        }
    }
}

internal sealed record MftTable(MftEntry[] Entries, MftStatistics Statistics, Dictionary<int, List<MftName>> Alternates);
