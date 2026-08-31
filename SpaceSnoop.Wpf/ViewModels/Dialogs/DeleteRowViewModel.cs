namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteRowViewModel(SpaceBase space) : ObservableObject
{
    private const int ErrorSummaryLimit = 100;

    [ObservableProperty]
    private DeleteRowState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    [NotifyPropertyChangedFor(nameof(ErrorSummary))]
    private string? _error;

    public SpaceBase Space { get; } = space;

    public string Path { get; } = space.AbsolutePath;

    public string Name { get; } = space.Name;

    public string ParentPath { get; } = System.IO.Path.GetDirectoryName(space.AbsolutePath) ?? string.Empty;

    public string SizeText { get; } = SizeFormatter.Format(space.TotalSize);

    public bool IsDirectory { get; } = space is DirectorySpace;

    public string? ErrorSummary => Shorten(Error);

    public string ToolTipText => Error?.Trim() is { Length: > 0 } reason
        ? $"{Path}{Environment.NewLine}{reason}"
        : Path;

    private static string? Shorten(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return null;
        }

        var line = error.AsSpan();
        var breakAt = line.IndexOfAny('\r', '\n');

        if (breakAt >= 0)
        {
            line = line[..breakAt];
        }

        line = line.Trim();

        return line.Length > ErrorSummaryLimit
            ? string.Concat(line[..(ErrorSummaryLimit - 1)].TrimEnd(), "…")
            : line.ToString();
    }
}
