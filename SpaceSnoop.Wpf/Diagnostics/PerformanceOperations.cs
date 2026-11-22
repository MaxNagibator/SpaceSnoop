using KeepShell.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Diagnostics;

public sealed class PerformanceOperations(PerformanceMonitor monitor)
{
    private static readonly TimeSpan DefaultRenderedFrameTimeout = TimeSpan.FromMilliseconds(250);

    private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    private readonly Lock _gate = new();

    private PerformanceOperation? _current;

    private PerformanceOperation? _last;

    private PerformanceCleanupRun? _lastCleanup;

    private IDisposable? _frames;
    private bool _hasVisibleWindow;

    public event EventHandler? Changed;

    public PerformanceOperation? Current => Volatile.Read(ref _current);

    public PerformanceOperation? Last => Volatile.Read(ref _last);

    public PerformanceCleanupRun? LastCleanup => Volatile.Read(ref _lastCleanup);

    public void ReportCleanup(PerformanceCleanupRun run)
    {
        Volatile.Write(ref _lastCleanup, run);
    }

    public bool TryReport(PerformanceOperation operation, PerformanceOperation? own)
    {
        RefreshWindowVisibility();
        bool started;
        lock (_gate)
        {
            if (_current is not null && !ReferenceEquals(_current, own))
            {
                return false;
            }

            started = _current is null;
            Volatile.Write(ref _current, operation);
            monitor.SetPhase(operation.Name);
            _frames ??= monitor.WatchFrames();
        }

        if (started)
        {
            Notify();
        }

        return true;
    }

    public void Release(PerformanceOperation? own)
    {
        lock (_gate)
        {
            if (own is null || !ReferenceEquals(_current, own))
            {
                return;
            }

            Volatile.Write(ref _current, null);
            monitor.SetPhase(null);

            _frames?.Dispose();
            _frames = null;
        }

        Notify();
    }

    public void ReportRun(PerformanceOperation run)
    {
        RefreshWindowVisibility();
        Volatile.Write(ref _last, run);
        Notify();
    }

    public async Task ReleaseAfterRenderAsync(PerformanceOperation? own)
    {
        try
        {
            if (own is not null)
            {
                await FlushFramesAsync();
            }
        }
        finally
        {
            Release(own);
        }
    }

    public Task FlushFramesAsync()
    {
        RefreshWindowVisibility();
        return Volatile.Read(ref _hasVisibleWindow) && Application.Current is { } app && !app.Dispatcher.HasShutdownStarted
            ? app.Dispatcher.InvokeAsync(
                    static () =>
                    {
                    },
                    DispatcherPriority.ContextIdle)
                .Task
            : Task.CompletedTask;
    }

    public Task WaitForRenderedFrameAsync(CancellationToken cancellationToken = default)
    {
        return WaitForRenderedFrameAsync(DefaultRenderedFrameTimeout, cancellationToken);
    }

    internal async Task<bool> WaitForRenderedFrameAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var waitToken = timeoutCts.Token;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler? handler = null;
        Application? subscribedApp = null;

        try
        {
            await FlushFramesAsync().WaitAsync(waitToken);

            RefreshWindowVisibility();

            if (!Volatile.Read(ref _hasVisibleWindow) || Application.Current is not { } app || app.Dispatcher.HasShutdownStarted)
            {
                return false;
            }

            subscribedApp = app;
            var subscribed = await app.Dispatcher.InvokeAsync(
                () =>
                {
                    if (app.MainWindow is not { IsVisible: true } window || window.WindowState == WindowState.Minimized)
                    {
                        return false;
                    }

                    var remaining = 2;
                    handler = (_, _) =>
                    {
                        if (--remaining > 0)
                        {
                            return;
                        }

                        if (Interlocked.Exchange(ref handler, null) is { } current)
                        {
                            CompositionTarget.Rendering -= current;
                        }

                        tcs.TrySetResult();
                    };
                    CompositionTarget.Rendering += handler;
                    return true;
                },
                DispatcherPriority.Normal,
                waitToken);

            if (!subscribed)
            {
                return false;
            }

            await tcs.Task.WaitAsync(waitToken);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (subscribedApp is { } app
                && Interlocked.Exchange(ref handler, null) is { } current
                && !app.Dispatcher.HasShutdownStarted)
            {
                if (app.Dispatcher.CheckAccess())
                {
                    CompositionTarget.Rendering -= current;
                }
                else
                {
                    _ = app.Dispatcher.BeginInvoke(() => CompositionTarget.Rendering -= current);
                }
            }
        }
    }

    public void ClearRun()
    {
        if (Volatile.Read(ref _last) is null)
        {
            return;
        }

        Volatile.Write(ref _last, null);
        Notify();
    }

    private void RefreshWindowVisibility()
    {
        if (Application.Current is not { } app)
        {
            Volatile.Write(ref _hasVisibleWindow, false);
        }
        else if (app.Dispatcher.CheckAccess())
        {
            Volatile.Write(ref _hasVisibleWindow, app.MainWindow is { IsVisible: true } window && window.WindowState != WindowState.Minimized);
        }
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
