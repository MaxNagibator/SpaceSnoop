namespace SpaceSnoop.Wpf.Views;

public partial class SettingsView : UserControl, IView<SettingsViewModel>
{
    public SettingsView()
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
