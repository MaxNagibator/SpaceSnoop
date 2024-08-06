namespace SpaceSnoop.Sorters;

public class NodeSorterByTotalSize : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return directoryX.TotalSize.CompareTo(directoryY.TotalSize);
    }
}
