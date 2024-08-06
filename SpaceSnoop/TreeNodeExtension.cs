namespace SpaceSnoop;

public static class TreeNodeExtension
{
    public static TreeNode AddDirectoryNode(this TreeNodeCollection nodeCollection, DirectorySpace spaceData)
    {
        TreeNode node = new(spaceData.ToString())
        {
            Tag = spaceData
        };

        nodeCollection.Add(node);
        return node;
    }

    public static TreeNode AddDirectoryNode(this TreeNode parent, DirectorySpace spaceData)
    {
        TreeNode newParent = parent.Nodes.AddDirectoryNode(spaceData);
        newParent.Tag = spaceData;

        return newParent;
    }

    public static void AddDirectoryNodes(this TreeNode parent, DirectorySpace spaceData)
    {
        foreach (DirectorySpace diskSpace in spaceData.SubDirectories)
        {
            parent.AddDirectoryNode(diskSpace);
        }
    }
}
