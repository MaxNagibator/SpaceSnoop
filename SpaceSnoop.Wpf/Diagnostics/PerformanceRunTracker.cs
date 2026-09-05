using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceRunTracker
{
    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    private PerformanceOperation? _last;

    public event EventHandler? Changed;

    public PerformanceOperation? Last => Volatile.Read(ref _last);

    public void Report(PerformanceOperation run)
    {
        Volatile.Write(ref _last, run);
        Notify();
    }

    public void Clear()
    {
        if (Volatile.Read(ref _last) is null)
        {
            return;
        }

        Volatile.Write(ref _last, null);
        Notify();
    }

    private void Notify()
    {
        if (_dispatcher.CheckAccess())
        {
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));
    }
}
