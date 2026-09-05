namespace SpaceSnoop.Wpf.ViewModels;

public sealed class AppNavigator : IAppNavigator
{
    private IAppNavigator? _shell;

    public string? CurrentSectionKey => _shell?.CurrentSectionKey;

    public void Attach(IAppNavigator shell)
    {
        _shell = shell;
    }

    public bool TryNavigate(string sectionKey)
    {
        return _shell?.TryNavigate(sectionKey) ?? false;
    }

    public void OpenSync(SyncProfile profile, ComparisonResult? comparison)
    {
        _shell?.OpenSync(profile, comparison);
    }

    public void AskAgent(string question)
    {
        _shell?.AskAgent(question);
    }
}
