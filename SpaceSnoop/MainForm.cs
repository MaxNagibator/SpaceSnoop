using Microsoft.VisualBasic.FileIO;
using SpaceSnoop.Extensions;
using SpaceSnoop.Services;
using System.Diagnostics;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private static readonly Color InfoColor = Color.Black;
    private static readonly Color ErrorColor = Color.Red;

    private readonly ColorService _colorService;
    private readonly WorkerService _workerService;
    private readonly SortService _sortService;

    private CancellationTokenSource? _cancellationTokenSource;

    public MainForm(
        WorkerService workerService,
        ColorService colorService,
        SortService sortService
    )
    {
        _workerService = workerService;
        _colorService = colorService;
        _sortService = sortService;

        InitializeComponent();

        _directoriesTreeView.ShowNodeToolTips = true;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData != Keys.Escape)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        StopWorker();
        return true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        PerformDelete();

        FinalizeWorker();
        FinalizeSorting();
        FinalizeColorService();

        base.OnFormClosing(e);
    }

    private void OnFormLoaded(object sender, EventArgs args)
    {
        InitializeWorker();
        InitializeSorting();
        InitializeColorService();

        FillDrives();

        SetDefaultSettings();
    }

    private void OnStartButtonClicked(object sender, EventArgs args)
    {
        var disk = _hardDiskComboBox.SelectedItem?.ToString() ?? _hardDiskComboBox.Text;

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        StartScanning(disk);
    }

    private void OnStopButtonClicked(object sender, EventArgs args)
    {
        StopWorker();
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object sender, TreeViewCancelEventArgs args)
    {
        var parent = args.Node;

        if (parent == null || _sortService.IsSorting || parent.Nodes.Count <= 0)
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
            _colorService.UpdateAssignedNodesColor(node);
        }
    }

    private void OnNodeMouseClicked(object sender, TreeNodeMouseClickEventArgs args)
    {
        if (args.Button != MouseButtons.Right)
        {
            return;
        }

        if (args.Node?.Tag is not SpaceBase selectedSpace)
        {
            return;
        }

        if ((ModifierKeys & Keys.Control) == Keys.Control)
        {
            selectedSpace.SwapDelete();
            _colorService.UpdateNodesColor(_directoriesTreeView.Nodes);
        }
        else
        {
            Process.Start(new ProcessStartInfo(selectedSpace.AbsolutePath) { UseShellExecute = true });
        }
    }

    private void OnChooseDirectoryClicked(object sender, EventArgs e)
    {
        using var folderBrowserDialog = new FolderBrowserDialog();

        var result = folderBrowserDialog.ShowDialog();

        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
        {
            return;
        }

        var selectedPath = folderBrowserDialog.SelectedPath;

        var index = _hardDiskComboBox.Items.IndexOf(selectedPath);

        _hardDiskComboBox.SelectedIndex = index == -1
            ? _hardDiskComboBox.Items.Add(selectedPath)
            : index;

        StartScanning(selectedPath);
    }

    private void OnSyncFormButtonClicked(object sender, EventArgs e)
    {
        string? initialPath = null;

        var selectedNode = _directoriesTreeView.SelectedNode;

        if (selectedNode?.Tag is DirectorySpace selectedDir)
        {
            initialPath = selectedDir.AbsolutePath;
        }

        using var syncForm = new SyncForm(initialPath);
        syncForm.ShowDialog(this);
    }

    private void OnIntensityChanged(object? sender, int intensity)
    {
        _intensityGroupBox.Text = $"Интенсивность: {intensity}";
        _colorService.UpdateNodesColor(_directoriesTreeView.Nodes);
    }

    private void OnWorkCompleted(object? sender, WorkerService.Response? response)
    {
        if (response != null)
        {
            var (directorySpace, elapsed, error) = response;

            if (!string.IsNullOrEmpty(error))
            {
                AppendColoredText($"[{DateTime.Now:HH:mm:ss:ffff}] Ошибка: {error}",
                    ErrorColor,
                    FontStyle.Bold);
            }

            if (directorySpace != null)
            {
                var addedParent = _directoriesTreeView.Nodes.AddSpaceNode(directorySpace).FillParentNode(directorySpace);
                _colorService.UpdateAssignedNodesColor(addedParent);
                _sortService.SortNodes();

                var text = $"""
                            [{DateTime.Now:HH:mm:ss:ffff}] Расчет завершён для:
                            {directorySpace.AbsolutePath}
                            Общее время: {elapsed.TotalSeconds:F2} с ({elapsed.Milliseconds} мс)

                            Файлов всего: {directorySpace.TotalFileCount:N0}
                            Подкаталогов всего: {directorySpace.TotalDirectoryCount:N0}
                            """;

                AppendColoredText(text, InfoColor);
            }
        }
        else
        {
            AppendColoredText($"[{DateTime.Now:HH:mm:ss:ffff}] Неожиданный null-ответ",
                ErrorColor,
                FontStyle.Italic);
        }

        _infoTextBox.AppendText(Environment.NewLine);

        StopProgressBar();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
    }

    private void StartWorker(string disk)
    {
        _cancellationTokenSource = new();
        _workerService.StartWorker(disk, _useMultithreadingCheckBox.Checked, _cancellationTokenSource.Token);
    }

    private void StopWorker()
    {
        _cancellationTokenSource?.Cancel();
    }

    private void SetDefaultSettings()
    {
        var version = Application.ProductVersion;
        var plusIndex = version.IndexOf('+', StringComparison.Ordinal);

        if (plusIndex > 0)
        {
            version = version[..plusIndex];
        }

        var title = $"SpaceSnoop v{version}";

        Text = AdministratorChecker.IsCurrentUserAdmin()
            ? $"{title} (Запущено от имени администратора)"
            : title;

        _hardDiskComboBox.SelectedIndex = 0;

        _useMultithreadingCheckBox.Checked = true;
    }

    private void FillDrives()
    {
        var hardDisk = DriveInfo.GetDrives();

        foreach (var disk in hardDisk)
        {
            _hardDiskComboBox.Items.Add(disk.Name);
        }
    }

    private void StartScanning(string disk)
    {
        StartProgressBar();
        RemovePathNode(disk);
        StartWorker(disk);
    }

    private void RemovePathNode(string path)
    {
        for (var i = 0; i < _directoriesTreeView.Nodes.Count; i++)
        {
            var node = _directoriesTreeView.Nodes[i];

            if (node.Tag is not SpaceBase space
                || !space.AbsolutePath.EndsWith(path, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            _directoriesTreeView.Nodes.Remove(node);
            break;
        }
    }

    private void StartProgressBar()
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Marquee);
    }

    private void StopProgressBar()
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Blocks);
    }

    private void InitializeColorService()
    {
        _colorService.Initialize(_intensityBar);
        _colorService.IntensityChanged += OnIntensityChanged;
    }

    private void FinalizeColorService()
    {
        _colorService.IntensityChanged -= OnIntensityChanged;
        _colorService.Dispose();
    }

    private void InitializeWorker()
    {
        _workerService.WorkCompleted += OnWorkCompleted;
    }

    private void FinalizeWorker()
    {
        StopWorker();
        _workerService.WorkCompleted -= OnWorkCompleted;
    }

    private void InitializeSorting()
    {
        _sortService.Initialize(_sortModeComboBox, _invertSortCheckBox, _directoriesTreeView);
    }

    private void FinalizeSorting()
    {
        _sortService.Dispose();
    }

    private void PerformDelete()
    {
        var toDelete = GetAllMarkedForDeletion();

        if (toDelete.Count == 0)
        {
            return;
        }

        var count = toDelete.Count;
        var totalBytes = toDelete.Sum(item => item.TotalSize);
        var totalSizeText = SizeFormatter.Format(totalBytes);
        var previewCount = 5;

        var pathsPreview = string.Join(Environment.NewLine, toDelete.Take(previewCount)
            .Select(x => x.AbsolutePath));

        if (count > previewCount)
        {
            pathsPreview += $"{Environment.NewLine}...и ещё {count - previewCount} элемент(ов)";
        }

        var text = $"""
                    В корзину будут перемещены {count} элемент(ов). 
                    Общий объём: {totalSizeText}.

                    {pathsPreview}

                    Выполнить удаление?
                    """;

        var result = MessageBox.Show(this,
            text,
            "Удаление",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        var logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deleted.txt");

        using var logWriter = new StreamWriter(logFilePath);

        foreach (var path in toDelete.Select(x => x.AbsolutePath))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    FileSystem.DeleteDirectory(path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);

                    logWriter.WriteLine(path);
                }
                else if (File.Exists(path))
                {
                    FileSystem.DeleteFile(path,
                        UIOption.OnlyErrorDialogs,
                        RecycleOption.SendToRecycleBin);

                    logWriter.WriteLine(path);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine($"Ошибка удаления {path}: {exception.Message}");
            }
        }
    }

    private List<SpaceBase> GetAllMarkedForDeletion()
    {
        var list = new List<SpaceBase>();
        TraverseNodes(_directoriesTreeView.Nodes);
        return list;

        void TraverseNodes(TreeNodeCollection nodes)
        {
            foreach (TreeNode node in nodes)
            {
                if (node.Tag is SpaceBase { State: SpaceState.Deleted } space)
                {
                    list.Add(space);

                    if (space is DirectorySpace)
                    {
                        continue;
                    }
                }

                if (node.Nodes.Count > 0)
                {
                    TraverseNodes(node.Nodes);
                }
            }
        }
    }

    private void AppendColoredText(string text, Color color, FontStyle style = FontStyle.Regular)
    {
        if (_infoTextBox.InvokeRequired)
        {
            _infoTextBox.Invoke(() => AppendColoredText(text, color, style));
            return;
        }

        _infoTextBox.SelectionStart = _infoTextBox.TextLength;
        _infoTextBox.SelectionLength = 0;

        _infoTextBox.SelectionColor = color;
        _infoTextBox.SelectionFont = new(_infoTextBox.Font, style);

        _infoTextBox.AppendText(text + Environment.NewLine);

        _infoTextBox.SelectionColor = _infoTextBox.ForeColor;
        _infoTextBox.SelectionFont = _infoTextBox.Font;

        _infoTextBox.ScrollToCaret();
    }
}
