namespace SpaceSnoop.Extensions;

public static class TreeNodeExtensions
{
    /// <summary>
    /// Преобразовать данные о пространстве в узел и добавить в коллекцию.
    /// </summary>
    public static TreeNode AddSpaceNode(this TreeNodeCollection nodes, SpaceBase space)
    {
        var node = new TreeNode(space.ToString())
        {
            Tag = space,
            ToolTipText = space.GetTooltipText(),
        };

        nodes.Add(node);

        return node;
    }

    /// <summary>
    /// Добавить к родительскому узлу файлы и подкаталоги, которые находятся в директории родительского узла.
    /// </summary>
    public static TreeNode FillParentNode(this TreeNode parent, DirectorySpace directory)
    {
        foreach (var file in directory.Files)
        {
            parent.Nodes.AddSpaceNode(file);
        }

        foreach (var subDirectory in directory.SubDirectories)
        {
            parent.Nodes.AddSpaceNode(subDirectory);
        }

        return parent;
    }
}
