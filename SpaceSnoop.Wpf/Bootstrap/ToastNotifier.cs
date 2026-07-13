namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ToastNotifier(ToastHostViewModel toasts, ShellPreferences prefs)
{
    public void Notify(string message, StatusSeverity severity = StatusSeverity.Info)
    {
        if (prefs.EnableToastNotifications)
        {
            toasts.Show(message, severity);
        }
    }
}
