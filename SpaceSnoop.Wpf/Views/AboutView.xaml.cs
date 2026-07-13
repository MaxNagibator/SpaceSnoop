namespace SpaceSnoop.Wpf.Views;

public partial class AboutView : UserControl, IView<AboutViewModel>
{
    public AboutView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AboutViewModel viewModel)
        {
            viewModel.Updater.LoadChangelogCommand.Execute(null);
        }
    }
}
