using System.ComponentModel;
using System.Diagnostics;
using SpaceSnoop.Extensions;

namespace SpaceSnoop;

public partial class MainForm : Form
{
    private readonly BackgroundWorker _backgroundWorker;
    private readonly DiskSpaceCalculator _diskSpaceCalculator;
    private readonly SpaceColorCalculator _spaceColorCalculator;
    private bool _isSorting;

    public MainForm()
    {
        _spaceColorCalculator = new SpaceColorCalculator();
        _diskSpaceCalculator = new DiskSpaceCalculator(LogInformation);
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

        SetDefaultSettings();

        _sortModeComboBox.SelectedIndexChanged += OnSortModeChanged;
    }

    protected override void OnFormClosing(FormClosingEventArgs args)
    {
        _backgroundWorker.DoWork -= OnDoWork;
        _backgroundWorker.RunWorkerCompleted -= OnRunWorkerCompleted;
        _sortModeComboBox.SelectedIndexChanged -= OnSortModeChanged;

        base.OnFormClosing(args);
    }

    private void OnStartButtonClicked(object sender, EventArgs args)
    {
        string disk = _hardDiskComboBox.SelectedItem?.ToString() ?? _hardDiskComboBox.Text;

        if (string.IsNullOrWhiteSpace(disk))
        {
            return;
        }

        StartScanning(disk);
    }

    private void OnIntensityChanged(object sender, EventArgs e)
    {
        _spaceColorCalculator.Intensity = (int)_intensityNumericUpDown.Value;
        UpdateNodeColors();
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

        if (directory.Exists == false)
        {
            LogWarning($@"Расчет для каталога {directory.FullName} невозможен. Директория не найдена.");
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();

        // TODO не работает остановка
        DirectorySpace data = _useMultithreadingCheckBox.Checked
            ? _diskSpaceCalculator.CalculateMultithreaded(directory)
            : _diskSpaceCalculator.Calculate(directory);

        stopwatch.Stop();
        LogInformation($@"Расчет для каталога {directory.FullName} завершен через {stopwatch.ElapsedMilliseconds:F2} мс.");

        args.Result = data;
    }

    private void OnRunWorkerCompleted(object? sender, RunWorkerCompletedEventArgs args)
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Blocks);

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

        TreeNode addedParent = _directoriesTreeView.Nodes.AddDirectoryNode(data).AddDirectoryNodes(data);
        UpdateNodeColors(addedParent);
        SortNodes();
    }

    private void OnSortModeChanged(object? sender, EventArgs e)
    {
        SortNodes();
    }

    private void OnDirectoriesTreeViewBeforeExpanded(object sender, TreeViewCancelEventArgs args)
    {
        TreeNode? parent = args.Node;

        if (parent == null || _isSorting || parent.Nodes.Count <= 0)
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

            node.AddDirectoryNodes(diskSpace);
            UpdateNodeColors(node);
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
            Process.Start("explorer.exe", selectedSpace.Path);
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

    private void SetDefaultSettings()
    {
        Text = AdministratorChecker.Instance.IsCurrentUserAdmin()
            ? "SpaceSnoop (Запущено от имени администратора)"
            : "SpaceSnoop";

        _hardDiskComboBox.SelectedIndex = 0;
        _sortModeComboBox.SelectedIndex = 1;

        _useMultithreadingCheckBox.Checked = true;
        _invertSortCheckBox.Checked = true;

        _intensityNumericUpDown.Minimum = SpaceColorCalculator.MinIntensity;
        _intensityNumericUpDown.Maximum = SpaceColorCalculator.MaxIntensity;
        _intensityNumericUpDown.Value = SpaceColorCalculator.DefaultIntensity;
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

    private void UpdateNodeColors()
    {
        foreach (TreeNode node in _directoriesTreeView.Nodes)
        {
            UpdateNodeColors(node);
        }
    }

    private void UpdateNodeColors(TreeNode parent)
    {
        foreach (TreeNode node in parent.Nodes)
        {
            if (parent.Tag is DirectorySpace directorySpace)
            {
                UpdateNodeColor(node, directorySpace);
            }
        }
    }

    private void UpdateNodeColor(TreeNode node, DirectorySpace parent)
    {
        if (node.Tag is DirectorySpace directorySpace)
        {
            node.ForeColor = _spaceColorCalculator.GetColorBasedOnSize(directorySpace, parent.MaxTotalSize);

            foreach (TreeNode childNode in node.Nodes)
            {
                UpdateNodeColor(childNode, directorySpace);
            }
        }
        else if (node.Tag is FileSpace fileSpace)
        {
            node.ForeColor = _spaceColorCalculator.GetColorBasedOnSize(fileSpace, parent.Size);
        }
    }

    private void Log(string message, string level = "INFO")
    {
        _uiLogsRichTextBox.Invoke(() => AppendLog(message, level));
    }

    private void AppendLog(string message, string level)
    {
        _uiLogsRichTextBox.AppendText($@"[{level}] {message}{Environment.NewLine}");
        _uiLogsRichTextBox.SelectionStart = _uiLogsRichTextBox.Text.Length;
        _uiLogsRichTextBox.ScrollToCaret();
    }

    private void LogInformation(string text)
    {
        Log(text);
    }

    private void LogWarning(string text)
    {
        Log(text, "WARN");
    }

    private void StartScanning(string disk)
    {
        _calculateProgressBar.Invoke(() => _calculateProgressBar.Style = ProgressBarStyle.Marquee);

        RemoveDiskNode(disk);

        _backgroundWorker.RunWorkerAsync(disk);
    }

    private void RefreshColorButtonClicked(object sender, EventArgs e)
    {
        UpdateNodeColors();
    }

    private void OnChooseDirectoryClicked(object sender, EventArgs e)
    {
        using FolderBrowserDialog folderBrowserDialog = new();

        DialogResult result = folderBrowserDialog.ShowDialog();

        if (result != DialogResult.OK || string.IsNullOrWhiteSpace(folderBrowserDialog.SelectedPath))
        {
            return;
        }

        string selectedPath = folderBrowserDialog.SelectedPath;
        _hardDiskComboBox.SelectedIndex = _hardDiskComboBox.Items.Add(selectedPath);

        StartScanning(selectedPath);
    }
}
