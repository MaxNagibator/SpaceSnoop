using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Chat;

public partial class ChatComposer : UserControl
{
    public ChatComposer()
    {
        InitializeComponent();
    }

    private void OnInputPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers == ModifierKeys.Shift)
        {
            return;
        }

        e.Handled = true;

        if (DataContext is ChatViewModel vm && vm.SendCommand.CanExecute(null))
        {
            vm.SendCommand.Execute(null);
        }
    }
}
