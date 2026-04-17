namespace SpaceSnoop.Wpf.ViewModels;

public sealed class ScanSortState : IComparer<SpaceBase>
{
    public ScanSortField Field { get; set; } = ScanSortField.Size;

    public bool Invert { get; set; } = true;

    public int Compare(SpaceBase? x, SpaceBase? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return 1;
        }

        if (y is null)
        {
            return -1;
        }

        var xIsDirectory = x is DirectorySpace;
        var yIsDirectory = y is DirectorySpace;

        if (xIsDirectory != yIsDirectory)
        {
            return xIsDirectory ? -1 : 1;
        }

        var result = Field switch
        {
            ScanSortField.Name => string.Compare(x.Name, y.Name, StringComparison.CurrentCulture),
            ScanSortField.Size => x.TotalSize.CompareTo(y.TotalSize),
            ScanSortField.CreationDate => x.CreationDate.CompareTo(y.CreationDate),
            ScanSortField.LastAccessTime => x.LastAccessTime.CompareTo(y.LastAccessTime),
            _ => 0,
        };

        return Invert ? -result : result;
    }
}
