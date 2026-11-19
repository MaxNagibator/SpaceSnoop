using System.Windows.Controls.Primitives;

namespace SpaceSnoop.Wpf.Views.Scan;

public partial class ScanInspectorRail : UserControl
{
    public ScanInspectorRail()
    {
        InitializeComponent();
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        MoreMenu.PlacementTarget = MoreButton;
        MoreMenu.Placement = PlacementMode.Left;
        MoreMenu.DataContext = DataContext;
        MoreMenu.IsOpen = true;
    }
}
