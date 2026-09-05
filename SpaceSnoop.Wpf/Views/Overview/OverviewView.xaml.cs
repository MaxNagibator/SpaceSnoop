using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Views.Overview;

public partial class OverviewView : UserControl, IView<OverviewViewModel>
{
    private bool _restored;

    public OverviewView()
    {
        InitializeComponent();
    }

    private OverviewViewModel? ViewModel => DataContext as OverviewViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _restored = false;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ViewModel is { } vm)
            {
                Scroller.ScrollToVerticalOffset(vm.Rows.ScrollOffset);
            }

            _restored = true;
        });
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_restored && ViewModel is { } vm)
        {
            vm.Rows.ScrollOffset = Scroller.VerticalOffset;
        }
    }
}
