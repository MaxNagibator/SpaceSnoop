namespace SpaceSnoop.Wpf.Views;

public partial class DockerView : UserControl, IView<DockerViewModel>
{
    public DockerView()
    {
        InitializeComponent();
    }

    private void SuppressAutoScroll(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is not TextBox)
        {
            e.Handled = true;
        }
    }
}
