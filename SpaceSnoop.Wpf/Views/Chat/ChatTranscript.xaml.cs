namespace SpaceSnoop.Wpf.Views.Chat;

public partial class ChatTranscript : UserControl
{
    private bool _stickToBottom = true;

    public ChatTranscript()
    {
        InitializeComponent();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange > 0)
        {
            if (_stickToBottom)
            {
                Scroller.ScrollToEnd();
            }

            return;
        }

        if (Math.Abs(e.VerticalChange) > 0.01)
        {
            _stickToBottom = Scroller.ScrollableHeight - Scroller.VerticalOffset < 4;
        }
    }
}
