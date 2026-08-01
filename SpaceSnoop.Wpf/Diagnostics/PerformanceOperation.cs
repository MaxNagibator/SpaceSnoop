namespace SpaceSnoop.Wpf.Diagnostics;

public sealed record PerformanceOperation(
    string Name,
    long Items,
    long Bytes,
    TimeSpan Elapsed,
    long? TotalItems = null,
    long? TotalBytes = null,
    EtaBasis Basis = EtaBasis.None)
{
    public double? ItemsPerSecond => Rate(Items);

    public double? BytesPerSecond => Rate(Bytes);

    public TimeSpan? Remaining()
    {
        return Basis switch
        {
            EtaBasis.Items => Estimate(TotalItems, Items, ItemsPerSecond),
            EtaBasis.Bytes => Estimate(TotalBytes, Bytes, BytesPerSecond),
            _ => Estimate(TotalItems, Items, ItemsPerSecond) ?? Estimate(TotalBytes, Bytes, BytesPerSecond),
        };
    }

    private static TimeSpan? Estimate(long? total, long done, double? perSecond)
    {
        if (total is not > 0 || perSecond is not > 0 || done >= total)
        {
            return null;
        }

        var seconds = (total.Value - done) / perSecond.Value;

        return double.IsFinite(seconds) ? TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds)) : null;
    }

    private double? Rate(long amount)
    {
        var seconds = Elapsed.TotalSeconds;

        if (seconds < AppDefaults.PerformanceRateMinSeconds || amount <= 0)
        {
            return null;
        }

        return amount / seconds;
    }
}
