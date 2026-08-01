namespace SpaceSnoop.Wpf.ViewModels.Dialogs;

public sealed partial class DeleteRowViewModel(SpaceBase space) : ObservableObject
{
    [ObservableProperty]
    private DeleteRowState _state;

    [ObservableProperty]
    private string? _error;

    public SpaceBase Space { get; } = space;

    public string Path { get; } = space.AbsolutePath;

    public string Name { get; } = space.Name;

    public string ParentPath { get; } = System.IO.Path.GetDirectoryName(space.AbsolutePath) ?? string.Empty;

    public string SizeText { get; } = SizeFormatter.Format(space.TotalSize);

    public bool IsDirectory { get; } = space is DirectorySpace;
}
