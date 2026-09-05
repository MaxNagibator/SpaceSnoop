using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Scan;

public partial class ScanTargetPicker : UserControl
{
    public ScanTargetPicker()
    {
        InitializeComponent();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && DataContext is ScanViewModel scan)
        {
            scan.Drives.ReloadLabels(scan.SelectedDrive);
        }
    }

    private void OnVolumeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Volumes.SelectedItem is DriveItem drive && DataContext is ScanViewModel scan)
        {
            scan.SelectedDrive = drive.Path;
        }
    }

    private void OnVolumeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(Volumes, source) is not ListBoxItem { IsEnabled: true })
        {
            return;
        }

        if (DataContext is ScanViewModel scan && scan.StartCommand.CanExecute(null))
        {
            scan.StartCommand.Execute(null);
        }
    }
}
