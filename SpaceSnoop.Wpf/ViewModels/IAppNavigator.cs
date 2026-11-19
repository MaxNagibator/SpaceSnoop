namespace SpaceSnoop.Wpf.ViewModels;

public interface IAppNavigator
{
    string? CurrentSectionKey { get; }

    bool TryNavigate(string sectionKey);

    void OpenSync(SyncProfile profile, ComparisonResult? comparison);

    void AskAgent(string question);
}
