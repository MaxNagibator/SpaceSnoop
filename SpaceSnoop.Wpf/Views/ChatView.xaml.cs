namespace SpaceSnoop.Wpf.Views;

public partial class ChatView : UserControl, IView<ChatViewModel>
{
    public ChatView()
    {
        InitializeComponent();

        Loaded += async (_, _) =>
        {
            if (DataContext is ChatViewModel vm)
            {
                await vm.EnsureLoadedAsync();
            }
        };
    }
}
