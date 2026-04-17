namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ScanNodeFactory(ILogger<ScanNodeViewModel> logger)
{
    public ScanNodeViewModel Create(SpaceBase space, double siblingMax, double parentTotal, ScanSortState sort)
    {
        return new(space, siblingMax, parentTotal, sort, logger, this);
    }
}
