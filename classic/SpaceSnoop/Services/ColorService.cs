namespace SpaceSnoop.Services;

public sealed class ColorService(SpaceColorCalculator spaceColorCalculator) : IDisposable
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
        // TODO: Требуется переосмысление

        if (node.Tag is not SpaceBase space)
        {
            return;
        }

        Color? foreColor = space.State switch
        {
            SpaceState.Deleted => Color.Gray,
            SpaceState.Error => Color.Black,
            _ => null,
        };

        switch (space)
        {
            case DirectorySpace directorySpace:
                foreColor ??= spaceColorCalculator.GetColorBasedOnSize(directorySpace, parent.MaxTotalSize);

                foreach (TreeNode childNode in node.Nodes)
                {
                    UpdateSpaceNodeColor(childNode, directorySpace);
                }

                break;

            case FileSpace fileSpace:
                foreColor ??= spaceColorCalculator.GetColorBasedOnSize(fileSpace, parent.Size);
                break;
        }

        if (space.State == SpaceState.Error)
        {
            node.BackColor = Color.Yellow;
        }

        if (foreColor != null)
        {
            node.ForeColor = foreColor.Value;
        }
    }
}
