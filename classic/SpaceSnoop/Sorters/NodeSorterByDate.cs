namespace SpaceSnoop.Sorters;

public class NodeSorterByDate : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return directoryX.CreationDate.CompareTo(directoryY.CreationDate);
    }

    protected override int CompareFileSpace(FileSpace fileX, FileSpace fileY)
    {
        return fileX.CreationDate.CompareTo(fileY.CreationDate);
    }
}
