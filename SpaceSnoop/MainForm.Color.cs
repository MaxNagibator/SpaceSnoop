namespace SpaceSnoop;

public partial class MainForm
{
    private readonly SpaceColorCalculator _spaceColorCalculator = new();

    private void InitializeColor()
    {
        _intensityBar.ValueChanged += OnIntensityChanged;

        _intensityBar.Minimum = SpaceColorCalculator.MinIntensity;
        _intensityBar.Maximum = SpaceColorCalculator.MaxIntensity;
        _intensityBar.Value = SpaceColorCalculator.DefaultIntensity;
    }

    private void FinalizeColor()
    {
        _intensityBar.ValueChanged -= OnIntensityChanged;
    }

    private void OnIntensityChanged(object? sender, EventArgs e)
    {
        _controlGroupBox.Text = $"Интенсивность: {_intensityBar.Value}";
        _spaceColorCalculator.Intensity = _intensityBar.Value;
        UpdateNodeColors();
    }

    private void UpdateNodeColors()
    {
        foreach (TreeNode node in _directoriesTreeView.Nodes)
        {
            UpdateNodeColors(node);
        }
    }

    private void UpdateNodeColors(TreeNode parent)
    {
        foreach (TreeNode node in parent.Nodes)
        {
            if (parent.Tag is DirectorySpace directorySpace)
            {
                UpdateNodeColor(node, directorySpace);
            }
        }
    }

    private void UpdateNodeColor(TreeNode node, DirectorySpace parent)
    {
        if (node.Tag is DirectorySpace directorySpace)
        {
            node.ForeColor = _spaceColorCalculator.GetColorBasedOnSize(directorySpace, parent.MaxTotalSize);

            foreach (TreeNode childNode in node.Nodes)
            {
                UpdateNodeColor(childNode, directorySpace);
            }
        }
        else if (node.Tag is FileSpace fileSpace)
        {
            node.ForeColor = _spaceColorCalculator.GetColorBasedOnSize(fileSpace, parent.Size);
        }
    }
}
