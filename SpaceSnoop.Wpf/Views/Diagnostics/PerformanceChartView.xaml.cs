namespace SpaceSnoop.Wpf.Views.Diagnostics;

public partial class PerformanceChartView : UserControl, IView<PerformanceChartViewModel>
{
    public PerformanceChartView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        (DataContext as PerformanceChartViewModel)?.SetActive(IsVisible);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        (e.OldValue as PerformanceChartViewModel)?.SetActive(false);
        (e.NewValue as PerformanceChartViewModel)?.SetActive(IsVisible);
    }
}
