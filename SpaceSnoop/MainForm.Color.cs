namespace SpaceSnoop;

public partial class MainForm
{
    private readonly SpaceColorCalculator _spaceColorCalculator = new();

    private void InitializeColor()
    {
        _refreshNodesButton.Click += OnRefreshColorClicked;
        _intensityNumericUpDown.ValueChanged += OnIntensityChanged;

        _intensityNumericUpDown.Minimum = SpaceColorCalculator.MinIntensity;
        _intensityNumericUpDown.Maximum = SpaceColorCalculator.MaxIntensity;
        _intensityNumericUpDown.Value = SpaceColorCalculator.DefaultIntensity;
    }

    private void FinalizeColor()
    {
        _refreshNodesButton.Click -= OnRefreshColorClicked;
        _intensityNumericUpDown.ValueChanged -= OnIntensityChanged;
    }

    private void OnIntensityChanged(object? sender, EventArgs e)
    {
        _spaceColorCalculator.Intensity = (int)_intensityNumericUpDown.Value;
        UpdateNodeColors();
    }

    private void OnRefreshColorClicked(object? sender, EventArgs e)
    {
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
