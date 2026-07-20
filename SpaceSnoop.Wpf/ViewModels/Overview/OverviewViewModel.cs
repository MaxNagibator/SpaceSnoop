using KeepShell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Overview;

public sealed partial class OverviewViewModel : ObservableObject, IPageHeader, IPageStatus, IPageRefresh
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ToastNotifier _notifier;
    private readonly ILogger<OverviewViewModel> _logger;
    private readonly ILogger<DirectoryComparer> _comparerLogger;
    private readonly ILogger<SyncEngine> _engineLogger;

    private CancellationTokenSource? _cts;
    private bool _suppressReload;
    private readonly bool _suppressPersist;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareAllCommand), nameof(SyncRowCommand), nameof(SyncAllCommand), nameof(CycleAllDirectionsCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private OverviewSortField _sortField = AppDefaults.OverviewSortDefault;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _groupUnchanged = AppDefaults.OverviewGroupUnchangedDefault;

    [ObservableProperty]
    private string _statusCaption = string.Empty;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax;

    public OverviewViewModel(ISettingsStore settings, IDialogService dialogs, ToastNotifier notifier, ILogger<OverviewViewModel> logger, ILogger<DirectoryComparer> comparerLogger, ILogger<SyncEngine> engineLogger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _notifier = notifier;
        _logger = logger;
        _comparerLogger = comparerLogger;
        _engineLogger = engineLogger;

        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = FilterRow;

        _suppressPersist = true;
        SortField = _settings.GetEnum(SettingsKeys.OverviewSort, AppDefaults.OverviewSortDefault);
        SortDescending = _settings.GetBool(SettingsKeys.OverviewSortDesc);
        GroupUnchanged = _settings.GetBool(SettingsKeys.OverviewGroupUnchanged, AppDefaults.OverviewGroupUnchangedDefault);
        _suppressPersist = false;

        UpdateSortAndGroup();
        ReloadRows();
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<SyncProfile, ComparisonResult?>? OpenInSyncRequested;

    public ObservableCollection<OverviewRowViewModel> Rows { get; } = [];

    public ICollectionView RowsView { get; }

    public bool HasRows => Rows.Count > 0;

    public bool SearchTextEmpty => string.IsNullOrWhiteSpace(SearchText);

    public bool NoMatches => Rows.Count > 0 && RowsView.IsEmpty;

    public double ScrollOffset { get; set; }

    public string PageTitle => "Обзор";

    public string PageDescription => "Пакетное сравнение всех профилей синхронизации без записи – что и куда нужно копировать.";

    public string? RefreshTooltip => "Перечитать профили";

    public ICommand CancelCommand => CancelOperationCommand;

    ICommand IPageRefresh.RefreshCommand => ReloadCommand;

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles && !IsBusy && !_suppressReload)
        {
            ReloadRows();
        }
    }

    private static (int Synced, int Failed, int Skipped) Tally(OverviewRowViewModel row)
    {
        return row.Status switch
        {
            OverviewRunStatus.Synced when row.SyncErrors == 0 => (1, 0, 0),
            OverviewRunStatus.Synced or OverviewRunStatus.Error => (0, 1, 0),
            _ => (0, 0, 1),
        };
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
        var skipped = 0;
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
                    skipped++;
                    continue;
                }

                row.Comparison = null;
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
                    row.Comparison = result;
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
            StatusCaption = $"Сравнено пар: {compared}, ошибок: {failed}, пропущено: {skipped}";
            _logger.OverviewCompareFinished(compared, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, compared, failed);
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
            RefreshView();
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncRow))]
    private async Task SyncRow(OverviewRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var lines = new[]
        {
            $"Профиль: {row.Name}",
            $"{row.Left} → {row.Right}",
            string.Empty,
            "Файлы будут скопированы по направлению профиля, удаления – в корзину.",
            string.Empty,
            "Продолжить?",
        };

        if (!_dialogs.Confirm("Синхронизация профиля", string.Join(Environment.NewLine, lines)))
        {
            return;
        }

        _cts = new();
        var token = _cts.Token;

        IsBusy = true;
        IsIndeterminate = true;
        ProgressMax = 0;
        ProgressValue = 0;
        StatusCaption = $"Синхронизация · {row.Name}";

        var stopwatch = Stopwatch.StartNew();
        _logger.OverviewSyncStarted(1);

        try
        {
            await SyncRowCore(row, token);
            stopwatch.Stop();
            var (synced, failed, skipped) = Tally(row);
            StatusCaption = row.StatusText;
            _logger.OverviewSyncFinished(synced, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(row.StatusText, synced, failed);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Синхронизация отменена.";
            _logger.OverviewSyncCancelled();
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            RefreshView();
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncAll))]
    private async Task SyncAll()
    {
        var lines = new[]
        {
            $"Синхронизировать все профили: {Rows.Count}.",
            string.Empty,
            "Файлы будут скопированы по направлению каждого профиля, удаления – в корзину.",
            string.Empty,
            "Продолжить?",
        };

        if (!_dialogs.Confirm("Синхронизация всех профилей", string.Join(Environment.NewLine, lines)))
        {
            return;
        }

        _cts = new();
        var token = _cts.Token;

        IsBusy = true;
        IsIndeterminate = false;
        ProgressMax = Rows.Count;
        ProgressValue = 0;

        var total = Rows.Count;
        var synced = 0;
        var failed = 0;
        var skipped = 0;
        var stopwatch = Stopwatch.StartNew();

        _logger.OverviewSyncStarted(total);

        try
        {
            for (var i = 0; i < Rows.Count; i++)
            {
                token.ThrowIfCancellationRequested();

                var row = Rows[i];
                ProgressValue = i;
                StatusCaption = $"Пара {i + 1} из {total} · {row.Name}";

                await SyncRowCore(row, token);

                var (rowSynced, rowFailed, rowSkipped) = Tally(row);
                synced += rowSynced;
                failed += rowFailed;
                skipped += rowSkipped;
            }

            ProgressValue = total;
            stopwatch.Stop();
            StatusCaption = $"Синхронизировано профилей: {synced}, c ошибками: {failed}, пропущено: {skipped}";
            _logger.OverviewSyncFinished(synced, failed, skipped, (long)stopwatch.Elapsed.TotalMilliseconds);
            NotifyResult(StatusCaption, synced, failed);
        }
        catch (OperationCanceledException)
        {
            StatusCaption = "Синхронизация отменена.";
            _logger.OverviewSyncCancelled();
        }
        finally
        {
            IsBusy = false;
            IsIndeterminate = false;
            RefreshView();
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelOperation()
    {
        _cts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanCycleAllDirections))]
    private void CycleAllDirections()
    {
        foreach (var row in Rows)
        {
            row.AdvanceDirection();
        }

        PersistProfiles();
    }

    private bool CanCycleAllDirections()
    {
        return !IsBusy && Rows.Count > 0;
    }

    private void PersistProfiles()
    {
        _suppressReload = true;

        try
        {
            SyncProfileStore.Save(_settings, Rows.Select(static row => row.Profile));
        }
        finally
        {
            _suppressReload = false;
        }
    }

    [RelayCommand]
    private void Reload()
    {
        ReloadRows();
    }

    private async Task SyncRowCore(OverviewRowViewModel row, CancellationToken token)
    {
        var preflight = OverviewPipeline.Classify(row.Profile);

        if (preflight is not null)
        {
            row.Error = null;
            row.Status = preflight.Value;
            return;
        }

        var profile = row.Profile;
        var left = profile.Left.Trim();
        var right = profile.Right.Trim();
        var mode = HeadlessSync.MapMode(profile.Mode);
        var mirror = profile.Mirror;
        var winner = profile.Winner;

        if (mirror && SyncProfile.MirrorSource(mode, winner, left, right) is { } mirrorSource
            && (!Directory.Exists(mirrorSource) || !Directory.EnumerateFileSystemEntries(mirrorSource).Any()))
        {
            row.Error = "Зеркало отменено: источник пуст.";
            row.Status = OverviewRunStatus.Error;
            return;
        }

        row.Comparison = null;
        row.Status = OverviewRunStatus.Syncing;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var report = await Task.Run(() =>
                {
                    var filter = new ExclusionFilter(profile.Exclusions);
                    var result = new DirectoryComparer(filter, _comparerLogger).Compare(left, right, token);
                    result.ApplyMode(mode, mirror, winner);
                    result.ResolveAllConflicts(SyncAction.Skip);
                    return new SyncEngine(_engineLogger, false).Execute(result, token);
                },
                token);

            stopwatch.Stop();
            row.ApplySyncReport(report);
            row.ElapsedMs = (long)stopwatch.Elapsed.TotalMilliseconds;
            row.Error = null;
            WriteSyncLog(profile.Name, report);
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
        }
    }

    private void NotifyResult(string caption, int ok, int failed)
    {
        var severity = failed > 0 ? StatusSeverity.Error
            : ok == 0 ? StatusSeverity.Warning
            : StatusSeverity.Success;

        _notifier.Notify(caption, severity);
    }

    private void WriteSyncLog(string name, SyncReport report)
    {
        try
        {
            SyncLog.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Обзор [{name}]: {report.SuccessCount} успешно, {report.Errors.Count} ошибок", report);
        }
        catch (Exception exception)
        {
            _logger.SyncLogWriteFailed(exception);
        }
    }

    private bool CanCompareAll()
    {
        return !IsBusy && Rows.Count > 0;
    }

    private bool CanSyncRow(OverviewRowViewModel? row)
    {
        return !IsBusy && row is not null;
    }

    private bool CanSyncAll()
    {
        return !IsBusy && Rows.Count > 0;
    }

    private void ReloadRows()
    {
        Rows.Clear();

        foreach (var profile in SyncProfileStore.Load(_settings))
        {
            Rows.Add(new(profile, RaiseOpenInSync, PersistProfiles));
        }

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(NoMatches));
        CompareAllCommand.NotifyCanExecuteChanged();
        SyncAllCommand.NotifyCanExecuteChanged();
        CycleAllDirectionsCommand.NotifyCanExecuteChanged();
    }

    private void RaiseOpenInSync(SyncProfile profile, ComparisonResult? comparison)
    {
        OpenInSyncRequested?.Invoke(profile, comparison);
    }

    private bool FilterRow(object item)
    {
        if (item is not OverviewRowViewModel row)
        {
            return false;
        }

        var query = SearchText.Trim();

        if (query.Length == 0)
        {
            return true;
        }

        return row.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Left.Contains(query, StringComparison.OrdinalIgnoreCase)
               || row.Right.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSortAndGroup()
    {
        using (RowsView.DeferRefresh())
        {
            RowsView.GroupDescriptions.Clear();
            RowsView.SortDescriptions.Clear();

            if (GroupUnchanged)
            {
                RowsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(OverviewRowViewModel.GroupKey)));
                RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.GroupOrder), ListSortDirection.Ascending));
            }

            var direction = SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;

            switch (SortField)
            {
                case OverviewSortField.Name:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.Name), direction));
                    break;

                case OverviewSortField.Differences:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.DiffCount), direction));
                    break;

                case OverviewSortField.Date:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.NewestModified), direction));
                    break;

                case OverviewSortField.Freshness:
                    RowsView.SortDescriptions.Add(new(nameof(OverviewRowViewModel.FreshnessSkew), direction));
                    break;
            }
        }

        OnPropertyChanged(nameof(NoMatches));
    }

    private void Persist(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(key, value);
    }

    private void RefreshView()
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(NoMatches));
    }

    partial void OnSearchTextChanged(string value)
    {
        RowsView.Refresh();
        OnPropertyChanged(nameof(SearchTextEmpty));
        OnPropertyChanged(nameof(NoMatches));
    }

    partial void OnSortFieldChanged(OverviewSortField value)
    {
        Persist(SettingsKeys.OverviewSort, value.ToString());
        UpdateSortAndGroup();
    }

    partial void OnSortDescendingChanged(bool value)
    {
        Persist(SettingsKeys.OverviewSortDesc, value ? "true" : "false");
        UpdateSortAndGroup();
    }

    partial void OnGroupUnchangedChanged(bool value)
    {
        Persist(SettingsKeys.OverviewGroupUnchanged, value ? "true" : "false");
        UpdateSortAndGroup();
    }
}
