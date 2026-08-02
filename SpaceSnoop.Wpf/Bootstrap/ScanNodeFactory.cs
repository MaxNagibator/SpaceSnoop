namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ScanNodeFactory(ScanPreferences preferences, AgentPreferences agent, IShellLauncher shell)
{
    public event Action<ScanNodeViewModel>? ArchiveRequested;
    public event Action<ScanNodeViewModel>? AskAgentRequested;
    public event Action? MarksChanged;

    public bool RevealFiles => preferences.RevealFiles;

    public bool ChatEnabled => agent.Enabled;

    public IShellLauncher Shell => shell;

    public ScanNodeViewModel Create(SpaceBase space, double siblingMax, double parentTotal, SpaceBase root, ScanSortState sort)
    {
        return new(space, siblingMax, parentTotal, root, null, sort, this);
    }

    public ScanNodeViewModel CreateRoot(SpaceBase space, ScanSortState sort)
    {
        return new(space, space.TotalSize, space.TotalSize, space, DriveCapacity.TryRead(space.AbsolutePath), sort, this);
    }

    public void RaiseMarksChanged()
    {
        MarksChanged?.Invoke();
    }

    public void RequestArchive(ScanNodeViewModel node)
    {
        ArchiveRequested?.Invoke(node);
    }

    public void RequestAskAgent(ScanNodeViewModel node)
    {
        AskAgentRequested?.Invoke(node);
    }
}
