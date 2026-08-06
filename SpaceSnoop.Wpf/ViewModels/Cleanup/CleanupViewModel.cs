using KeepShell.Services;
using KeepShell.Services.Platform;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Cleanup;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Cleanup;

public sealed partial class CleanupViewModel : ObservableObject, IPageHeader, IPageRefresh, IPageStatus, ICleanupAutomation
{
    private readonly CleanupService _service;
    private readonly CleanupProgressDialogFactory _dialogFactory;
    private readonly IDialogService _dialogs;
    private readonly ModalHostViewModel _modals;
    private readonly IShellLauncher _shell;
    private readonly ISettingsStore _settings;
    private readonly ToastNotifier _notifier;
    private readonly ILogger<CleanupViewModel> _logger;

    private CancellationTokenSource? _measureCts;
    private bool _loadedOnce;
    private bool _suppressPersist;
    private bool _rebuildPending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    [NotifyPropertyChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(CleanSelectedCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusCaption))]
    private string? _statusText;

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

    public CleanupViewModel(
        CleanupService service,
        CleanupProgressDialogFactory dialogFactory,
        IDialogService dialogs,
        ModalHostViewModel modals,
        IShellLauncher shell,
        ISettingsStore settings,
        ToastNotifier notifier,
        ILogger<CleanupViewModel> logger)
    {
        _service = service;
        _dialogFactory = dialogFactory;
        _dialogs = dialogs;
        _modals = modals;
        _shell = shell;
        _settings = settings;
        _notifier = notifier;
        _logger = logger;

        LoadSettings();
        BuildTargets();
    }

    public ObservableCollection<CleanupTargetViewModel> Targets { get; } = [];

    public string PageTitle => "Очистка";

    public string PageDescription => "Временные файлы, кэши и корзина. Удаление безвозвратно, мимо корзины.";

    public string? RefreshTooltip => "Замерить корзины заново";

    public string? StatusCaption => StatusText;

    public bool IsIdle => !IsBusy;

    public bool IsIndeterminate => true;

    public double ProgressValue => 0;

    public double ProgressMax => 1;

    public bool CanClean => !IsBusy && SelectedCount > 0 && SelectedFiles > 0;

    public string TotalText => SizeFormatter.Format(TotalBytes);

    public string SelectedText => SizeFormatter.Format(SelectedBytes);

    public bool HasSelectedBytes => SelectedBytes > 0;

    public ICommand? CancelCommand => IsBusy && _measureCts is not null ? CancelMeasureCommand : null;

    ICommand IPageRefresh.RefreshCommand => RefreshCommand;

    public async Task EnsureLoadedAsync()
    {
        if (_loadedOnce)
        {
            return;
        }

        _loadedOnce = true;
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

        foreach (var target in CleanupCatalog.BuildDefault(TimeSpan.FromHours(MinAgeHours)))
        {
            var row = new CleanupTargetViewModel(target) { IsSelected = selected.Contains(target.Id) };
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

    private void OnTargetChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
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
        long total = 0;
        long selected = 0;
        var count = 0;
        var files = 0;

        foreach (var row in Targets)
        {
            if (row.IsAvailable)
            {
                total += row.SizeBytes;
            }

            if (!row.IsSelected || !row.IsAvailable)
            {
                continue;
            }

            selected += row.SizeBytes;
            files += row.Files;
            count++;
        }

        TotalBytes = total;
        SelectedBytes = selected;
        SelectedFiles = files;
        SelectedCount = count;

        foreach (var row in Targets)
        {
            row.Share = total > 0 && row.IsAvailable ? (double)row.SizeBytes / total : 0;
        }
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

        _measureCts = new();
        IsBusy = true;
        StatusText = "Замеряю…";

        try
        {
            foreach (var row in Targets.ToList())
            {
                _measureCts.Token.ThrowIfCancellationRequested();
                await MeasureRowAsync(row, _measureCts.Token);
            }

            StatusText = $"Найдено {SizeFormatter.Format(TotalBytes)}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Замер отменён.";
        }
        finally
        {
            _measureCts.Dispose();
            _measureCts = null;
            IsBusy = false;
            ApplyPendingRebuild();
        }
    }

    private void ApplyPendingRebuild()
    {
        if (!_rebuildPending)
        {
            return;
        }

        _rebuildPending = false;
        BuildTargets();
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

        IsBusy = true;

        try
        {
            await MeasureRowAsync(row, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
            ApplyPendingRebuild();
        }
    }

    private async Task MeasureRowAsync(CleanupTargetViewModel row, CancellationToken token)
    {
        row.IsMeasuring = true;

        try
        {
            row.Apply(await _service.MeasureAsync(row.Model, token));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.CleanupMeasureFailed(exception.Unwrap(), row.Model.Id);
            row.Apply(new(0, 0, [], CleanupAvailability.Missing));
        }
        finally
        {
            row.IsMeasuring = false;
            UpdateTotals();
        }
    }

    [RelayCommand]
    private void OpenFolder(CleanupTargetViewModel? row)
    {
        if (row is not { HasPath: true, Path.Length: > 0 })
        {
            return;
        }

        if (!_shell.Open(row.Path))
        {
            _notifier.Notify($"Не удалось открыть «{row.Path}».", StatusSeverity.Warning);
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var row in Targets)
        {
            row.IsSelected = row.CanClean;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var row in Targets)
        {
            row.IsSelected = false;
        }
    }

    [RelayCommand]
    private Task CleanOneAsync(CleanupTargetViewModel? row)
    {
        return row is null || !row.CanClean || IsBusy
            ? Task.CompletedTask
            : RunCleanupAsync([row]);
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private Task CleanSelectedAsync()
    {
        return RunCleanupAsync(SelectedRows());
    }

    private async Task RunCleanupAsync(IReadOnlyList<CleanupTargetViewModel> rows)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var bytes = rows.Sum(static x => x.SizeBytes);
        var files = rows.Sum(static x => x.Files);
        var confirm = BuildConfirm(rows, bytes, files, null);

        if (!await _dialogs.ShowAsync(confirm))
        {
            return;
        }

        await RunConfirmedAsync(rows, bytes, files, CancellationToken.None, false);
    }

    private async Task<CleanupProgressDialogViewModel> RunConfirmedAsync(
        IReadOnlyList<CleanupTargetViewModel> rows,
        long bytes,
        int files,
        CancellationToken cancellationToken,
        bool automated)
    {
        var dialog = _dialogFactory.Create(new([.. rows.Select(static x => x.Model)], bytes, files, cancellationToken));

        IsBusy = true;

        try
        {
            if (automated)
            {
                await RunDialogForAutomationAsync(dialog);
            }
            else
            {
                await _dialogs.ShowAsync(dialog);
            }
        }
        finally
        {
            await dialog.StopAsync();
            IsBusy = false;
        }

        _notifier.Notify(
            dialog.StatusText,
            dialog.HasErrors || dialog.WasCancelled || dialog.FreedBytes == 0 ? StatusSeverity.Warning : StatusSeverity.Success);
        StatusText = dialog.StatusText;

        foreach (var row in rows)
        {
            await MeasureOneAsync(row);
        }

        return dialog;
    }

    private ConfirmDialogViewModel BuildConfirm(
        IReadOnlyList<CleanupTargetViewModel> rows,
        long bytes,
        int files,
        string? requestedBy)
    {
        List<ConfirmLine> lines = [];

        if (requestedBy is not null)
        {
            lines.Add(new ConfirmTextLine(requestedBy, ConfirmTextTone.Strong));
            lines.Add(new ConfirmGapLine());
        }

        lines.Add(new ConfirmTextLine($"Будет очищено корзин: {rows.Count:N0}."));
        lines.Add(new ConfirmMetricLine("Файлов", $"{files:N0}", SizeFormatter.Format(bytes)));

        lines.AddRange(rows.Select(static row =>
            new ConfirmMetricLine(row.Name, row.FilesText, row.SizeText, ConfirmMetricTone.Sub)));

        return new ConfirmDialogViewModel(
            "Очистка диска",
            PackIconLucideKind.Trash2,
            lines,
            [
                new("Отмена", ConfirmChoiceKind.Dismissive),
                new("Очистить", ConfirmChoiceKind.Destructive),
            ])
        {
            Warning = "Файлы удаляются безвозвратно, мимо корзины – восстановить их нельзя.",
        };
    }
}
