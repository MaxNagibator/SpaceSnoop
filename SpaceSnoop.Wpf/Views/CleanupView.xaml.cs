namespace SpaceSnoop.Wpf.Views;

public partial class CleanupView : UserControl, IView<CleanupPageViewModel>
{
    public CleanupView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is CleanupPageViewModel vm)
            {
                await vm.EnsureLoadedAsync();
            }
        };
    }

    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is not TextBox)
        {
            e.Handled = true;
        }
    }
}
