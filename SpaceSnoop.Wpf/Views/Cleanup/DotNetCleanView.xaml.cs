namespace SpaceSnoop.Wpf.Views.Cleanup;

public partial class DotNetCleanView : UserControl, IView<DotNetCleanViewModel>
{
    public DotNetCleanView()
    {
        InitializeComponent();
    }

    private void ScrollOutputToEnd(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox output)
        {
            output.Dispatcher.BeginInvoke(new Action(output.ScrollToEnd));
        }
    }
}
