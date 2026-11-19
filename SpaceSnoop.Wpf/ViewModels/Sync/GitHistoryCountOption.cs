namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed record GitHistoryCountOption(int Count, string Text, string? Tooltip = null)
{
    public static IReadOnlyList<GitHistoryCountOption> All { get; } =
    [
        new(4, "4", "Показывать 4 последних коммита"),
        new(8, "8", "Показывать 8 последних коммитов"),
        new(16, "16", "Показывать 16 последних коммитов"),
        new(32, "32", "Показывать 32 последних коммита"),
    ];

    public static GitHistoryCountOption FromCount(int count)
    {
        return All.FirstOrDefault(o => o.Count == count) ?? All[0];
    }
}
