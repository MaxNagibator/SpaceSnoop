namespace SpaceSnoop.Wpf.Views.Scan;

public partial class ScanToolbar : UserControl
{
    public ScanToolbar()
    {
        InitializeComponent();
    }

    private void OnDrivesDropDownOpened(object sender, EventArgs e)
    {
        if (DataContext is ScanViewModel scan)
        {
            scan.Drives.ReloadLabels();
        }
    }
}
