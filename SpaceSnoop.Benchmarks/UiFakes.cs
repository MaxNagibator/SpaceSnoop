using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Sync;

namespace SpaceSnoop.Benchmarks;

internal sealed class BenchUiDispatcher : IUiDispatcher
{
    public bool HasAccess => true;

    public void Invoke(Action action)
    {
        action();
    }

    public IUiTimer CreateTimer(TimeSpan interval, Action tick)
    {
        return new BenchUiTimer
        {
            Interval = interval,
        };
    }
}

internal sealed class BenchUiTimer : IUiTimer
{
    public bool IsRunning { get; private set; }

    public TimeSpan Interval { get; set; }

    public void Start()
    {
        IsRunning = true;
    }

    public void Stop()
    {
        IsRunning = false;
    }
}

internal sealed class BenchSettingsStore : ISettingsStore
{
    public event EventHandler<string>? Changed
    {
        add => _ = value;
        remove => _ = value;
    }

    public event EventHandler<SettingsWriteFailedEventArgs>? WriteFailed
    {
        add => _ = value;
        remove => _ = value;
    }

    public string FilePath => string.Empty;

    public string? GetStringValue(string key)
    {
        return null;
    }

    public void SetValue(string key, string value)
    {
    }

    public void Flush()
    {
    }
}

internal sealed class BenchDialogService : IDialogService
{
    public Task<bool> ShowAsync(IDialogViewModel viewModel)
    {
        return Task.FromResult(false);
    }

    public Task<bool> ReplaceAsync(IDialogViewModel viewModel)
    {
        return Task.FromResult(false);
    }

    public bool Confirm(string title, string message, bool defaultYes = false)
    {
        return false;
    }

    public bool ConfirmWarning(string title, string message, bool defaultYes = false)
    {
        return false;
    }

    public void Info(string title, string message)
    {
    }

    public void Warning(string title, string message)
    {
    }

    public void Error(string title, string message)
    {
    }
}

internal sealed class BenchShellLauncher : IShellLauncher
{
    public bool Open(string pathOrUrl)
    {
        return false;
    }

    public bool Reveal(string path)
    {
        return false;
    }

    public bool Start(string executable, params string[] arguments)
    {
        return false;
    }
}

internal sealed class BenchSyncRowHost : ISyncRowHost
{
    public bool BlankAbsent => false;

    public bool ChatEnabled => false;

    public void ToggleExpand(DirectoryComparison dir)
    {
    }

    public void ToggleGroup(string? key)
    {
    }

    public void ExpandSubtree(DirectoryComparison dir)
    {
    }

    public void CollapseSubtree(DirectoryComparison dir)
    {
    }

    public void ApplyToSubtree(DirectoryComparison dir, SyncAction action)
    {
    }

    public void NotifyActionsChanged()
    {
    }

    public Task CompareContentAsync(FileComparison file)
    {
        return Task.CompletedTask;
    }

    public void AskAgentAbout(SyncNodeViewModel node)
    {
    }
}

internal sealed class BenchShell : IDisposable
{
    public ScanNodeFactory NodeFactory { get; }

    public ScanSortState Sort { get; } = new();

    public BenchUiDispatcher Dispatcher { get; }

    public BenchSettingsStore Settings { get; }

    public PerformanceMonitor Monitor { get; }

    public PerformanceOperations Operations { get; }

    public ShellPreferences Preferences { get; }

    public BenchShell()
    {
        Dispatcher = new();
        Settings = new();
        Monitor = new(new(), Dispatcher, NullLogger<PerformanceMonitor>.Instance);
        Operations = new(Monitor);
        Preferences = new(Settings);
        NodeFactory = new(new(Settings), new(Settings), new BenchShellLauncher());
    }

    public ScanNodeViewModel CreateNode(DirectorySpace root)
    {
        return NodeFactory.Create(root, root.TotalSize, root.TotalSize, root, Sort);
    }

    public void Dispose()
    {
        Monitor.Dispose();
    }
}
