using SpaceSnoop.Extensions;
using SpaceSnoop.Services;
using System.Diagnostics;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private readonly AdministratorChecker _administratorChecker;
    private readonly ColorService _colorService;
    private readonly WorkerService _workerService;
    private readonly SortService _sortService;

    private CancellationTokenSource? _cancellationTokenSource;

    public MainForm(
        WorkerService workerService,
        ColorService colorService,
        SortService sortService,
        AdministratorChecker administratorChecker
    )
    {
        _administratorChecker = administratorChecker;
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

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        FinalizeWorker();
        FinalizeSorting();
        FinalizeColorService();

        base.OnFormClosing(args);
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

        if (args.Node.Tag is SpaceBase selectedSpace)
        {
            Process.Start("explorer.exe", selectedSpace.AbsolutePath);
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

    private void OnIntensityChanged(object? sender, int intensity)
    {
        _intensityGroupBox.Text = $"Интенсивность: {intensity}";
        _colorService.UpdateNodesColor(_directoriesTreeView.Nodes);
    }

    private void OnWorkCompleted(object? sender, DirectorySpace? directorySpace)
    {
        if (directorySpace != null)
        {
            var addedParent = _directoriesTreeView.Nodes.AddSpaceNode(directorySpace).FillParentNode(directorySpace);
            _colorService.UpdateAssignedNodesColor(addedParent);
            _sortService.SortNodes();
        }

        StopProgressBar();
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
        Text = _administratorChecker.IsCurrentUserAdmin()
            ? "SpaceSnoop (Запущено от имени администратора)"
            : "SpaceSnoop";

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
                || space.AbsolutePath.EndsWith(path, StringComparison.CurrentCultureIgnoreCase) == false)
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
}
