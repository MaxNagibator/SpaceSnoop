using KeepShell.Services;
using Microsoft.Win32;
using SpaceSnoop.Core.Export;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.ViewModels.Scan;

public sealed partial class ScanViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(120);

    private readonly DiskSpaceCalculator _calculator;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly OperationPreferences _operations;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly DeleteProgressDialogFactory _deleteDialogFactory;
    private readonly ArchiveProgressDialogFactory _archiveDialogFactory;
    private readonly ILogger<ScanViewModel> _logger;
    private readonly ToastNotifier _notifier;
    private readonly PerformanceMonitor _performance;
    private readonly DispatcherTimer _progressTimer;

    private readonly ScanSortState _sortState = new();
    private readonly List<ScanNodeViewModel> _treemapPath = [];

    private CancellationTokenSource? _cts;
    private bool _suppressPersist;
    private ScanNodeViewModel? _highlighted;

    private ScanProgress? _progress;
    private Stopwatch? _scanStopwatch;
    private double? _progressFraction;
    private long? _estimatedTotalBytes;

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
    [NotifyCanExecuteChangedFor(nameof(DeleteMarkedCommand))]
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
    private ScanNodeViewModel? _treemapRoot;

    [ObservableProperty]
    private bool _hasTreemapTiles;

    [ObservableProperty]
    private bool _treemapTruncated;

    [ObservableProperty]
    private string _treemapTruncatedText = string.Empty;

    [ObservableProperty]
    private string _resultPath = string.Empty;

    [ObservableProperty]
    private string _resultSizeText = "–";

    [ObservableProperty]
    private string _resultFileCountText = "–";

    [ObservableProperty]
    private string _resultDirCountText = "–";

    [ObservableProperty]
    private string _resultElapsedText = "–";

    [ObservableProperty]
    private string _resultRateText = "–";

    [ObservableProperty]
    private string _scanCurrentPath = string.Empty;

    [ObservableProperty]
    private string _scanDirCountText = "0";

    [ObservableProperty]
    private string _scanFileCountText = "0";

    [ObservableProperty]
    private string _scanBytesText = "0 байт";

    [ObservableProperty]
    private string _scanElapsedText = "0,0 с";

    [ObservableProperty]
    private string _scanThroughputText = "–";

    [ObservableProperty]
    private string _scanRemainingText = string.Empty;

    [ObservableProperty]
    private bool _scanHasRemaining;

    [ObservableProperty]
    private string _scanTopLevelText = string.Empty;

    [ObservableProperty]
    private bool _scanHasBranches;

    [ObservableProperty]
    private bool _scanHasDeterminateProgress;

    [ObservableProperty]
    private string _scanPercentText = string.Empty;

    [ObservableProperty]
    private ScanNodeViewModel? _selectedNode;

    [ObservableProperty]
    private bool _scanWasCancelled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMarked))]
    [NotifyCanExecuteChangedFor(nameof(DeleteMarkedCommand))]
    private int _markedCount;

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
        PerformanceMonitor performance)
    {
        _calculator = calculator;
        _performance = performance;
        _dialogs = dialogs;
        _settings = settings;
        _operations = operations;
        _nodeFactory = nodeFactory;
        _deleteDialogFactory = deleteDialogFactory;
        _archiveDialogFactory = archiveDialogFactory;
        _logger = logger;
        _notifier = notifier;

        Inspector = inspector;
        Preferences = preferences;
        Preferences.PropertyChanged += OnPreferencesChanged;
        Inspector.Intensity = Preferences.Intensity;

        _nodeFactory.MarksChanged += RecountMarked;
        _nodeFactory.ArchiveRequested += OnArchiveRequested;
        _nodeFactory.AskAgentRequested += OnAskAgentRequested;

        _progressTimer = new() { Interval = ProgressPollInterval };
        _progressTimer.Tick += OnProgressTick;

        foreach (var drive in DriveInfo.GetDrives())
        {
            AddDrive(drive.Name);
        }

        LoadSettings();
        LoadDriveLabels();
    }

    public event Action<string>? AskAgentRequested;

    public ObservableCollection<DriveItem> Drives { get; } = [];

    public ObservableCollection<ScanNodeViewModel> Roots { get; } = [];

    public RangeObservableCollection<ScanNodeViewModel> TreemapTiles { get; } = [];

    public RangeObservableCollection<TreemapCrumb> TreemapBreadcrumbs { get; } = [];

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

    public bool HasMarked => MarkedCount > 0;

    public bool TreeVisible => HasResult && !ShowTreemap;

    public bool TreemapVisible => HasResult && ShowTreemap;

    public bool IsIndeterminate => !_progressFraction.HasValue;

    public double ProgressValue => _progressFraction ?? 0;

    public double ProgressMax => 1;

    public ICommand CancelCommand => StopCommand;

    internal DirectorySpace? CurrentRoot => Roots.Count > 0 ? Roots[0].Space as DirectorySpace : null;

    internal TimeSpan LastScanElapsed { get; private set; }

    internal Func<ScanExportModel>? CaptureExportBuilder(int depth, int entryLimit)
    {
        if (CurrentRoot is not { } root)
        {
            return null;
        }

        var options = new ScanExportOptions(depth, Preferences.UseMultithreading, Preferences.MaxParallelism);
        var path = root.AbsolutePath;

        return () => ScanExport.Build(root, path, options, AppInfo.Version, entryLimit);
    }

    internal void SelectPathForAutomation(string path)
    {
        if (!HasDrive(path))
        {
            AddDrive(path);
            LoadDriveLabels();
        }

        SelectedDrive = path;
    }

    internal Task ScanFromAutomationAsync(string path, CancellationToken cancellationToken)
    {
        SelectPathForAutomation(path);

        return ScanAsync(path, cancellationToken);
    }

    internal void ApplyScanResult(string path, DirectorySpace result, TimeSpan elapsed)
    {
        RemoveRoot(path);

        var node = _nodeFactory.CreateRoot(result, _sortState);
        node.IsExpanded = true;

        Roots.Insert(0, node);
        SetTreemapRoot(node);

        ResultPath = result.AbsolutePath;
        ResultSizeText = result.TotalSizeText;
        ResultFileCountText = result.TotalFileCount.ToString("N0");
        ResultDirCountText = result.TotalDirectoryCount.ToString("N0");
        LastScanElapsed = elapsed;
        ResultElapsedText = FormatElapsed(elapsed);
        ResultRateText = PerformanceFormat.Rate(new("Сканирование", result.TotalFileCount, result.Size, elapsed)) ?? "–";
        HasResult = true;
        RecountMarked();

        _logger.ScanCompleted(result.AbsolutePath,
            result.TotalSizeText,
            result.TotalFileCount,
            result.TotalDirectoryCount,
            (long)elapsed.TotalMilliseconds);
    }

    internal SpaceBase? FindForAutomation(string path)
    {
        return ScanLookup.Find(Roots.Select(static root => root.Space).OfType<SpaceBase>(), path);
    }

    internal bool IsScanRoot(SpaceBase space)
    {
        return Roots.Any(root => ReferenceEquals(root.Space, space));
    }

    internal ArchiveRequest CreateArchiveRequest(DirectorySpace dir, bool deleteOriginal)
    {
        return _archiveDialogFactory.CreateRequest(dir, deleteOriginal, interactive: false);
    }

    internal async Task<ArchiveProgressDialogViewModel> ArchiveFromAutomationAsync(
        DirectorySpace dir,
        ArchiveRequest request,
        CancellationToken cancellationToken)
    {
        var dialog = _archiveDialogFactory.Create(request);

        using (cancellationToken.Register(dialog.RequestStop))
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }

        ApplyArchiveResult(dir, dialog);

        return dialog;
    }

    internal int MarkForAutomation(IReadOnlyList<SpaceBase> targets, bool mark)
    {
        var changed = 0;

        foreach (var space in targets)
        {
            if (mark)
            {
                if (space.IsDeleted)
                {
                    continue;
                }

                space.Delete();
            }
            else
            {
                if (!HasMarkedSelfOrChild(space))
                {
                    continue;
                }

                ScanNodeViewModel.RestoreRecursive(space);
            }

            changed++;
        }

        if (changed > 0)
        {
            foreach (var root in Roots)
            {
                root.RefreshMarks();
            }

            RecountMarked();
        }

        return changed;
    }

    internal long MarkedBytes()
    {
        return CollectMarked().Sum(static item => item.TotalSize);
    }

    private static bool HasMarkedSelfOrChild(SpaceBase space)
    {
        if (space.IsDeleted)
        {
            return true;
        }

        if (space is not DirectorySpace dir)
        {
            return false;
        }

        return dir.SubDirectories.Cast<SpaceBase>().Concat(dir.Files).Any(HasMarkedSelfOrChild);
    }

    private void RecountMarked()
    {
        MarkedCount = CollectMarked().Count;
    }

    private void OnProgressTick(object? sender, EventArgs e)
    {
        UpdateLiveProgress();
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

    private async void OnArchiveRequested(ScanNodeViewModel node)
    {
        if (node.Space is not DirectorySpace dir)
        {
            return;
        }

        var dialog = _archiveDialogFactory.Create(dir);

        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            await dialog.StopAsync();
        }

        ApplyArchiveResult(dir, dialog);
    }

    private void ApplyArchiveResult(DirectorySpace dir, ArchiveProgressDialogViewModel dialog)
    {
        if (dialog.CreatedArchivePath is { } archivePath)
        {
            AddArchiveToTree(dir, archivePath);
        }

        if (dialog.OriginalDeleted)
        {
            ApplyDeletionResult([dir]);
        }
    }

    private static long? EstimateTotalBytes(DirectoryInfo directory)
    {
        try
        {
            var full = directory.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = directory.Root.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var drive = new DriveInfo(directory.Root.FullName);

            if (!drive.IsReady)
            {
                return null;
            }

            var used = drive.TotalSize - drive.TotalFreeSpace;
            return used > 0 ? used : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        return elapsed.TotalSeconds < 60
            ? $"{elapsed.TotalSeconds:F1} с"
            : $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
    }

    private static void CollectMarked(DirectorySpace dir, List<SpaceBase> list)
    {
        foreach (var sub in dir.SubDirectories)
        {
            if (sub.IsDeleted)
            {
                list.Add(sub);
            }
            else
            {
                CollectMarked(sub, list);
            }
        }

        foreach (var file in dir.Files)
        {
            if (file.IsDeleted)
            {
                list.Add(file);
            }
        }
    }

    private static void RefreshNodeAfterDeletion(ScanNodeViewModel node, HashSet<SpaceBase> deletedSet)
    {
        if (node.Space is null)
        {
            return;
        }

        var hasDeletedChild = node.Children.Any(c => c.Space is not null && deletedSet.Contains(c.Space));

        if (hasDeletedChild)
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return;
        }

        foreach (var child in node.Children)
        {
            RefreshNodeAfterDeletion(child, deletedSet);
        }

        node.NotifyPropertiesChanged();
    }

    private static bool RefreshNodeAfterAddition(ScanNodeViewModel node, DirectorySpace parent)
    {
        if (ReferenceEquals(node.Space, parent))
        {
            node.ReloadChildren();
            node.NotifyPropertiesChanged();
            return true;
        }

        foreach (var child in node.Children)
        {
            if (RefreshNodeAfterAddition(child, parent))
            {
                node.NotifyPropertiesChanged();
                return true;
            }
        }

        node.NotifyPropertiesChanged();
        return false;
    }

    private static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void AddArchiveToTree(DirectorySpace source, string archivePath)
    {
        if (source.Parent is not DirectorySpace parent || !File.Exists(archivePath))
        {
            return;
        }

        parent.AddFile(new(archivePath));

        foreach (var root in Roots)
        {
            RefreshNodeAfterAddition(root, parent);
        }

        if (TreemapRoot is not null)
        {
            RebuildTiles();
        }

        if (SelectedNode is not null)
        {
            Inspector.Show(SelectedNode);
        }
    }

    private void AddDrive(string path)
    {
        Drives.Add(new(path));
    }

    private bool HasDrive(string path)
    {
        return Drives.Any(drive => string.Equals(drive.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private void LoadDriveLabels()
    {
        _ = Task.WhenAll(Drives.Select(drive => drive.LoadLabelAsync()))
            .ContinueWith(task => _logger.DriveSizesFailed(task.Exception!),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
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

        if (value && TreemapRoot is null && Roots.Count > 0)
        {
            SetTreemapRoot(Roots[0]);
        }
    }

    [RelayCommand]
    private void DrillInto(ScanNodeViewModel? node)
    {
        if (node is null || !node.IsDirectory || !node.HasChildren)
        {
            return;
        }

        _treemapPath.Add(node);
        TreemapRoot = node;
        RebuildBreadcrumbs();
        RebuildTiles();
        SelectedNode = node;
    }

    [RelayCommand]
    private void DrillToCrumb(ScanNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        var index = _treemapPath.IndexOf(node);

        if (index < 0)
        {
            return;
        }

        _treemapPath.RemoveRange(index + 1, _treemapPath.Count - index - 1);
        TreemapRoot = node;
        RebuildBreadcrumbs();
        RebuildTiles();
    }

    private void SetTreemapRoot(ScanNodeViewModel root)
    {
        _treemapPath.Clear();
        _treemapPath.Add(root);
        TreemapRoot = root;
        RebuildBreadcrumbs();
        RebuildTiles();
    }

    private void RebuildBreadcrumbs()
    {
        var crumbs = new TreemapCrumb[_treemapPath.Count];

        for (var i = 0; i < _treemapPath.Count; i++)
        {
            crumbs[i] = new(_treemapPath[i], i > 0);
        }

        TreemapBreadcrumbs.ReplaceAll(crumbs);
    }

    private void RebuildTiles()
    {
        if (TreemapRoot is null)
        {
            TreemapTiles.ReplaceAll([]);
            HasTreemapTiles = false;
            TreemapTruncated = false;
            TreemapTruncatedText = string.Empty;
            return;
        }

        TreemapRoot.EnsureLoaded();

        var children = TreemapRoot.Children
            .Where(static c => c.Space is not null && c.Weight > 0)
            .OrderByDescending(static c => c.Weight)
            .ToList();

        var shown = children.Take(AppDefaults.TreemapTileLimit).ToList();
        TreemapTiles.ReplaceAll(shown);
        HasTreemapTiles = shown.Count > 0;

        var hidden = children.Count - shown.Count;
        TreemapTruncated = hidden > 0;
        TreemapTruncatedText = hidden > 0
            ? $"Показаны крупнейшие {shown.Count} из {children.Count}"
            : string.Empty;
    }

    private void RefreshTreemapAfterDeletion(HashSet<SpaceBase> deletedSet)
    {
        if (_treemapPath.Count == 0)
        {
            return;
        }

        var cut = -1;

        for (var i = 0; i < _treemapPath.Count; i++)
        {
            var space = _treemapPath[i].Space;

            if (space is null || deletedSet.Contains(space))
            {
                cut = i;
                break;
            }
        }

        if (cut == 0)
        {
            var fallback = Roots.FirstOrDefault();

            if (fallback is null)
            {
                _treemapPath.Clear();
                TreemapRoot = null;
                TreemapBreadcrumbs.ReplaceAll([]);
                RebuildTiles();
            }
            else
            {
                SetTreemapRoot(fallback);
            }

            return;
        }

        if (cut > 0)
        {
            _treemapPath.RemoveRange(cut, _treemapPath.Count - cut);
            TreemapRoot = _treemapPath[^1];
            RebuildBreadcrumbs();
        }

        RebuildTiles();
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
            if (!HasDrive(lastDrive))
            {
                AddDrive(lastDrive);
            }

            SelectedDrive = lastDrive;
        }
        else
        {
            SelectedDrive = Drives.Count > 0 ? Drives[0].Path : string.Empty;
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
        var dialog = new OpenFolderDialog
        {
            Title = "Выберите каталог для сканирования",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var path = dialog.FolderName;

        if (!HasDrive(path))
        {
            AddDrive(path);
            LoadDriveLabels();
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

        _estimatedTotalBytes = EstimateTotalBytes(directory);
        _progress = new();
        _scanStopwatch = Stopwatch.StartNew();
        ResetLiveProgress(path);
        _progressTimer.Start();

        var progress = _progress;

        _logger.ScanStarted(path, Preferences.UseMultithreading, Preferences.MaxParallelism);

        try
        {
            var result = await Task.Run(() => Preferences.UseMultithreading
                    ? _calculator.CalculateMultithreaded(directory, Preferences.MaxParallelism, progress, token)
                    : _calculator.Calculate(directory, progress, token),
                token);

            _scanStopwatch.Stop();

            ApplyScanResult(path, result, _scanStopwatch.Elapsed);

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
            _progressTimer.Stop();
            _scanStopwatch?.Stop();
            _performance.ReportOperation(null);
            _progress = null;
            _progressFraction = null;
            _estimatedTotalBytes = null;

            IsScanning = false;
            StatusCaption = null;
            OnPropertyChanged(nameof(IsIndeterminate));
            OnPropertyChanged(nameof(ProgressValue));

            _cts?.Dispose();
            _cts = null;
        }
    }

    private void ResetLiveProgress(string path)
    {
        _progressFraction = null;
        ScanCurrentPath = path;
        ScanDirCountText = "0";
        ScanFileCountText = "0";
        ScanBytesText = SizeFormatter.Format(0);
        ScanElapsedText = FormatElapsed(TimeSpan.Zero);
        ScanThroughputText = "–";
        ScanRemainingText = string.Empty;
        ScanHasRemaining = false;
        ScanTopLevelText = string.Empty;
        ScanPercentText = string.Empty;
        ScanHasBranches = false;
        ScanHasDeterminateProgress = false;

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
    }

    private void UpdateLiveProgress()
    {
        if (_progress is null)
        {
            return;
        }

        var snapshot = _progress.CreateSnapshot();
        var elapsed = _scanStopwatch?.Elapsed ?? TimeSpan.Zero;

        ScanCurrentPath = string.IsNullOrEmpty(snapshot.CurrentPath) ? ScanCurrentPath : snapshot.CurrentPath;
        ScanDirCountText = snapshot.DirectoriesScanned.ToString("N0");
        ScanFileCountText = snapshot.FilesScanned.ToString("N0");
        ScanBytesText = SizeFormatter.Format(snapshot.BytesScanned);
        ScanElapsedText = FormatElapsed(elapsed);

        ScanHasBranches = snapshot.TopLevelTotal > 0;
        ScanTopLevelText = ScanHasBranches
            ? $"{snapshot.TopLevelCompleted:N0} / {snapshot.TopLevelTotal:N0}"
            : string.Empty;

        double? fraction = _estimatedTotalBytes is > 0
            ? Math.Clamp((double)snapshot.BytesScanned / _estimatedTotalBytes.Value, 0d, 1d)
            : null;

        _progressFraction = fraction;
        ScanHasDeterminateProgress = fraction.HasValue;
        ScanPercentText = fraction.HasValue ? $"{fraction.Value * 100:F0} %" : string.Empty;

        var operation = new PerformanceOperation("Сканирование",
            snapshot.FilesScanned,
            snapshot.BytesScanned,
            elapsed,
            TotalBytes: _estimatedTotalBytes,
            Basis: EtaBasis.Bytes);

        ScanThroughputText = PerformanceFormat.Rate(operation) ?? "–";
        ScanRemainingText = PerformanceFormat.Remaining(operation) ?? string.Empty;
        ScanHasRemaining = ScanRemainingText.Length > 0;

        _performance.ReportOperation(operation);

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
    }

    private bool CanDeleteMarked()
    {
        return !IsScanning && MarkedCount > 0;
    }

    [RelayCommand(CanExecute = nameof(CanDeleteMarked))]
    private async Task DeleteMarkedAsync()
    {
        var marked = CollectMarked();

        if (marked.Count == 0)
        {
            _logger.NothingMarkedForDeletion();
            _dialogs.Info("Удаление", "Нет элементов, помеченных на удаление. Пометьте их через контекстное меню узла.");
            return;
        }

        var permanent = _operations.DeleteMode == DeleteMode.Permanent;
        _logger.DeletionRequested(marked.Count, permanent);
        var dialog = _deleteDialogFactory.Create(marked, permanent);

        if (!_operations.ConfirmBeforeDelete)
        {
            dialog.StartCommand.Execute(null);
        }

        try
        {
            await _dialogs.ShowAsync(dialog);
        }
        finally
        {
            await dialog.StopAsync();
        }

        ApplyDeletionResult(dialog.DeletedItems);
    }

    private void ApplyDeletionResult(IReadOnlyList<SpaceBase> deletedItems)
    {
        if (deletedItems.Count == 0)
        {
            return;
        }

        _logger.DeletionResultApplied(deletedItems.Count);

        var deletedSet = new HashSet<SpaceBase>(ReferenceEqualityComparer.Instance);

        foreach (var item in deletedItems)
        {
            deletedSet.Add(item);
        }

        if (SelectedNode?.Space is not null && deletedSet.Contains(SelectedNode.Space))
        {
            SelectedNode = null;
        }

        foreach (var item in deletedItems)
        {
            if (item.Parent is DirectorySpace parentDir)
            {
                parentDir.Remove(item);
            }
        }

        var rootsToRemove = new List<ScanNodeViewModel>();

        foreach (var rootVm in Roots)
        {
            if (rootVm.Space is not null && deletedSet.Contains(rootVm.Space))
            {
                rootsToRemove.Add(rootVm);
            }
            else
            {
                RefreshNodeAfterDeletion(rootVm, deletedSet);
            }
        }

        foreach (var rootVm in rootsToRemove)
        {
            Roots.Remove(rootVm);
        }

        var resultRoot = Roots.FirstOrDefault(r =>
            string.Equals(NormalizePath(r.AbsolutePath), NormalizePath(ResultPath), StringComparison.OrdinalIgnoreCase));

        if (resultRoot?.Space is DirectorySpace resultDir)
        {
            ResultSizeText = resultDir.TotalSizeText;
            ResultFileCountText = resultDir.TotalFileCount.ToString("N0");
            ResultDirCountText = resultDir.TotalDirectoryCount.ToString("N0");
        }
        else if (rootsToRemove.Any(r => string.Equals(NormalizePath(r.AbsolutePath), NormalizePath(ResultPath), StringComparison.OrdinalIgnoreCase)))
        {
            HasResult = false;
        }

        RefreshTreemapAfterDeletion(deletedSet);
        RecountMarked();
    }

    private List<SpaceBase> CollectMarked()
    {
        var list = new List<SpaceBase>();

        foreach (var root in Roots)
        {
            if (root.Space is not DirectorySpace dir)
            {
                continue;
            }

            if (dir.IsDeleted)
            {
                list.Add(dir);
            }
            else
            {
                CollectMarked(dir, list);
            }
        }

        return list;
    }

    private void RemoveRoot(string path)
    {
        var normalized = NormalizePath(path);

        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            if (string.Equals(NormalizePath(Roots[i].AbsolutePath), normalized, StringComparison.OrdinalIgnoreCase))
            {
                Roots.RemoveAt(i);
            }
        }
    }
}

public sealed record TreemapCrumb(ScanNodeViewModel Node, bool ShowSeparator);
