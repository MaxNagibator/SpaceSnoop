namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed class DockerBucketViewModel
{
    public DockerBucketViewModel(DockerUsage usage)
    {
        SizeBytes = DockerSize.ToBytes(usage.Size);
        ReclaimBytes = DockerSize.ToBytes(SizePart(usage.Reclaimable));

        Title = DockerText.Category(usage.Type);
        Count = usage.TotalCount;
        SizeText = SizeFormatter.Format(SizeBytes);
        ReclaimText = Reclaim(ReclaimBytes, SizeBytes);
    }

    public string Title { get; }

    public int Count { get; }

    public string SizeText { get; }

    public string ReclaimText { get; }

    public long SizeBytes { get; }

    public long ReclaimBytes { get; }

    public bool HasReclaim => ReclaimBytes > 0;

    public double Fraction { get; private set; }

    public double ReclaimFraction { get; private set; }

    public static List<DockerBucketViewModel> Build(IEnumerable<DockerUsage> usages)
    {
        List<DockerBucketViewModel> items = [.. usages.Select(usage => new DockerBucketViewModel(usage))];
        var largest = items.Count == 0 ? 0 : items.Max(item => item.SizeBytes);

        foreach (var item in items)
        {
            item.Fraction = largest > 0 ? (double)item.SizeBytes / largest : 0;
            item.ReclaimFraction = largest > 0 ? Math.Clamp((double)item.ReclaimBytes / largest, 0, item.Fraction) : 0;
        }

        return items;
    }

    internal static string SizePart(string reclaimable)
    {
        var value = reclaimable.Trim();
        var open = value.IndexOf('(');

        return open < 0 ? value : value[..open].Trim();
    }

    private static string Reclaim(long reclaimBytes, long sizeBytes)
    {
        if (reclaimBytes <= 0)
        {
            return SizeFormatter.Format(0);
        }

        var share = sizeBytes > 0 ? (double)reclaimBytes / sizeBytes : 0;

        return $"{SizeFormatter.Format(reclaimBytes)} ({ShareFormatter.Format(share)})";
    }
}
