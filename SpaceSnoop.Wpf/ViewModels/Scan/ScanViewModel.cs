using KeepShell.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private readonly DiskSpaceCalculator _calculator;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly ScanArchiveViewModel _archive;
    private readonly ILogger<ScanViewModel> _logger;
    private readonly ToastNotifier _notifier;
    private readonly PerformanceMonitor _performance;
    private readonly PerformanceRunTracker _runs;
    private readonly IFilePicker _filePicker;

    private readonly ScanSortState _sortState = new();

    private CancellationTokenSource? _cts;
    private bool _suppressPersist;
    private ScanNodeViewModel? _highlighted;

    [ObservableProperty]
    private string _selectedDrive = string.Empty;

    [ObservableProperty]
    private ScanSortOption? _selectedSortOption;

    [ObservableProperty]
    private bool _invertSort = AppDefaults.ScanSortInvertDefault;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string? _statusCaption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeVisible))]
    [NotifyPropertyChangedFor(nameof(TreemapVisible))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeVisible))]
    [NotifyPropertyChangedFor(nameof(TreemapVisible))]
    private bool _showTreemap = AppDefaults.ScanTreemapDefault;

    [ObservableProperty]
    private ScanNodeViewModel? _selectedNode;

    [ObservableProperty]
    private bool _scanWasCancelled;

    public ScanViewModel(
        DiskSpaceCalculator calculator,
        IDialogService dialogs,
        ISettingsStore settings,
        OperationPreferences operations,
        ScanPreferences preferences,
        ScanInspectorViewModel inspector,
        ScanNodeFactory nodeFactory,
        DeleteProgressDialogFactory deleteDialogFactory,
        ArchiveProgressDialogFactory archiveDialogFactory,
        ILogger<ScanViewModel> logger,
        ToastNotifier notifier,
        PerformanceMonitor performance,
        PerformanceRunTracker runs,
        IFilePicker filePicker,
        IUiDispatcher uiDispatcher)
    {
        _calculator = calculator;
        _performance = performance;
        _runs = runs;
        _dialogs = dialogs;
        _settings = settings;
        _nodeFactory = nodeFactory;
        _logger = logger;
        _notifier = notifier;
        _filePicker = filePicker;

        Inspector = inspector;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Inspector.Intensity = Preferences.Intensity;

        Progress = new(performance, uiDispatcher);
        Progress.PropertyChanged += OnProgressPropertyChanged;

        Summary = new();

        Treemap = new(Roots);
        Treemap.DrilledInto += OnTreemapDrilledInto;

        _nodeFactory.AskAgentRequested += OnAskAgentRequested;

        Marks = new(dialogs,
            deleteDialogFactory,
            operations,
            logger,
            nodeFactory,
            Roots,
            Summary,
            Treemap,
            () => SelectedNode,
            value => SelectedNode = value,
            () => HasResult = false,
            () => IsScanning);

        _archive = new(archiveDialogFactory, dialogs, nodeFactory, Roots, Treemap, Inspector, () => SelectedNode, Marks);

        Drives = new(logger);

        LoadSettings();
        Drives.LoadDriveLabels();
    }

    public event Action<string>? AskAgentRequested;

    public DriveCatalog Drives { get; }

    public ScanMarksViewModel Marks { get; }

    public ObservableCollection<ScanNodeViewModel> Roots { get; } = [];

    public ScanProgressViewModel Progress { get; }

    public ScanSummaryViewModel Summary { get; }

    public ScanTreemapViewModel Treemap { get; }

    public ScanInspectorViewModel Inspector { get; }

    public ObservableCollection<ScanSortOption> SortOptions { get; } =
    [
        new("По имени", ScanSortField.Name),
        new("По размеру", ScanSortField.Size),
        new("По дате создания", ScanSortField.CreationDate),
        new("По времени последнего доступа", ScanSortField.LastAccessTime),
        new("По количеству файлов", ScanSortField.FileCount),
    ];

    public ScanPreferences Preferences { get; }

    public double Intensity
    {
        get => Preferences.Intensity;
        set => Preferences.Intensity = value;
    }

    public string PageTitle => "Сканирование";

    public string PageDescription => "Анализ занятого места по дискам и каталогам.";

    public bool IsBusy => IsScanning;

    public int MarkedCount => Marks.MarkedCount;

    public bool TreeVisible => HasResult && !ShowTreemap;

    public bool TreemapVisible => HasResult && ShowTreemap;

    public bool IsIndeterminate => Progress.IsIndeterminate;

    public double ProgressValue => Progress.ProgressValue;

    public double ProgressMax => Progress.ProgressMax;

    public ICommand CancelCommand => StopCommand;

    internal DirectorySpace? CurrentRoot => Roots.Count > 0 ? Roots[0].Space as DirectorySpace : null;

    internal TimeSpan LastScanElapsed { get; private set; }

    private void OnProgressPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IsIndeterminate) or nameof(ProgressValue))
        {
            OnPropertyChanged(e.PropertyName);
        }
    }

    private void OnTreemapDrilledInto(ScanNodeViewModel node)
    {
        SelectedNode = node;
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ScanPreferences.Intensity))
        {
            return;
        }

        Inspector.Intensity = Preferences.Intensity;
        OnPropertyChanged(nameof(Intensity));
    }

    private void OnAskAgentRequested(ScanNodeViewModel node)
    {
        if (node.Space is not { } space)
        {
            return;
        }

        AskAgentRequested?.Invoke(ChatQuestion.ForScanNode(space.AbsolutePath, node.SizeText, node.IsDirectory));
    }

    partial void OnIsScanningChanged(bool value)
    {
        Progress.IsScanning = value;
        Summary.IsScanning = value;
        Marks.DeleteMarkedCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultChanged(bool value)
    {
        Summary.HasResult = value;
    }

    partial void OnSelectedDriveChanged(string value)
    {
        Persist(() => _settings.SetValue(SettingsKeys.ScanLastDrive, value));
    }

    partial void OnSelectedSortOptionChanged(ScanSortOption? value)
    {
        if (value is null)
        {
            return;
        }

        _sortState.Field = value.Field;
        Persist(() => _settings.SetEnum(SettingsKeys.ScanSortMode, value.Field));
        ResortRoots();
    }

    partial void OnInvertSortChanged(bool value)
    {
        _sortState.Invert = value;
        Persist(() => _settings.SetBool(SettingsKeys.ScanSortInvert, value));
        ResortRoots();
    }

    partial void OnSelectedNodeChanged(ScanNodeViewModel? value)
    {
        _highlighted?.IsSelected = false;
        _highlighted = value;

        if (value is null)
        {
            Inspector.Clear();
        }
        else
        {
            value.IsSelected = true;
            Inspector.Show(value);
        }
    }

    partial void OnShowTreemapChanged(bool value)
    {
        Persist(() => _settings.SetBool(SettingsKeys.ScanTreemap, value));

        if (value && Treemap.TreemapRoot is null && Roots.Count > 0)
        {
            Treemap.SetRoot(Roots[0]);
        }
    }

    private void ResortRoots()
    {
        foreach (var root in Roots)
        {
            root.Resort();
        }
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        var sortField = _settings.GetEnum(SettingsKeys.ScanSortMode, AppDefaults.ScanSortModeDefault);
        var invertSort = _settings.GetBool(SettingsKeys.ScanSortInvert, AppDefaults.ScanSortInvertDefault);
        _sortState.Field = sortField;
        _sortState.Invert = invertSort;
        InvertSort = invertSort;
        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Field == sortField)
                             ?? SortOptions.First(option => option.Field == AppDefaults.ScanSortModeDefault);

        ShowTreemap = _settings.GetBool(SettingsKeys.ScanTreemap);

        var lastDrive = _settings.GetStringValue(SettingsKeys.ScanLastDrive);

        if (!string.IsNullOrWhiteSpace(lastDrive))
        {
            if (!Drives.HasDrive(lastDrive))
            {
                Drives.AddDrive(lastDrive);
            }

            SelectedDrive = lastDrive;
        }
        else
        {
            SelectedDrive = Drives.Items.Count > 0 ? Drives.Items[0].Path : string.Empty;
        }

        _suppressPersist = false;
    }

    private void Persist(Action write)
    {
        if (!_suppressPersist)
        {
            write();
        }
    }

    private bool CanStart()
    {
        return !IsScanning;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        await ScanAsync(SelectedDrive?.Trim() ?? string.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task BrowseAsync()
    {
        if (_filePicker.PickFolder("Выберите каталог для сканирования") is not { } path)
        {
            return;
        }

        if (!Drives.HasDrive(path))
        {
            Drives.AddDrive(path);
            Drives.LoadDriveLabels();
        }

        SelectedDrive = path;
        await ScanAsync(path);
    }

    private bool CanStop()
    {
        return IsScanning;
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _cts?.Cancel();
    }

    private async Task ScanAsync(string path, CancellationToken external = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var directory = new DirectoryInfo(path);

        if (!directory.Exists)
        {
            _dialogs.Warning("Сканирование", $"Каталог не найден: {path}");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        var token = _cts.Token;
        IsScanning = true;
        ScanWasCancelled = false;
        StatusCaption = $"Сканирование: {path}";

        SelectedNode = null;

        var progress = Progress.Begin(directory, path, Preferences.UseMultithreading ? Preferences.MaxParallelism : 1);

        _logger.ScanStarted(path, Preferences.UseMultithreading, Preferences.MaxParallelism);

        try
        {
            var result = await Task.Run(() => Preferences.UseMultithreading
                    ? _calculator.CalculateMultithreaded(directory, Preferences.MaxParallelism, progress, token)
                    : _calculator.Calculate(directory, progress, token),
                token);

            ApplyScanResult(path, result, Progress.Finish());

            _notifier.Notify($"Сканирование завершено: {result.AbsolutePath} · {result.TotalSizeText}", StatusSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            ScanWasCancelled = true;
            _logger.ScanCancelled(path);
        }
        catch (Exception exception)
        {
            var cause = exception.Unwrap();
            _logger.ScanFailed(cause, path);
            _notifier.Notify($"Ошибка сканирования: {cause.Message}", StatusSeverity.Error);
            _dialogs.Error("Ошибка сканирования", cause.Message);
        }
        finally
        {
            Progress.Finish();

            IsScanning = false;
            StatusCaption = null;

            _cts?.Dispose();
            _cts = null;
        }
    }
}
