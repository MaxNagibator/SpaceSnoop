using KeepShell.Services;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private readonly ScanRunner _runner;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly ScanArchiveViewModel _archive;
    private readonly ILogger<ScanViewModel> _logger;
    private readonly ToastNotifier _notifier;
    private readonly PerformanceOperations _performance;
    private readonly IFilePicker _filePicker;
    private readonly IAppNavigator _navigator;

    private CancellationTokenSource? _cts;
    private int _scanVersion;
    private bool _suppressPersist;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    [NotifyPropertyChangedFor(nameof(StartCaption))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _selectedDrive = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(ShowTargetPicker))]
    [NotifyPropertyChangedFor(nameof(ShowScanResult))]
    [NotifyPropertyChangedFor(nameof(ShowScanningState))]
    [NotifyPropertyChangedFor(nameof(ShowStaleResult))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    [NotifyCanExecuteChangedFor(nameof(OpenPickerCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(RescanCancelledCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string? _statusCaption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStaleResult))]
    [NotifyPropertyChangedFor(nameof(StaleResultCaption))]
    private string _scanTargetPath = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeVisible))]
    [NotifyPropertyChangedFor(nameof(TreemapVisible))]
    [NotifyPropertyChangedFor(nameof(DuplicatesVisible))]
    [NotifyPropertyChangedFor(nameof(ShowTargetPicker))]
    [NotifyPropertyChangedFor(nameof(ShowScanResult))]
    [NotifyPropertyChangedFor(nameof(ShowScanningState))]
    [NotifyPropertyChangedFor(nameof(ShowStaleResult))]
    [NotifyPropertyChangedFor(nameof(StaleResultCaption))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    [NotifyCanExecuteChangedFor(nameof(OpenPickerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClosePickerCommand))]
    private bool _hasResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowTargetPicker))]
    [NotifyPropertyChangedFor(nameof(ShowScanResult))]
    [NotifyPropertyChangedFor(nameof(ShowSummary))]
    [NotifyCanExecuteChangedFor(nameof(OpenPickerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClosePickerCommand))]
    private bool _isPickerOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TreeVisible))]
    [NotifyPropertyChangedFor(nameof(TreemapVisible))]
    [NotifyPropertyChangedFor(nameof(DuplicatesVisible))]
    private int _selectedViewIndex;

    [ObservableProperty]
    private ScanNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RescanCancelledCommand))]
    private bool _scanWasCancelled;

    public DriveCatalog Drives { get; }

    public ScanMarksViewModel Marks { get; }

    public ObservableCollection<ScanNodeViewModel> Roots { get; } = [];

    public ScanProgressViewModel Progress { get; }

    public ScanSummaryViewModel Summary { get; }

    public FirstRunViewModel FirstRun { get; }

    public ScanTipsViewModel Tips { get; }

    public ScanTreemapViewModel Treemap { get; }

    public ScanInspectorViewModel Inspector { get; }

    public ScanDuplicatesViewModel Duplicates { get; }

    public ScanSortViewModel Sort { get; }

    public ScanPreferences Preferences { get; }

    public double Intensity
    {
        get => Preferences.Intensity;
        set => Preferences.Intensity = value;
    }

    public bool IsBusy => IsScanning || IsExporting;

    public int MarkedCount => Marks.MarkedCount;

    public IReadOnlyList<SegmentOption> ViewModes { get; }

    public bool DuplicatesEnabled => Preferences.DuplicatesEnabled;

    public ScanViewMode ViewMode
    {
        get => (ScanViewMode)(SelectedViewIndex + 1);
        set => SelectedViewIndex = Math.Clamp((int)value - 1, 0, ViewModes.Count - 1);
    }

    internal AdminRestartPrompt RestartPrompt { get; }

    internal DirectorySpace? CurrentRoot => Roots.Count > 0 ? Roots[0].Space as DirectorySpace : null;

    internal bool IsUpdatingSelection { get; private set; }

    public ScanViewModel(
        ScanRunner runner,
        IDialogService dialogs,
        ISettingsStore settings,
        OperationPreferences operations,
        ScanPreferences preferences,
        ScanInspectorViewModel inspector,
        ScanNodeFactory nodeFactory,
        DeleteProgressDialogFactory deleteDialogFactory,
        ArchiveProgressDialogFactory archiveDialogFactory,
        DuplicateProgressDialogFactory duplicateDialogFactory,
        ILogger<ScanViewModel> logger,
        ToastNotifier notifier,
        PerformanceOperations performance,
        IFilePicker filePicker,
        IUiDispatcher uiDispatcher,
        IAppNavigator navigator,
        IApplicationLifetime lifetime,
        ThemeSelectionViewModel theme,
        UpdatePreferences updates)
    {
        _navigator = navigator;
        _runner = runner;
        _performance = performance;
        _dialogs = dialogs;
        _settings = settings;
        _nodeFactory = nodeFactory;
        _logger = logger;
        _notifier = notifier;
        _filePicker = filePicker;

        Inspector = inspector;
        Inspector.ChildSelected += OnInspectorChildSelected;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Inspector.Intensity = Preferences.Intensity;

        Sort = new(settings, ResortRoots);

        Progress = new(performance, uiDispatcher);
        Progress.PropertyChanged += OnProgressPropertyChanged;

        RestartPrompt = new(dialogs, notifier, () => MarkedCount, () => AdminRestart.Run(lifetime));
        Summary = new(AdminElevation.IsElevated, RestartPrompt.RunAsync);
        FirstRun = new(settings, preferences, operations, theme, updates, AdminElevation.IsElevated, RestartPrompt.RunAsync);
        FirstRun.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.IsVisible))
            {
                StartCommand.NotifyCanExecuteChanged();
                BrowseCommand.NotifyCanExecuteChanged();
            }
        };

        Tips = new(settings, () => FirstRun.IsVisible && ShowTargetPicker);

        Treemap = new(Roots);
        Treemap.DrilledInto += OnTreemapDrilledInto;

        _nodeFactory.AskAgentRequested += OnAskAgentRequested;
        _nodeFactory.IgnoreRequested += OnIgnoreRequested;
        _nodeFactory.UnignoreRequested += OnUnignoreRequested;

        Drives = new(logger);

        Marks = new(
            dialogs,
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
            () => IsBusy,
            () => Drives.ReloadLabels(SelectedDrive));

        _archive = new(
            archiveDialogFactory,
            dialogs,
            nodeFactory,
            Roots,
            Treemap,
            Inspector,
            () => SelectedNode,
            Marks,
            () => Drives.ReloadLabels(SelectedDrive));

        Duplicates = new(
            dialogs,
            duplicateDialogFactory,
            settings,
            preferences,
            notifier,
            () => CurrentRoot,
            MarkForAutomation,
            () => IsBusy);

        Marks.ItemsDeleted += Duplicates.RemoveDeleted;
        Marks.ItemsDeleted += deleted =>
        {
            if (!HasIgnoredItems)
            {
                return;
            }

            var affected = _nodeFactory.Visibility.AffectedSpaces();
            _nodeFactory.Visibility.RemoveDeleted(deleted);
            RefreshVisibility(affected);
        };

        InitializeSelectionWiring();

        Roots.CollectionChanged += (_, _) =>
        {
            _nodeFactory.Visibility.RetainRoots(Roots);
            OnPropertyChanged(nameof(HasIgnoredItems));
            OnPropertyChanged(nameof(ResetIgnoredCaption));
        };

        Roots.CollectionChanged += (_, _) => OnPropertyChanged(nameof(StaleResultCaption));

        ViewModes = BuildViewModes(preferences.DuplicatesEnabled);

        LoadSettings();
        Drives.LoadDriveLabels();
    }

    internal static IReadOnlyList<SegmentOption> BuildViewModes(bool duplicates)
    {
        List<SegmentOption> modes =
        [
            new(PackIconLucideKind.FolderTree, "Дерево", "Дерево каталогов с тепловой подсветкой"),
            new(PackIconLucideKind.Map, "Карта", "Карта занятого места (treemap)"),
        ];

        if (duplicates)
        {
            modes.Add(new(PackIconLucideKind.CopyCheck, "Дубликаты", "Одинаковые файлы, найденные сличением содержимого"));
        }

        return modes;
    }

    private void OnTreemapDrilledInto(ScanNodeViewModel node)
    {
        SelectedNode = node;
    }

    private void OnInspectorChildSelected(ScanNodeViewModel node)
    {
        if (ViewMode == ScanViewMode.Treemap
            && node.ParentNode is { } parent
            && !ReferenceEquals(Treemap.TreemapRoot, parent))
        {
            Treemap.DrillIntoCommand.Execute(parent);
        }

        SelectSingle(node);
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

        _navigator.AskAgent(ChatQuestion.ForScanNode(space.AbsolutePath, node.SizeText, node.IsDirectory));
    }

    partial void OnIsScanningChanged(bool value)
    {
        Progress.IsScanning = value;
        Summary.IsScanning = value;
        Marks.DeleteMarkedCommand.NotifyCanExecuteChanged();
        Duplicates.NotifyScanStateChanged();
        ExportFullScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasResultChanged(bool value)
    {
        Summary.HasResult = value;
        Duplicates.NotifyScanStateChanged();
        ExportFullScanCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDriveChanged(string value)
    {
        Drives.Select(value);
        Persist(() => _settings.SetValue(SettingsKeys.ScanLastDrive, value));
    }

    partial void OnSelectedNodeChanged(ScanNodeViewModel? oldValue, ScanNodeViewModel? newValue)
    {
        if (IsUpdatingSelection)
        {
            oldValue?.IsPrimarySelected = false;
            newValue?.IsPrimarySelected = true;
            return;
        }

        IsUpdatingSelection = true;

        try
        {
            oldValue?.IsPrimarySelected = false;
            newValue?.IsPrimarySelected = true;
            ApplySelection(newValue is null ? [] : [newValue], newValue, true);
        }
        finally
        {
            IsUpdatingSelection = false;
        }
    }

    partial void OnSelectedViewIndexChanged(int value)
    {
        var mode = ViewMode;

        Persist(() => _settings.SetEnum(SettingsKeys.ScanView, mode));

        if (mode == ScanViewMode.Treemap && Treemap.TreemapRoot is null && Roots.Count > 0)
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

        ViewMode = _settings.GetEnum(SettingsKeys.ScanView, AppDefaults.ScanViewDefault);

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
        return !IsBusy && !_isPreparing && !FirstRun.IsVisible;
    }

    private bool CanStartTarget()
    {
        return CanStart() && HasTarget;
    }

    [RelayCommand(CanExecute = nameof(CanStartTarget))]
    private async Task StartAsync()
    {
        await ScanAfterRescanPromptAsync(SelectedDrive?.Trim() ?? string.Empty);
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
        await ScanAfterRescanPromptAsync(path);
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
}
