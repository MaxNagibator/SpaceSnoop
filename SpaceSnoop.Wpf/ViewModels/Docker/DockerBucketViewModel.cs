namespace SpaceSnoop.Wpf.ViewModels.Docker;

public sealed class DockerBucketViewModel
{
    public DockerBucketViewModel(DockerUsage usage)
    {
        var sizeBytes = DockerSize.ToBytes(usage.Size);
        var reclaimBytes = DockerSize.ToBytes(SizePart(usage.Reclaimable));

        Title = DockerText.Category(usage.Type);
        Count = usage.TotalCount;
        SizeText = SizeFormatter.Format(sizeBytes);
        ReclaimText = Reclaim(reclaimBytes, sizeBytes);
    }

    public string Title { get; }

    public int Count { get; }

    public string SizeText { get; }

    public string ReclaimText { get; }

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
