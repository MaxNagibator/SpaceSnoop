using KeepShell.Diagnostics;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class PerformanceHudViewModel : ObservableObject
{
    private readonly PerformanceMonitor _monitor;

    private readonly PerformanceOperations _operations;

    private readonly ShellPreferences _preferences;

    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private string _delayText = string.Empty;

    [ObservableProperty]
    private string _memoryText = string.Empty;

    [ObservableProperty]
    private string _collectionsText = string.Empty;

    [ObservableProperty]
    private string? _operationText;

    [ObservableProperty]
    private bool _isHitch;

    public PerformanceHudViewModel(PerformanceMonitor monitor, PerformanceOperations operations, ShellPreferences preferences)
    {
        _monitor = monitor;
        _operations = operations;
        _preferences = preferences;
        _preferences.PropertyChanged += OnPreferencesChanged;
        _monitor.Updated += OnMonitorUpdated;

        IsVisible = _preferences.ShowPerformanceHud;
        Apply(_monitor.Snapshot);
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ShellPreferences.ShowPerformanceHud))
        {
            return;
        }

        IsVisible = _preferences.ShowPerformanceHud;

        if (!IsVisible)
        {
            return;
        }

        _monitor.Start();
        Apply(_monitor.Snapshot);
    }

    private void OnMonitorUpdated(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        Apply(_monitor.Snapshot);
    }

    private void Apply(PerformanceSnapshot snapshot)
    {
        var current = _operations.Current;

        SummaryText = PerformanceText.Summary(snapshot, current);
        DelayText = PerformanceText.Delay(snapshot.UiDelayMs, snapshot.UiPeakMs);
        MemoryText = PerformanceText.Memory(snapshot.ManagedBytes, snapshot.WorkingSetBytes);
        CollectionsText = PerformanceText.Collections(snapshot.Gen0Collections, snapshot.Gen1Collections, snapshot.Gen2Collections);
        OperationText = PerformanceText.Operation(current);
        IsHitch = snapshot.UiPeakMs >= AppDefaults.PerformanceHitchMs;
    }
}
