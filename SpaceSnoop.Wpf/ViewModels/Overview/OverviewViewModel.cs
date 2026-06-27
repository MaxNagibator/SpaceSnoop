using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewViewModel : ObservableObject, IPageHeader, IPageStatus, IPageRefresh
{
    private readonly ISettingsStore _settings;
    private readonly ILogger<OverviewViewModel> _logger;
    private readonly ILogger<DirectoryComparer> _comparerLogger;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareAllCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusCaption = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax;

    public OverviewViewModel(ISettingsStore settings, ILogger<OverviewViewModel> logger, ILogger<DirectoryComparer> comparerLogger)
    {
        _settings = settings;
        _logger = logger;
        _comparerLogger = comparerLogger;

        ReloadRows();
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<SyncProfile>? OpenInSyncRequested;

    public ObservableCollection<OverviewRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    public string PageTitle => "Обзор";

    public string PageDescription => "Пакетное сравнение всех профилей синхронизации без записи – что и куда нужно копировать.";

    public string? RefreshTooltip => "Перечитать профили";

    public ICommand CancelCommand => CancelOperationCommand;

    ICommand IPageRefresh.RefreshCommand => ReloadCommand;

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles && !IsBusy)
        {
            ReloadRows();
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompareAll))]
    private async Task CompareAll()
    {
        _cts = new();
        var token = _cts.Token;

        IsBusy = true;
        IsIndeterminate = false;
        ProgressMax = Rows.Count;
        ProgressValue = 0;

        var total = Rows.Count;
        var compared = 0;
        var failed = 0;
        var stopwatch = Stopwatch.StartNew();

        _logger.OverviewCompareStarted(total);

        try
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var row = Rows[i];
                ProgressValue = i;
                StatusCaption = $"Пара {i + 1} из {total} · {row.Name}";

                var preflight = OverviewPipeline.Classify(row.Profile);

                if (preflight is not null)
                {
                    row.Error = null;
                    row.Status = preflight.Value;
                    continue;
                }

                row.Status = OverviewRunStatus.Comparing;
                var rowStopwatch = Stopwatch.StartNew();

                try
                {
                    var filter = new ExclusionFilter(row.Profile.Exclusions);
                    var result = await Task.Run(() => new DirectoryComparer(filter, _comparerLogger).Compare(row.Profile.Left.Trim(), row.Profile.Right.Trim(), token),
                        token);

                    rowStopwatch.Stop();
                    row.ApplyStatistics(result.GetStatistics(), result.GetDirectoryStatistics());
                    row.ApplyFreshness(SyncFreshness.Compute(result.Root));
                    row.ElapsedMs = (long)rowStopwatch.Elapsed.TotalMilliseconds;
                    row.Error = null;
                    row.Status = OverviewRunStatus.Compared;
                    compared++;
                }
                catch (OperationCanceledException)
                {
                    row.Status = OverviewRunStatus.None;
                    throw;
                }
                catch (Exception exception)
                {
                    row.Error = exception.Unwrap().Message;
                    row.Status = OverviewRunStatus.Error;
                    failed++;
                }
            }

            ProgressValue = total;
            stopwatch.Stop();
            StatusCaption = $"Сравнено пар: {compared}, ошибок: {failed}";
            _logger.OverviewCompareFinished(compared, failed, (long)stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Сравнение отменено.";
            _logger.OverviewCompareCancelled();
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
    }

    [RelayCommand]
    private void Reload()
    {
        ReloadRows();
    }

    private bool CanCompareAll()
    {
        return !IsBusy && Rows.Count > 0;
    }

    private void ReloadRows()
    {
        Rows.Clear();

        foreach (var profile in SyncProfileStore.Load(_settings))
        {
            Rows.Add(new(profile, RaiseOpenInSync));
        }

        OnPropertyChanged(nameof(HasRows));
        CompareAllCommand.NotifyCanExecuteChanged();
    }

    private void RaiseOpenInSync(SyncProfile profile)
    {
        OpenInSyncRequested?.Invoke(profile);
    }
}
