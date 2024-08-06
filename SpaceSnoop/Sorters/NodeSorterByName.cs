namespace SpaceSnoop.Sorters;

public class NodeSorterByName : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return string.Compare(directoryX.Name, directoryY.Name, StringComparison.CurrentCulture);
    }
}
