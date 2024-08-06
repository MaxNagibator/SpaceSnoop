namespace SpaceSnoop.Sorters;

public class NodeSorterByDate : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return directoryX.CreationDate.CompareTo(directoryY.CreationDate);
    }
}
