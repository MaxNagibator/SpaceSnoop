namespace SpaceSnoop.Sorters;

public class NodeSorterByLastAccessTime : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return directoryX.LastAccessTime.CompareTo(directoryY.LastAccessTime);
    }

    protected override int CompareFileSpace(FileSpace fileX, FileSpace fileY)
    {
        return fileX.LastAccessTime.CompareTo(fileY.LastAccessTime);
    }
}
