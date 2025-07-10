using SpaceSnoop.Extensions;
using SpaceSnoop.Services;
using SpaceSnoop.Services.Sync;
using SpaceSnoop.Services.Sync.Resolvers;

namespace SpaceSnoop;

public partial class SyncForm : Form
{
    private readonly SyncSettings _syncSettings;
    private readonly WorkerService _sourceWorkerService;
    private readonly WorkerService _targetWorkerService;
    private SyncService? _syncService;
    private CancellationTokenSource? _cancellationTokenSource;
    private DirectorySpace? _sourceDirectorySpace;
    private DirectorySpace? _targetDirectorySpace;
    private string? _selectedSourcePath;
    private string? _selectedTargetPath;
    private CancellationTokenSource? _sourceWorkerCancellationTokenSource;
    private CancellationTokenSource? _targetWorkerCancellationTokenSource;

    public SyncForm(WorkerService sourceWorkerService, WorkerService targetWorkerService)
    {
        _sourceWorkerService = sourceWorkerService;
        _targetWorkerService = targetWorkerService;

        InitializeComponent();
        _syncSettings = new();
        InitializeForm();
        InitializeWorkers();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);

        CleanupSyncResources();

        _sourceWorkerService.WorkCompleted -= OnSourceWorkCompleted;
        _targetWorkerService.WorkCompleted -= OnTargetWorkCompleted;

        _sourceWorkerCancellationTokenSource?.Cancel();
        _targetWorkerCancellationTokenSource?.Cancel();

