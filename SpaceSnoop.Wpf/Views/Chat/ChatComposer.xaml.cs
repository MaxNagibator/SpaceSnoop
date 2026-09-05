using System.Windows.Input;

namespace SpaceSnoop.Wpf.Views.Chat;

public partial class ChatComposer : UserControl
{
    public ChatComposer()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => FocusInput();
        Unloaded += (_, _) =>
        {
            if (DataContext is ChatViewModel vm)
            {
                vm.FocusRequested -= FocusInput;
            }
        };
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ChatViewModel previous)
        {
            previous.FocusRequested -= FocusInput;
        }

        if (e.NewValue is ChatViewModel current)
        {
            current.FocusRequested += FocusInput;
        }
    }

    private void FocusInput()
    {
        Dispatcher.BeginInvoke(() =>
        {
            Input.Focus();
            Input.CaretIndex = Input.Text.Length;
        });
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
