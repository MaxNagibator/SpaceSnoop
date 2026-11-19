using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

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

    private void OnTargetDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && StartTarget(FindTarget(e.OriginalSource as DependencyObject)))
        {
            e.Handled = true;
        }
    }

    private void OnTargetKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && StartTarget(e.OriginalSource as RadioButton))
        {
            e.Handled = true;
            return;
        }

        if (DirectionOf(e.Key) is { } direction
            && e.OriginalSource is RadioButton { DataContext: DriveItem } source
            && MoveTarget(source, direction))
        {
            e.Handled = true;
        }
    }

    private static FocusNavigationDirection? DirectionOf(Key key)
    {
        return key switch
        {
            Key.Left => FocusNavigationDirection.Left,
            Key.Right => FocusNavigationDirection.Right,
            Key.Up => FocusNavigationDirection.Up,
            Key.Down => FocusNavigationDirection.Down,
            _ => null,
        };
    }

    private bool MoveTarget(RadioButton source, FocusNavigationDirection direction)
    {
        if (source.PredictFocus(direction) is not UIElement next || !IsAncestorOf(next))
        {
            return false;
        }

        next.Focus();

        if (next is RadioButton { IsEnabled: true, DataContext: DriveItem target }
            && DataContext is ScanViewModel scan)
        {
            scan.SelectTargetCommand.Execute(target.Path);
        }

        return true;
    }

    private RadioButton? FindTarget(DependencyObject? source)
    {
        while (source is not null && !ReferenceEquals(source, this))
        {
            switch (source)
            {
                case RadioButton target:
                    return target;

                case ButtonBase:
                    return null;
            }

            source = source is Visual or Visual3D
                ? VisualTreeHelper.GetParent(source)
                : LogicalTreeHelper.GetParent(source);
        }

        return null;
    }

    private bool StartTarget(RadioButton? source)
    {
        if (source is not { IsEnabled: true, DataContext: DriveItem target }
            || DataContext is not ScanViewModel scan)
        {
            return false;
        }

        scan.SelectTargetCommand.Execute(target.Path);

        if (!scan.StartCommand.CanExecute(null))
        {
            return false;
        }

        scan.StartCommand.Execute(null);

        return true;
    }
}
