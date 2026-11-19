namespace SpaceSnoop.Wpf.ViewModels.Sync;

internal static class SyncRootsCheck
{
    internal static Func<string, string, string?> Refusal { get; set; } = SyncRoots.Refusal;
}
