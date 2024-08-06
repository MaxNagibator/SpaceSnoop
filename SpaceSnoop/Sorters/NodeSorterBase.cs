using System.Collections;

namespace SpaceSnoop.Sorters;

public abstract class NodeSorterBase : IComparer
{
    private static bool _isInverted;

    public int Compare(object? x, object? y)
    {
        if (x is not TreeNode nodeX || y is not TreeNode nodeY)
        {
            return 0;
        }

        if (nodeX.Tag is not DirectorySpace directoryX || nodeY.Tag is not DirectorySpace directoryY)
        {
            return 0;
        }

        int result = CompareDirectorySpace(directoryX, directoryY);
        return _isInverted ? -result : result;
    }

    public static void SetInversion(bool isInverted)
    {
        _isInverted = isInverted;
    }

    protected abstract int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY);
}
