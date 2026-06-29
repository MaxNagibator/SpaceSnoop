namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ScanNodeFactory(ILogger<ScanNodeViewModel> logger, ScanPreferences preferences)
{
    public event Action<ScanNodeViewModel>? ArchiveRequested;
    public event Action? MarksChanged;

    public bool RevealFiles => preferences.RevealFiles;

    public ScanNodeViewModel Create(SpaceBase space, double siblingMax, double parentTotal, ScanSortState sort)
    {
        return new(space, siblingMax, parentTotal, sort, logger, this);
    }

    public void RaiseMarksChanged()
    {
        MarksChanged?.Invoke();
    }

    public void RequestArchive(ScanNodeViewModel node)
    {
        ArchiveRequested?.Invoke(node);
    }
}
