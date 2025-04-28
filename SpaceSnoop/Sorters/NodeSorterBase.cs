namespace SpaceSnoop.Sorters;

public abstract class NodeSorterBase : IComparer
{
    private static bool _isInverted;

    public static void SetInversion(bool isInverted)
    {
        _isInverted = isInverted;
    }

    public int Compare(object? x, object? y)
    {
        if (x is not TreeNode nodeX || y is not TreeNode nodeY)
        {
            return 0;
        }

        var result = nodeX.Tag switch
        {
            FileSpace when nodeY.Tag is DirectorySpace => _isInverted ? -1 : 1,
            DirectorySpace when nodeY.Tag is FileSpace => _isInverted ? 1 : -1,
            DirectorySpace directoryX when nodeY.Tag is DirectorySpace directoryY => CompareDirectorySpace(directoryX, directoryY),
            FileSpace fileX when nodeY.Tag is FileSpace fileY => CompareFileSpace(fileX, fileY),
            _ => 0,
        };

        return _isInverted ? -result : result;
    }

    protected abstract int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY);
    protected abstract int CompareFileSpace(FileSpace fileX, FileSpace fileY);
}
