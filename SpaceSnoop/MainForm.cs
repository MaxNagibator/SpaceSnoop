using System.ComponentModel;

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

        DirectoryInfo directory = new(disk);
        DirectorySpace data = _diskSpaceCalculator.Calculate(directory);

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
}
