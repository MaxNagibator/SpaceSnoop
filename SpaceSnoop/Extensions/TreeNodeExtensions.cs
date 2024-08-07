namespace SpaceSnoop.Extensions;

public static class TreeNodeExtensions
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
        return parent.Nodes.AddDirectoryNode(spaceData);
    }

    public static TreeNode AddDirectoryNodes(this TreeNode parent, DirectorySpace spaceData)
    {
        foreach (DirectorySpace diskSpace in spaceData.SubDirectories)
        {
            parent.AddDirectoryNode(diskSpace);
        }

        foreach (FileSpace file in spaceData.Files)
        {
            TreeNode fileNode = new(file.ToString())
            {
                Tag = file,
                ToolTipText = file.GetTooltipText()
            };

            parent.Nodes.Add(fileNode);
        }

        return parent;
    }
}
