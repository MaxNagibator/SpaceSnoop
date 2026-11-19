using System.Collections.Specialized;

namespace SpaceSnoop.Wpf.Bootstrap;

public sealed class ToastNotifier
{
    private const string UndoCaption = "Отменить";

    private readonly ToastHostViewModel _toasts;
    private readonly ShellPreferences _prefs;
    private Action? _lastUndo;
    private ToastViewModel? _lastUndoToast;

    public ToastNotifier(ToastHostViewModel toasts, ShellPreferences prefs)
    {
        _toasts = toasts;
        _prefs = prefs;
        _toasts.Toasts.CollectionChanged += OnToastsChanged;
    }

    public void ClearUndo()
    {
        _lastUndo = null;
        _lastUndoToast = null;
    }

    public void Notify(string message, StatusSeverity severity = StatusSeverity.Info, Action? onUndo = null)
    {
        if (onUndo is not null && _lastUndoToast is { } previousToast)
        {
            _toasts.DismissCommand.Execute(previousToast);
        }

        _lastUndo = onUndo;
        _lastUndoToast = null;

        if (!_prefs.EnableToastNotifications)
        {
            return;
        }

        if (onUndo is null)
        {
            _toasts.Show(message, severity);
            return;
        }

        ToastViewModel? toast = null;
        var undone = false;
        var undo = new RelayCommand(() =>
        {
            if (undone || !ReferenceEquals(_lastUndo, onUndo))
            {
                return;
            }

            undone = true;
            ClearUndo();
            onUndo();
            _toasts.DismissCommand.Execute(toast);
        });

        toast = _toasts.Show(message, severity, null, UndoCaption, undo);
        _lastUndoToast = toast;
    }

    private void OnToastsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_lastUndoToast is not null && !_toasts.Toasts.Contains(_lastUndoToast))
        {
            ClearUndo();
        }
    }
}
