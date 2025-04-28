namespace SpaceSnoop.Services;

public class ColorService(SpaceColorCalculator spaceColorCalculator) : IDisposable
{
    private TrackBar? _intensityBar;

    public event EventHandler<int>? IntensityChanged;

    public void Dispose()
    {
        if (_intensityBar != null)
        {
            _intensityBar.ValueChanged -= OnIntensityChanged;
            _intensityBar.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    public void Initialize(TrackBar intensityBar)
    {
        _intensityBar = intensityBar;

        _intensityBar.ValueChanged += OnIntensityChanged;

        _intensityBar.Minimum = SpaceColorCalculator.MinIntensity;
        _intensityBar.Maximum = SpaceColorCalculator.MaxIntensity;
        _intensityBar.Value = SpaceColorCalculator.DefaultIntensity;
    }

    public void UpdateNodesColor(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            UpdateAssignedNodesColor(node);
        }
    }

    public void UpdateAssignedNodesColor(TreeNode parent)
    {
        foreach (TreeNode node in parent.Nodes)
        {
            if (parent.Tag is DirectorySpace directorySpace)
            {
                UpdateSpaceNodeColor(node, directorySpace);
            }
        }
    }

    private void OnIntensityChanged(object? sender, EventArgs e)
    {
        if (_intensityBar != null)
        {
            IntensityChanged?.Invoke(this, _intensityBar.Value);
            spaceColorCalculator.Intensity = _intensityBar.Value;
        }
    }

    private void UpdateSpaceNodeColor(TreeNode node, DirectorySpace parent)
    {
        if (node.Tag is DirectorySpace directorySpace)
        {
            node.ForeColor = spaceColorCalculator.GetColorBasedOnSize(directorySpace, parent.MaxTotalSize);

            foreach (TreeNode childNode in node.Nodes)
            {
                UpdateSpaceNodeColor(childNode, directorySpace);
            }
        }
        else if (node.Tag is FileSpace fileSpace)
        {
            node.ForeColor = spaceColorCalculator.GetColorBasedOnSize(fileSpace, parent.Size);
        }
    }
}
