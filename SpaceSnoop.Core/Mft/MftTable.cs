namespace SpaceSnoop.Core.Mft;

internal sealed class MftStatistics
{
    public long RecordsScanned { get; set; }

    public long RecordsInUse { get; set; }

    public long Extensions { get; set; }

    public long Nameless { get; set; }

    public long Detached { get; set; }

    public long Damaged { get; set; }

    public long StaleParents { get; set; }

    public long Rehomed { get; set; }

    public long HardLinkedFiles { get; set; }

    public long OrphanFiles { get; set; }

    public long OrphanBytes { get; set; }

    public long SkippedLinks { get; set; }
}

internal sealed record MftTable(MftEntry[] Entries, MftStatistics Statistics, Dictionary<int, List<MftName>> Alternates);
