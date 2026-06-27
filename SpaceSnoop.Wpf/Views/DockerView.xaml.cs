namespace SpaceSnoop.Wpf.Views;

public partial class DockerView : UserControl, IView<DockerViewModel>
{
    public DockerView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is DockerViewModel vm)
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
