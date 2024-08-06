namespace SpaceSnoop.Extensions;

public static class TreeNodeExtension
{
    public static TreeNode AddDirectoryNode(this TreeNodeCollection nodeCollection, DirectorySpace spaceData)
    {
        TreeNode node = new(spaceData.ToString())
        {
            Tag = spaceData,
            ToolTipText = spaceData.GetTooltipText()
        };

        nodeCollection.Add(node);
        return node;
    }

    public static TreeNode AddDirectoryNode(this TreeNode parent, DirectorySpace spaceData)
    {
        TreeNode newParent = parent.Nodes.AddDirectoryNode(spaceData);
        return newParent;
    }

    public static void AddDirectoryNodes(this TreeNode parent, DirectorySpace spaceData)
    {
        long maxSize = spaceData.MaxTotalSize;

        foreach (DirectorySpace diskSpace in spaceData.SubDirectories)
        {
            TreeNode node = parent.AddDirectoryNode(diskSpace);
            node.ForeColor = diskSpace.GetColorBasedOnSize(maxSize);
        }
    }
}
