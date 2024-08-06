using System.ComponentModel;
using System.Diagnostics;
using SpaceSnoop.Extensions;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly DiskSpaceCalculator _diskSpaceCalculator;
    private bool _isSorting;

    public MainForm()
    {
        _diskSpaceCalculator = new DiskSpaceCalculator();
        InitializeComponent();

        _backgroundWorker = new BackgroundWorker();
        InitializeWorker();

        _directoriesTreeView.ShowNodeToolTips = true;
    }

    private void OnFormLoaded(object sender, EventArgs args)
    {
        SorterMode[] sorterModes =
        [
            SorterMode.ByName,
            SorterMode.BySize,
            SorterMode.ByDate,
            SorterMode.ByLastAccessTime
        ];

        _sortModeComboBox.Items.AddRange(sorterModes);

        DriveInfo[] hardDisk = DriveInfo.GetDrives();

        foreach (DriveInfo disk in hardDisk)
        {
            _hardDiskComboBox.Items.Add(disk.Name);
        }

        _sortModeComboBox.SelectedIndexChanged += OnSortModeChanged;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;
        _sortModeComboBox.SelectedIndexChanged -= OnSortModeChanged;

        base.OnFormClosing(e);
    }

    private void OnStartButtonClicked(object sender, EventArgs args)
    {
        string? disk = _hardDiskComboBox.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Marquee);

        RemoveDiskNode(disk);

        _backgroundWorker.RunWorkerAsync(disk);
    }

    private void OnStopButtonClicked(object sender, EventArgs args)
    {
        _backgroundWorker.CancelAsync();
    }

    private void OnDoWork(object? sender, DoWorkEventArgs args)
    {
        string? disk = args.Argument?.ToString();

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        DirectoryInfo directory = new(disk);

        DirectorySpace data = _useMultithreadingCheckBox.Checked
            ? _diskSpaceCalculator.CalculateMultithreaded(directory)
            : _diskSpaceCalculator.Calculate(directory);

        stopwatch.Stop();
        Log($@"Расчет для каталога {directory.FullName} завершен через {stopwatch.ElapsedMilliseconds:F2} мс.");

        args.Result = data;
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        if (args.Error != null)
        {
            return;
        }

        if (args.Cancelled)
        {
            return;
        }

        if (args.Result is not DirectorySpace data)
        {
            return;
        }

        _directoriesTreeView.Nodes.AddDirectoryNode(data).AddDirectoryNodes(data);
        SortNodes();

        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Blocks);
    }

    private void OnSortModeChanged(object? sender, EventArgs e)
    {
        SortNodes();
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object sender, TreeViewCancelEventArgs args)
    {
        if (args.Node == null || _isSorting)
        {
            return;
        }

        foreach (TreeNode node in args.Node.Nodes)
        {
            if (node.Tag is DirectorySpace diskSpace)
            {
                node.AddDirectoryNodes(diskSpace);
            }
        }
    }

    private void OnNodeMouseClicked(object sender, TreeNodeMouseClickEventArgs args)
    {
        if (args.Button != MouseButtons.Right)
        {
            return;
        }

        if (args.Node.Tag is DirectorySpace selectedDirectory)
        {
            Process.Start("explorer.exe", selectedDirectory.Path);
        }
    }

    private void OnInvertSortCheckBoxChanged(object sender, EventArgs e)
    {
        // TODO Крайне неудачное решение
        NodeSorterBase.SetInversion(_invertSortCheckBox.Checked);
        SortNodes();
    }

    private void InitializeWorker()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    private void RemoveDiskNode(string disk)
    {
        for (int i = 0; i < _directoriesTreeView.Nodes.Count; i++)
        {
            TreeNode node = _directoriesTreeView.Nodes[i];

            if (node.Text.StartsWith(disk, StringComparison.InvariantCultureIgnoreCase) == false)
            {
                continue;
            }

            _directoriesTreeView.Nodes.Remove(node);
            break;
        }
    }

    private void SortNodes()
    {
        if (_sortModeComboBox.SelectedItem is not SorterMode selectedSortMode)
        {
            return;
        }

        _isSorting = true;
        _directoriesTreeView.TreeViewNodeSorter = selectedSortMode.Comparer;
        _directoriesTreeView.Sort();
        _isSorting = false;
    }

    private void Log(string text)
    {
        _uiLogsTextBox.Invoke(() => _uiLogsTextBox.Text += $@"{text}{Environment.NewLine}");
    }
}
