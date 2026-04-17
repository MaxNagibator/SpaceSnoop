using KeepShell.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class ScanViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private static readonly TimeSpan ProgressPollInterval = TimeSpan.FromMilliseconds(120);

    private readonly DiskSpaceCalculator _calculator;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly OperationPreferences _operations;
    private readonly ScanNodeFactory _nodeFactory;
    private readonly DeleteProgressDialogFactory _deleteDialogFactory;
    private readonly ILogger<ScanViewModel> _logger;
    private readonly DispatcherTimer _progressTimer;

    private readonly ScanSortState _sortState = new();

    private CancellationTokenSource? _cts;
    private bool _suppressPersist;

    private ScanProgress? _progress;
    private Stopwatch? _scanStopwatch;
    private double? _progressFraction;
    private long? _estimatedTotalBytes;
    private long _rootTotalSize;

    [ObservableProperty]
    private string _selectedDrive = string.Empty;

    [ObservableProperty]
    private bool _useMultithreading = AppDefaults.ScanMultithreadingDefault;

    [ObservableProperty]
    private int _maxParallelism = Environment.ProcessorCount;

    [ObservableProperty]
    private double _intensity = AppDefaults.IntensityDefault;

    [ObservableProperty]
    private ScanSortOption _selectedSortOption = null!;

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
    private bool _hasResult;

    [ObservableProperty]
    private string _resultPath = string.Empty;

    [ObservableProperty]
    private string _resultSizeText = "—";

    [ObservableProperty]
    private string _resultFileCountText = "—";

    [ObservableProperty]
    private string _resultDirCountText = "—";

    [ObservableProperty]
    private string _resultElapsedText = "—";

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
    private string _scanThroughputText = "—";

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

    public ScanViewModel(
        DiskSpaceCalculator calculator,
        IDialogService dialogs,
        ISettingsStore settings,
        OperationPreferences operations,
        ScanInspectorViewModel inspector,
        ScanNodeFactory nodeFactory,
        DeleteProgressDialogFactory deleteDialogFactory,
        ILogger<ScanViewModel> logger)
    {
        _calculator = calculator;
        _dialogs = dialogs;
        _settings = settings;
        _operations = operations;
        _nodeFactory = nodeFactory;
        _deleteDialogFactory = deleteDialogFactory;
        _logger = logger;

        Inspector = inspector;

        _progressTimer = new() { Interval = ProgressPollInterval };
        _progressTimer.Tick += OnProgressTick;

        foreach (var drive in DriveInfo.GetDrives())
        {
            Drives.Add(drive.Name);
        }

        LoadSettings();
    }

    public ObservableCollection<string> Drives { get; } = [];

    public ObservableCollection<ScanNodeViewModel> Roots { get; } = [];

    public ScanInspectorViewModel Inspector { get; }

    public ObservableCollection<ScanSortOption> SortOptions { get; } =
    [
        new("По имени", ScanSortField.Name),
        new("По размеру", ScanSortField.Size),
        new("По дате создания", ScanSortField.CreationDate),
        new("По времени последнего доступа", ScanSortField.LastAccessTime),
    ];

    public int ProcessorCount { get; } = Environment.ProcessorCount;

    public string ParallelismHint =>
        $"Сколько каталогов обходить одновременно. "
        + $"Максимум и значение по умолчанию — число логических процессоров ({ProcessorCount}). "
        + $"Меньше потоков — ниже нагрузка и расход памяти.";

    public string PageTitle => "Сканирование";

    public string PageDescription => "Анализ занятого места по дискам и каталогам.";

    public bool IsBusy => IsScanning;

    public bool IsIndeterminate => !_progressFraction.HasValue;

    public double ProgressValue => _progressFraction ?? 0;

    public double ProgressMax => 1;

    public ICommand CancelCommand => StopCommand;

    private void OnProgressTick(object? sender, EventArgs e)
    {
        UpdateLiveProgress();
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

    private static string NormalizePath(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    partial void OnUseMultithreadingChanged(bool value)
    {
        Persist(() => _settings.SetBool(SettingsKeys.ScanMultithreading, value));
    }

    partial void OnMaxParallelismChanged(int value)
    {
        Persist(() => _settings.SetInt(SettingsKeys.ScanParallelism, value));
    }

    partial void OnIntensityChanged(double value)
    {
        Inspector.Intensity = value;
        Persist(() => _settings.SetDouble(SettingsKeys.ScanIntensity, value));
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
        if (value is null)
        {
            Inspector.Clear();
        }
        else
        {
            Inspector.Show(value, _rootTotalSize);
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

        UseMultithreading = _settings.GetBool(SettingsKeys.ScanMultithreading, AppDefaults.ScanMultithreadingDefault);
        MaxParallelism = Math.Clamp(_settings.GetInt(SettingsKeys.ScanParallelism, ProcessorCount), 1, ProcessorCount);
        Intensity = _settings.GetDouble(SettingsKeys.ScanIntensity, AppDefaults.IntensityDefault);

        var sortField = _settings.GetEnum(SettingsKeys.ScanSortMode, AppDefaults.ScanSortModeDefault);
        var invertSort = _settings.GetBool(SettingsKeys.ScanSortInvert, AppDefaults.ScanSortInvertDefault);
        _sortState.Field = sortField;
        _sortState.Invert = invertSort;
        InvertSort = invertSort;
        SelectedSortOption = SortOptions.FirstOrDefault(option => option.Field == sortField)
                             ?? SortOptions.First(option => option.Field == AppDefaults.ScanSortModeDefault);

        var lastDrive = _settings.GetStringValue(SettingsKeys.ScanLastDrive);

        if (!string.IsNullOrWhiteSpace(lastDrive))
        {
            if (!Drives.Contains(lastDrive))
            {
                Drives.Add(lastDrive);
            }

            SelectedDrive = lastDrive;
        }
        else
        {
            SelectedDrive = Drives.Count > 0 ? Drives[0] : string.Empty;
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

        if (!Drives.Contains(path))
        {
            Drives.Add(path);
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

    private async Task ScanAsync(string path)
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

        _cts = new();
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

        _logger.ScanStarted(path, UseMultithreading, MaxParallelism);

        try
        {
            var result = await Task.Run(() => UseMultithreading
                    ? _calculator.CalculateMultithreaded(directory, MaxParallelism, progress, token)
                    : _calculator.Calculate(directory, progress, token),
                token);

            _scanStopwatch.Stop();

            RemoveRoot(path);

            _rootTotalSize = result.TotalSize;

            var node = _nodeFactory.Create(result, result.TotalSize, result.TotalSize, _sortState);
            node.IsExpanded = true;

            Roots.Insert(0, node);

            ResultPath = result.AbsolutePath;
            ResultSizeText = result.TotalSizeText;
            ResultFileCountText = result.TotalFileCount.ToString("N0");
            ResultDirCountText = result.TotalDirectoryCount.ToString("N0");
            ResultElapsedText = FormatElapsed(_scanStopwatch.Elapsed);
            HasResult = true;

            _logger.ScanCompleted(result.AbsolutePath,
                result.TotalSizeText,
                result.TotalFileCount,
                result.TotalDirectoryCount,
                (long)_scanStopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
            ScanWasCancelled = true;
            _logger.ScanCancelled(path);
        }
        catch (Exception exception)
        {
            _logger.ScanFailed(exception, path);
            _dialogs.Error("Ошибка сканирования", exception.Message);
        }
        finally
        {
            _progressTimer.Stop();
            _scanStopwatch?.Stop();
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
        ScanThroughputText = "—";
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

        var seconds = elapsed.TotalSeconds;

        if (seconds > 0.25 && snapshot.FilesScanned > 0)
        {
            var filesPerSecond = snapshot.FilesScanned / seconds;
            var bytesPerSecond = (long)(snapshot.BytesScanned / seconds);
            ScanThroughputText = $"{filesPerSecond:N0} файл/с · {SizeFormatter.Format(bytesPerSecond)}/с";
        }

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

        OnPropertyChanged(nameof(IsIndeterminate));
        OnPropertyChanged(nameof(ProgressValue));
    }

    [RelayCommand]
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
            dialog.RequestStop();
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
            _rootTotalSize = resultDir.TotalSize;
            ResultSizeText = resultDir.TotalSizeText;
            ResultFileCountText = resultDir.TotalFileCount.ToString("N0");
            ResultDirCountText = resultDir.TotalDirectoryCount.ToString("N0");
        }
        else if (rootsToRemove.Any(r => string.Equals(NormalizePath(r.AbsolutePath), NormalizePath(ResultPath), StringComparison.OrdinalIgnoreCase)))
        {
            HasResult = false;
        }
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
