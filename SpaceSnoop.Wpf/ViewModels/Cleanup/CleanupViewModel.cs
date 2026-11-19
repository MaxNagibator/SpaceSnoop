using KeepShell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus, ICleanupAutomation
{
    internal const string MeasuringStatus = "Замеряю…";

    private readonly CleanupProgressDialogFactory _dialogFactory;
    private readonly IDialogService _dialogs;
    private readonly ModalHostViewModel _modals;
    private readonly IShellLauncher _shell;
    private readonly ISettingsStore _settings;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ToastNotifier _notifier;
    private readonly ILogger<CleanupViewModel> _logger;

    private readonly Func<TimeSpan, IReadOnlyList<CleanupTarget>> _catalog;
    private readonly Func<CleanupTarget, CancellationToken, Task<CleanupMeasurement>> _measure;
    private CancellationTokenSource? _measureCts;
    private bool _loadedOnce;
    private bool _suppressPersist;
    private bool _rebuildPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyPropertyChangedFor(nameof(IsIndeterminate))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMax = 1;

    [ObservableProperty]
    private int _minAgeHours = AppDefaults.CleanupMinAgeHoursDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalText))]
    private long _totalBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedText))]
    [NotifyPropertyChangedFor(nameof(HasSelectedBytes))]
    private long _selectedBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private int _selectedFiles;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private int _selectedCount;

    public ObservableCollection<CleanupTargetViewModel> Targets { get; } = [];

    public string PageTitle => "Windows";

    public string PageDescription => "Временные файлы, кэши и корзина. Удаление безвозвратно, мимо корзины.";

    public string? RefreshTooltip => "Замерить корзины заново";

    public string? StatusCaption => StatusText;

    public bool IsIdle => !IsBusy;

    public bool IsIndeterminate => _measureCts is null;

    public bool CanClean => !IsBusy && SelectedCount > 0 && SelectedFiles > 0;

    public string TotalText => SizeFormatter.Format(TotalBytes);

    public string SelectedText => SizeFormatter.Format(SelectedBytes);

    public bool HasSelectedBytes => SelectedBytes > 0;

    public ICommand? CancelCommand => IsBusy && _measureCts is not null ? CancelMeasureCommand : null;

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public CleanupViewModel(
        CleanupService service,
        CleanupProgressDialogFactory dialogFactory,
        IDialogService dialogs,
        ModalHostViewModel modals,
        IShellLauncher shell,
        ISettingsStore settings,
        IUiDispatcher uiDispatcher,
        ToastNotifier notifier,
        ILogger<CleanupViewModel> logger)
        : this(
            dialogFactory,
            dialogs,
            modals,
            shell,
            settings,
            uiDispatcher,
            notifier,
            logger,
            static minAge => CleanupCatalog.BuildDefault(minAge),
            service.MeasureAsync)
    {
    }

    internal CleanupViewModel(
        CleanupProgressDialogFactory dialogFactory,
        IDialogService dialogs,
        ModalHostViewModel modals,
        IShellLauncher shell,
        ISettingsStore settings,
        IUiDispatcher uiDispatcher,
        ToastNotifier notifier,
        ILogger<CleanupViewModel> logger,
        Func<TimeSpan, IReadOnlyList<CleanupTarget>> catalog,
        Func<CleanupTarget, CancellationToken, Task<CleanupMeasurement>> measure)
    {
        _catalog = catalog;
        _measure = measure;
        _dialogFactory = dialogFactory;
        _dialogs = dialogs;
        _modals = modals;
        _shell = shell;
        _settings = settings;
        _uiDispatcher = uiDispatcher;
        _notifier = notifier;
        _logger = logger;

        LoadSettings();
        BuildTargets();
    }

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce || IsBusy)
        {
            return;
        }

        await RefreshAsync();
    }

    partial void OnMinAgeHoursChanged(int value)
    {
        var clamped = Math.Clamp(value, AppDefaults.CleanupMinAgeHoursMin, AppDefaults.CleanupMinAgeHoursMax);

        if (clamped != value)
        {
            MinAgeHours = clamped;
            return;
        }

        Persist(() => _settings.SetInt(SettingsKeys.CleanupMinAgeHours, clamped));

        if (IsBusy)
        {
            _rebuildPending = true;
            return;
        }

        StatusText = null;
        BuildTargets();
    }

    private void LoadSettings()
    {
        _suppressPersist = true;
        MinAgeHours = Math.Clamp(
            _settings.GetInt(SettingsKeys.CleanupMinAgeHours, AppDefaults.CleanupMinAgeHoursDefault),
            AppDefaults.CleanupMinAgeHoursMin,
            AppDefaults.CleanupMinAgeHoursMax);

        _suppressPersist = false;
    }

    private void BuildTargets()
    {
        var selected = ReadSelectedIds();

        foreach (var row in Targets)
        {
            row.PropertyChanged -= OnTargetChanged;
        }

        Targets.Clear();

        foreach (var target in _catalog(TimeSpan.FromHours(MinAgeHours)))
        {
            var row = new CleanupTargetViewModel(target)
            {
                IsSelected = selected.Contains(target.Id),
            };

            row.PropertyChanged += OnTargetChanged;
            Targets.Add(row);
        }

        UpdateTotals();
        _loadedOnce = false;
    }

    private HashSet<string> ReadSelectedIds()
    {
        var stored = _settings.GetStringValue(SettingsKeys.CleanupSelected);

        return stored is null
            ? []
            : [.. stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    private void OnTargetChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(CleanupTargetViewModel.IsSelected))
        {
            return;
        }

        UpdateTotals();
        PersistSelection();
    }

    private void PersistSelection()
    {
        Persist(() => _settings.SetValue(
            SettingsKeys.CleanupSelected,
            string.Join(',', Targets.Where(static x => x.IsSelected).Select(static x => x.Model.Id))));
    }

    private void Persist(Action write)
    {
        if (!_suppressPersist)
        {
            write();
        }
    }

    private void UpdateTotals()
    {
        var rows = Targets.ToList();
        var tally = CleanupTotals.Compute(rows);

        TotalBytes = tally.TotalBytes;
        SelectedBytes = tally.SelectedBytes;
        SelectedFiles = tally.SelectedFiles;
        SelectedCount = tally.SelectedCount;

        CleanupTotals.ApplyShares(rows, tally.TotalBytes);
    }

    private List<CleanupTargetViewModel> SelectedRows()
    {
        return [.. Targets.Where(static x => x.IsSelected && x.CanClean)];
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _measureCts = cancellation;
        ProgressValue = 0;
        ProgressMax = Targets.Count;
        IsBusy = true;
        StatusText = MeasuringStatus;

        try
        {
            while (true)
            {
                await MeasureTargetsPassAsync(cancellation.Token);
                if (!ApplyPendingRebuild())
                {
                    break;
                }
            }

            _loadedOnce = true;
            StatusText = $"Найдено {SizeFormatter.Format(TotalBytes)}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Замер отменён.";
        }
        finally
        {
            _measureCts = null;
            IsBusy = false;
            if (ApplyPendingRebuild() && !cancellation.IsCancellationRequested)
            {
                StatusText = null;
            }
        }
    }

    private async Task MeasureTargetsPassAsync(CancellationToken token)
    {
        ProgressValue = 0;
        ProgressMax = Targets.Count;

        foreach (var row in Targets.ToList())
        {
            token.ThrowIfCancellationRequested();
            if (_rebuildPending)
            {
                return;
            }

            StatusText = $"Замер {ProgressValue + 1} из {ProgressMax}: {row.Name}";
            await MeasureRowAsync(row, token);
            ProgressValue++;
        }
    }

    private bool ApplyPendingRebuild()
    {
        if (!_rebuildPending)
        {
            return false;
        }

        _rebuildPending = false;
        _uiDispatcher.Invoke(BuildTargets);

        return true;
    }

    [RelayCommand]
    private void CancelMeasure()
    {
        _measureCts?.Cancel();
    }

    [RelayCommand]
    private async Task MeasureOneAsync(CleanupTargetViewModel? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var targetId = row.Model.Id;
        using var cancellation = new CancellationTokenSource();
        _measureCts = cancellation;
        ProgressValue = 0;
        ProgressMax = 1;
        IsBusy = true;

        try
        {
            var current = row;
            while (true)
            {
                ProgressValue = 0;
                StatusText = $"Замер: {current.Name}";
                await MeasureRowAsync(current, cancellation.Token);
                ProgressValue = 1;

                if (!ApplyPendingRebuild())
                {
                    break;
                }

                current = Targets.FirstOrDefault(x => string.Equals(x.Model.Id, targetId, StringComparison.Ordinal));
                if (current is null)
                {
                    StatusText = null;
                    return;
                }
            }

            StatusText = $"{current.Name}: {(current.HasAvailabilityIssue ? current.AvailabilityText : current.SizeText)}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Замер отменён.";
        }
        finally
        {
            _measureCts = null;
            IsBusy = false;
            if (ApplyPendingRebuild() && !cancellation.IsCancellationRequested)
            {
                StatusText = null;
            }
        }
    }

    private async Task MeasureRowAsync(CleanupTargetViewModel row, CancellationToken token)
    {
        row.IsMeasuring = true;

        try
        {
            row.Apply(await _measure(row.Model, token));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var unwrapped = exception.Unwrap();
            _logger.CleanupMeasureFailed(unwrapped, row.Model.Id);
            row.Apply(new(0, 0, [], CleanupAvailability.Failed), unwrapped.Message);
        }
        finally
        {
            row.IsMeasuring = false;
            UpdateTotals();
        }
    }
}
