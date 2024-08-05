using System.ComponentModel;
using System.Diagnostics;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly DiskSpaceCalculator _diskSpaceCalculator;

    public MainForm()
    {
        _diskSpaceCalculator = new DiskSpaceCalculator();
        InitializeComponent();

        _backgroundWorker = new BackgroundWorker();
        InitializeWorker();
    }

    private void InitializeWorker()
    {
        _backgroundWorker.DoWork += OnDoWork;
        _backgroundWorker.RunWorkerCompleted += OnRunWorkerCompleted;
        _backgroundWorker.WorkerSupportsCancellation = true;
    }

    private void OnFormLoaded(object sender, EventArgs args)
    {
        DriveInfo[] hardDisk = DriveInfo.GetDrives();

        foreach (DriveInfo disk in hardDisk)
        {
            _hardDiskComboBox.Items.Add(disk.Name);
        }
    }

    private void OnStartButtonClicked(object sender, EventArgs args)
    {
        string? disk = _hardDiskComboBox.SelectedItem?.ToString();

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Marquee);

        _directoriesTreeView.Nodes.Clear();
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

        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Blocks);
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object sender, TreeViewCancelEventArgs args)
    {
        if (args.Node == null)
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

    private void Log(string text)
    {
        _uiLogsTextBox.Invoke(() => _uiLogsTextBox.Text += $@"{text}{Environment.NewLine}");
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
}