        _sourceWorkerCancellationTokenSource?.Dispose();
        _targetWorkerCancellationTokenSource?.Dispose();
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object? sender, TreeViewCancelEventArgs args)
    {
        var parent = args.Node;

        if (parent == null || parent.Nodes.Count <= 0)
        {
            return;
        }

        foreach (TreeNode node in parent.Nodes)
        {
            if (node.Tag is not DirectorySpace diskSpace)
            {
                continue;
            }

            if (node.Nodes.Count > 0)
            {
                break;
            }

            node.FillParentNode(diskSpace);
        }
    }

    private void SourceDirectoryComboBox_TextChanged(object? sender, EventArgs args)
    {
        var path = sourceDirectoryComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _selectedSourcePath = path;
        ValidateDirectories();
    }

    private void TargetDirectoryComboBox_TextChanged(object? sender, EventArgs e)
    {
        var path = targetDirectoryComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        _selectedTargetPath = path;
        ValidateDirectories();
    }

    private void SourceDirectoryBrowseButton_Click(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog();

        folderDialog.Description = "Выберите исходную директорию для синхронизации";
        folderDialog.UseDescriptionForTitle = true;
        folderDialog.ShowNewFolderButton = false;
        folderDialog.SelectedPath = _selectedSourcePath ?? string.Empty;

        if (folderDialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
        {
            return;
        }

        var selectedPath = folderDialog.SelectedPath;
        var index = sourceDirectoryComboBox.Items.IndexOf(selectedPath);

        sourceDirectoryComboBox.SelectedIndex = index == -1
            ? sourceDirectoryComboBox.Items.Add(selectedPath)
            : index;

        _selectedSourcePath = selectedPath;
        AddStatusMessage($"Выбрана исходная директория: {selectedPath}");
        ValidateDirectories();
        SourceDirectoryScanButton_Click(null, EventArgs.Empty);
    }

    private void TargetDirectoryBrowseButton_Click(object? sender, EventArgs e)
    {
        using var folderDialog = new FolderBrowserDialog();

        folderDialog.Description = "Выберите целевую директорию для синхронизации";
        folderDialog.UseDescriptionForTitle = true;
        folderDialog.ShowNewFolderButton = true;
        folderDialog.SelectedPath = _selectedTargetPath ?? string.Empty;

        if (folderDialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(folderDialog.SelectedPath))
        {
            return;
        }

        var selectedPath = folderDialog.SelectedPath;
        var index = targetDirectoryComboBox.Items.IndexOf(selectedPath);

        targetDirectoryComboBox.SelectedIndex = index == -1
            ? targetDirectoryComboBox.Items.Add(selectedPath)
            : index;

        _selectedTargetPath = selectedPath;
        AddStatusMessage($"Выбрана целевая директория: {selectedPath}");
        ValidateDirectories();
        TargetDirectoryScanButton_Click(null, EventArgs.Empty);
    }

    private void SourceDirectoryScanButton_Click(object? sender, EventArgs e)
    {
        var path = sourceDirectoryComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            AddStatusMessage("Выберите исходную директорию для сканирования.");
            return;
        }

        if (Directory.Exists(path))
        {
            StartSourceWorker(path);
            return;
        }

        AddStatusMessage($"Директория не существует: {path}");
    }

    private void TargetDirectoryScanButton_Click(object? sender, EventArgs e)
    {
        var path = targetDirectoryComboBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            AddStatusMessage("Выберите целевую директорию для сканирования.");
            return;
        }

        if (Directory.Exists(path))
        {
            StartTargetWorker(path);
            return;
        }

        AddStatusMessage($"Директория не существует: {path}");
    }

    private void OnSourceWorkCompleted(object? sender, WorkerService.Response? response)
    {
        sourceDirectoryScanButton.Enabled = true;

        if (response != null)
        {
            var (directorySpace, elapsed, error) = response;

            if (string.IsNullOrEmpty(error) == false)
            {
                AddStatusMessage($"Ошибка сканирования исходной директории: {error}");
                return;
            }

            if (directorySpace == null)
            {
                return;
            }

            _sourceDirectorySpace = directorySpace;

            sourceDirectoryTreeView.Nodes
                .AddSpaceNode(directorySpace)
                .FillParentNode(directorySpace);

            AddStatusMessage($"Сканирование исходной директории завершено за {elapsed.TotalSeconds:F2} с");
            AddStatusMessage($"Найдено файлов: {directorySpace.TotalFileCount:N0}, директорий: {directorySpace.TotalDirectoryCount:N0}");

            ValidateDirectories();
        }
        else
        {
            AddStatusMessage("Неожиданный null-ответ при сканировании исходной директории");
        }
    }

    private void OnTargetWorkCompleted(object? sender, WorkerService.Response? response)
    {
        targetDirectoryScanButton.Enabled = true;

        if (response != null)
        {
            var (directorySpace, elapsed, error) = response;

            if (string.IsNullOrEmpty(error) == false)
            {
                AddStatusMessage($"Ошибка сканирования целевой директории: {error}");
                return;
            }

            if (directorySpace == null)
            {
                return;
            }

            _targetDirectorySpace = directorySpace;

            targetDirectoryTreeView.Nodes
                .AddSpaceNode(directorySpace)
                .FillParentNode(directorySpace);

            AddStatusMessage($"Сканирование целевой директории завершено за {elapsed.TotalSeconds:F2} с");
            AddStatusMessage($"Найдено файлов: {directorySpace.TotalFileCount:N0}, директорий: {directorySpace.TotalDirectoryCount:N0}");

            ValidateDirectories();
        }
        else
        {
            AddStatusMessage("Неожиданный null-ответ при сканировании целевой директории");
        }
    }

    private async void StartSyncButton_Click(object? sender, EventArgs e)
    {
        try
        {
            await StartSynchronizationAsync();
        }
        catch (Exception exception)
        {
            AddStatusMessage($"Ошибка при запуске синхронизации: {exception.Message}");
            UpdateSyncButtonStates(true);
        }
    }

    private void StopSyncButton_Click(object? sender, EventArgs e)
    {
        StopSynchronization();
    }

    private void OnSyncProgressChanged(object? sender, SyncProgressEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => UpdateProgress(e));
        }
        else
        {
            UpdateProgress(e);
        }
    }

    private void OnSyncCompleted(object? sender, SyncResult result)
    {
        if (InvokeRequired)
        {
            Invoke(() => HandleSyncCompletion(result));
        }
        else
        {
            HandleSyncCompletion(result);
        }
    }

    private void OnConflictResolutionRequested(object? sender, SyncConflictEventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(() => HandleConflictResolution(e));
        }
        else
        {
            HandleConflictResolution(e);
        }
    }

    private void InitializeForm()
    {
        UpdateSyncButtonStates(false);
        InitializeComboBoxes();
        AddStatusMessage("Готов к синхронизации. Выберите исходную и целевую директории.");
    }

    private void InitializeComboBoxes()
    {
        FillDrives();
    }

    private void FillDrives()
    {
        // sourceDirectoryComboBox.Items.Add(@"C:\Downloads\chem");
        // targetDirectoryComboBox.Items.Add(@"C:\Downloads\Test");

        var drives = DriveInfo.GetDrives();

        foreach (var drive in drives)
        {
            if (drive is not { IsReady: true, DriveType: DriveType.Fixed })
            {
                continue;
            }

            sourceDirectoryComboBox.Items.Add(drive.Name);
            targetDirectoryComboBox.Items.Add(drive.Name);
        }

        if (sourceDirectoryComboBox.Items.Count == 0)
        {
            return;
        }

        sourceDirectoryComboBox.SelectedIndex = 0;
        targetDirectoryComboBox.SelectedIndex = 0;
    }

    private void InitializeWorkers()
    {
        _sourceWorkerService.WorkCompleted += OnSourceWorkCompleted;
        _targetWorkerService.WorkCompleted += OnTargetWorkCompleted;
    }

    private void StartSourceWorker(string path)
    {
        _sourceWorkerCancellationTokenSource = new();
        sourceDirectoryScanButton.Enabled = false;
        sourceDirectoryTreeView.Nodes.Clear();

        AddStatusMessage($"Начинаем сканирование исходной директории: {path}");
        _sourceWorkerService.StartWorker(path, false, _sourceWorkerCancellationTokenSource.Token);
    }

    private void StartTargetWorker(string path)
    {
        _targetWorkerCancellationTokenSource = new();
        targetDirectoryScanButton.Enabled = false;
        targetDirectoryTreeView.Nodes.Clear();

        AddStatusMessage($"Начинаем сканирование целевой директории: {path}");
        _targetWorkerService.StartWorker(path, false, _targetWorkerCancellationTokenSource.Token);
    }

    private void ValidateDirectories()
    {
        var isSourcePathProvided = string.IsNullOrEmpty(_selectedSourcePath) == false;
        var isSourceDirectoryValid = isSourcePathProvided && Directory.Exists(_selectedSourcePath);
        var isTargetDirectoryProvided = string.IsNullOrEmpty(_selectedTargetPath) == false;
        var areDirectoriesDifferent = string.Equals(_selectedSourcePath, _selectedTargetPath, StringComparison.OrdinalIgnoreCase) == false;
        var areDirectoriesScanned = _sourceDirectorySpace != null && _targetDirectorySpace != null;

        var canStartSynchronization = isSourceDirectoryValid
                                      && isTargetDirectoryProvided
                                      && areDirectoriesDifferent
                                      && areDirectoriesScanned;

        UpdateSyncButtonStates(canStartSynchronization);

        if (isSourcePathProvided && isSourceDirectoryValid == false)
        {
            AddStatusMessage("Ошибка: Исходная директория не существует.");
        }
        else if (isSourceDirectoryValid && isTargetDirectoryProvided && areDirectoriesDifferent == false)
        {
            AddStatusMessage("Ошибка: Исходная и целевая директории не могут быть одинаковыми.");
        }
        else if (isSourceDirectoryValid && isTargetDirectoryProvided && areDirectoriesDifferent && areDirectoriesScanned == false)
        {
            AddStatusMessage("Директории выбраны корректно. Нажмите 'Сканировать' для анализа директорий перед синхронизацией.");
        }
        else if (canStartSynchronization)
        {
            AddStatusMessage("Директории просканированы и готовы к синхронизации.");
        }
    }

    private void UpdateSyncButtonStates(bool canStartSync)
    {
        startSyncButton.Enabled = canStartSync && _syncService == null;
        stopSyncButton.Enabled = _syncService != null;
    }

    private void AddStatusMessage(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var formattedMessage = $"[{timestamp}] {message}";

        if (statusTextBox.InvokeRequired)
        {
            statusTextBox.Invoke(() => AppendStatusMessage(formattedMessage));
        }
        else
        {
            AppendStatusMessage(formattedMessage);
        }
    }

    private void AppendStatusMessage(string formattedMessage)
    {
        if (statusTextBox.Text.Length > 0)
        {
            statusTextBox.AppendText(Environment.NewLine);
        }

        statusTextBox.AppendText(formattedMessage);
        statusTextBox.SelectionStart = statusTextBox.Text.Length;
        statusTextBox.ScrollToCaret();
    }

    private async Task StartSynchronizationAsync()
    {
        if (_sourceDirectorySpace == null || _targetDirectorySpace == null)
        {
            AddStatusMessage("Ошибка: Необходимо сначала просканировать исходную и целевую директории.");
            UpdateSyncButtonStates(true);
            return;
        }

        if (string.IsNullOrEmpty(_selectedSourcePath) || string.IsNullOrEmpty(_selectedTargetPath))
        {
            AddStatusMessage("Ошибка: Не выбраны директории для синхронизации.");
            UpdateSyncButtonStates(true);
            return;
        }

        AddStatusMessage("Подготовка к синхронизации...");
        UpdateSyncButtonStates(false);

        try
        {
            AddStatusMessage($"Исходная директория: {_sourceDirectorySpace.AbsolutePath}");
            AddStatusMessage($"Целевая директория: {_targetDirectorySpace.AbsolutePath}");
            AddStatusMessage($"Файлов в исходной директории: {_sourceDirectorySpace.TotalFileCount:N0}");
            AddStatusMessage($"Файлов в целевой директории: {_targetDirectorySpace.TotalFileCount:N0}");

            _syncService = SyncServiceFactory.Create(ConflictResolutionStrategy.AskUser, HandleConflictAsync);
            _cancellationTokenSource = new();

            _syncService.ProgressChanged += OnSyncProgressChanged;
            _syncService.SyncCompleted += OnSyncCompleted;
            _syncService.ConflictResolutionRequested += OnConflictResolutionRequested;

            AddStatusMessage("Начинаем синхронизацию...");

            await _syncService.SyncAsync(_sourceDirectorySpace, _targetDirectorySpace, _syncSettings, _cancellationTokenSource.Token);
        }
        catch (OperationCanceledException)
        {
            AddStatusMessage("Синхронизация была отменена пользователем.");
        }
        catch (Exception exception)
        {
            AddStatusMessage($"Ошибка синхронизации: {exception.Message}\n{exception.StackTrace}");
        }
        finally
        {
            CleanupSyncResources();
        }
    }

    private void StopSynchronization()
    {
        if (_cancellationTokenSource is not { Token.CanBeCanceled: true })
        {
            return;
        }

        _cancellationTokenSource.Cancel();
        AddStatusMessage("Запрошена остановка синхронизации...");
    }

    private void CleanupSyncResources()
    {
        if (_syncService != null)
        {
            _syncService.ProgressChanged -= OnSyncProgressChanged;
            _syncService.SyncCompleted -= OnSyncCompleted;
            _syncService.ConflictResolutionRequested -= OnConflictResolutionRequested;
            _syncService = null;
        }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;

        UpdateSyncButtonStates(true);
        progressBar.Value = 0;
    }

    private void UpdateProgress(SyncProgressEventArgs e)
    {
        var operationProgress = (int)Math.Round(e.OperationProgress);
        progressBar.Value = Math.Min(operationProgress, 100);

        var progressText = $"Прогресс: {e.CompletedOperations}/{e.TotalOperations} операций ({operationProgress}%)";

        if (e.CurrentOperation != null)
        {
            progressText += $" - {e.CurrentOperation.OperationType}: {Path.GetFileName(e.CurrentOperation.SourcePath)}";
        }

        progressLabel.Text = progressText;
    }

    private void HandleSyncCompletion(SyncResult result)
    {
        AddStatusMessage($"Синхронизация завершена: {result}");

        if (result.FailedOperations > 0)
        {
            AddStatusMessage($"Операций с ошибками: {result.FailedOperations}");

            foreach (var (operation, errorMessage) in result.FailedOperationDetails)
            {
                AddStatusMessage($"  - {operation.SourcePath}: {errorMessage}");
            }
        }

        CleanupSyncResources();

        SourceDirectoryScanButton_Click(null, EventArgs.Empty);
        TargetDirectoryScanButton_Click(null, EventArgs.Empty);
    }

    private void HandleConflictResolution(SyncConflictEventArgs e)
    {
        var conflictMessage = $"Конфликт: {e.ConflictReason}\nФайл: {e.Operation.SourcePath}";

        if (e is { SourceInfo: not null, TargetInfo: not null })
        {
            conflictMessage += $"\nИсходный файл: {e.SourceInfo.LastWriteTime}, размер: {e.SourceInfo.Length} байт";
            conflictMessage += $"\nЦелевой файл: {e.TargetInfo.LastWriteTime}, размер: {e.TargetInfo.Length} байт";
        }

        conflictMessage += "\n\nВыберите действие:";

        var result = MessageBox.Show(conflictMessage,
            "Конфликт синхронизации",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        e.Resolution = result switch
        {
            DialogResult.Yes => ConflictResolutionStrategy.SourceWins,
            DialogResult.No => ConflictResolutionStrategy.TargetWins,
            _ => ConflictResolutionStrategy.Skip,
        };

        AddStatusMessage($"Конфликт разрешен: {e.Resolution}");
    }

    private async Task<ConflictResolutionStrategy?> HandleConflictAsync(SyncConflictEventArgs conflictArgs)
    {
        var taskCompletionSource = new TaskCompletionSource<ConflictResolutionStrategy?>();

        if (InvokeRequired)
        {
            await InvokeAsync(() =>
            {
                HandleConflictResolution(conflictArgs);
                taskCompletionSource.SetResult(conflictArgs.Resolution);
            });
        }
        else
        {
            HandleConflictResolution(conflictArgs);
            taskCompletionSource.SetResult(conflictArgs.Resolution);
        }

        return await taskCompletionSource.Task;
    }
}
