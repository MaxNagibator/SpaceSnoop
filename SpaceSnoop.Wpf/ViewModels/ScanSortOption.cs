namespace SpaceSnoop.Wpf.ViewModels;

public sealed class ScanSortOption(string name, ScanSortField field)
{
    public string Name { get; } = name;

    public ScanSortField Field { get; } = field;

    public override string ToString()
    {
        return Name;
    }
}
