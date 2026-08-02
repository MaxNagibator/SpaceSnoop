namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal interface ISyncRowHost
{
    bool BlankAbsent { get; }

    bool ChatEnabled { get; }

    void ToggleExpand(DirectoryComparison dir);

    void ToggleGroup(string? key);

    void ExpandSubtree(DirectoryComparison dir);

    void CollapseSubtree(DirectoryComparison dir);

    void ApplyToSubtree(DirectoryComparison dir, SyncAction action);

    void NotifyActionsChanged();

    Task CompareContentAsync(FileComparison file);

    void AskAgentAbout(SyncNodeViewModel node);
}
