namespace SpaceSnoop.Sorters;

public class NodeSorterByTotalSize : NodeSorterBase
{
    protected override int CompareDirectorySpace(DirectorySpace directoryX, DirectorySpace directoryY)
    {
        return directoryX.TotalSize.CompareTo(directoryY.TotalSize);
    }

    protected override int CompareFileSpace(FileSpace fileX, FileSpace fileY)
    {
        return fileX.Size.CompareTo(fileY.Size);
    }
}
