namespace SpaceSnoop.Wpf.Views;

public partial class PerformanceView : UserControl, IView<PerformanceViewModel>
{
    public PerformanceView()
    {
        InitializeComponent();

        Loaded += (_, _) => (DataContext as PerformanceViewModel)?.SetActive(true);
        Unloaded += (_, _) => (DataContext as PerformanceViewModel)?.SetActive(false);
    }
}
