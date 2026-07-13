namespace SpaceSnoop.Wpf.Views.Schedule;

public partial class ScheduleView : UserControl, IView<ScheduleViewModel>
{
    public ScheduleView()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            if (DataContext is ScheduleViewModel vm)
            {
                vm.Refresh();
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
