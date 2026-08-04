using KeepShell.Services;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewViewModel : ObservableObject, IPageHeader, IPageStatus, IPageRefresh
{
    private readonly ISettingsStore _settings;
    private bool _suppressReload;

    public OverviewViewModel(ISettingsStore settings, IDialogService dialogs, ToastNotifier notifier, ILogger<OverviewViewModel> logger, CompareDirectoriesUseCase compare, ExecuteSyncUseCase sync)
    {
        _settings = settings;

        Rows = new(settings, () => IsBusy, () => _suppressReload, ReloadRows);

        Batch = new(settings, dialogs, notifier, logger, compare, sync, Rows);
        Batch.PropertyChanged += OnBatchPropertyChanged;

        ReloadRows();
    }

    public event Action<SyncProfile, ComparisonResult?>? OpenInSyncRequested;

    public OverviewRowsViewModel Rows { get; }

    public OverviewBatchViewModel Batch { get; }

    public string PageTitle => "Обзор";

    public string PageDescription => "Пакетное сравнение всех профилей синхронизации без записи – что и куда нужно копировать.";

    public string? RefreshTooltip => "Перечитать профили";

    public bool IsBusy => Batch.IsBusy;

    public string StatusCaption => Batch.StatusCaption;

    public bool IsIndeterminate => Batch.IsIndeterminate;

    public double ProgressValue => Batch.ProgressValue;

    public double ProgressMax => Batch.ProgressMax;

    public ICommand CancelCommand => Batch.CancelOperationCommand;

    ICommand IPageRefresh.RefreshCommand => ReloadCommand;

    public void ApplyProfileRun(SyncProfileRun run)
    {
        if (IsBusy)
        {
            return;
        }

        var row = Rows.Rows.FirstOrDefault(item => string.Equals(item.Profile.Id, run.ProfileId, StringComparison.Ordinal));

        if (row is null)
        {
            return;
        }

        row.Error = null;
        row.ElapsedMs = run.ElapsedMs;

        if (run.Report is { } report)
        {
            row.Comparison = null;
            row.ApplySyncReport(report);
        }
        else if (run.Comparison is { } comparison)
        {
            row.ApplyStatistics(comparison.GetStatistics(), comparison.GetDirectoryStatistics());
            row.ApplyFreshness(SyncFreshness.Compute(comparison.Root));
            row.Comparison = comparison;
            row.Status = OverviewRunStatus.Compared;
        }

        Rows.RefreshView();
    }

    private void OnBatchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OverviewBatchViewModel.IsBusy):
                OnPropertyChanged(nameof(IsBusy));
                CycleAllDirectionsCommand.NotifyCanExecuteChanged();
                break;

            case nameof(OverviewBatchViewModel.StatusCaption):
            case nameof(OverviewBatchViewModel.IsIndeterminate):
            case nameof(OverviewBatchViewModel.ProgressValue):
            case nameof(OverviewBatchViewModel.ProgressMax):
                OnPropertyChanged(e.PropertyName);
                break;

            default:
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCycleAllDirections))]
    private void CycleAllDirections()
    {
        foreach (var row in Rows.Rows)
        {
            row.AdvanceDirection();
        }

        PersistProfiles();
    }

    private bool CanCycleAllDirections()
    {
        return !IsBusy && Rows.Rows.Count > 0;
    }

    private void PersistProfiles()
    {
        _suppressReload = true;

        try
        {
            SyncProfileStore.Save(_settings, Rows.Rows.Select(static row => row.Profile));
        }
        finally
        {
            _suppressReload = false;
        }

        Batch.CompareAllCommand.NotifyCanExecuteChanged();
        Batch.SyncAllCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Reload()
    {
        ReloadRows();
    }

    private void ReloadRows()
    {
        Rows.Rows.Clear();

        foreach (var profile in SyncProfileStore.Load(_settings))
        {
            Rows.Rows.Add(new(profile, RaiseOpenInSync, PersistProfiles));
        }

        Rows.NotifyRowsChanged();
        Batch.CompareAllCommand.NotifyCanExecuteChanged();
        Batch.SyncAllCommand.NotifyCanExecuteChanged();
        CycleAllDirectionsCommand.NotifyCanExecuteChanged();
    }

    private void RaiseOpenInSync(SyncProfile profile, ComparisonResult? comparison)
    {
        OpenInSyncRequested?.Invoke(profile, comparison);
    }
}
