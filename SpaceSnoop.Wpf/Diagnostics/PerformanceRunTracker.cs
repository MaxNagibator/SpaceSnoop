namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceRunTracker
{
    private PerformanceOperation? _last;

    public event EventHandler? Changed;

    public PerformanceOperation? Last => Volatile.Read(ref _last);

    public void Report(PerformanceOperation run)
    {
        Volatile.Write(ref _last, run);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (Volatile.Read(ref _last) is null)
        {
            return;
        }

        Volatile.Write(ref _last, null);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
