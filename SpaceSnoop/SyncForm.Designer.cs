using System.ComponentModel;

namespace SpaceSnoop;

partial class SyncForm
{
    /// <summary>
    /// Required designer variable.
    /// </summary>
    private IContainer components = null;

    /// <summary>
    /// Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private Label sourceDirectoryLabel;
    private ComboBox sourceDirectoryComboBox;
    private Button sourceDirectoryBrowseButton;
    private Button sourceDirectoryScanButton;
    private TreeView sourceDirectoryTreeView;
    private Label targetDirectoryLabel;
    private ComboBox targetDirectoryComboBox;
    private Button targetDirectoryBrowseButton;
    private Button targetDirectoryScanButton;
    private TreeView targetDirectoryTreeView;
    private Button startSyncButton;
    private Button stopSyncButton;
    private ProgressBar progressBar;
    private Label progressLabel;
    private TextBox statusTextBox;
    private Label statusLabel;

    /// <summary>
    /// Required method for Designer support - do not modify
    /// the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        sourceDirectoryLabel = new Label();
        sourceDirectoryComboBox = new ComboBox();
        sourceDirectoryBrowseButton = new Button();
        sourceDirectoryScanButton = new Button();
        sourceDirectoryTreeView = new TreeView();
        targetDirectoryLabel = new Label();
        targetDirectoryComboBox = new ComboBox();
        targetDirectoryBrowseButton = new Button();
        targetDirectoryScanButton = new Button();
        targetDirectoryTreeView = new TreeView();
        startSyncButton = new Button();
        stopSyncButton = new Button();
        progressBar = new ProgressBar();
        progressLabel = new Label();
        statusTextBox = new TextBox();
        statusLabel = new Label();
        SuspendLayout();
        // 
        // sourceDirectoryLabel
        // 
        sourceDirectoryLabel.AutoSize = true;
        sourceDirectoryLabel.Location = new Point(22, 32);
        sourceDirectoryLabel.Margin = new Padding(6, 0, 6, 0);
        sourceDirectoryLabel.Name = "sourceDirectoryLabel";
        sourceDirectoryLabel.Size = new Size(261, 32);
        sourceDirectoryLabel.TabIndex = 0;
        sourceDirectoryLabel.Text = "Исходная директория:";
        // 
        // sourceDirectoryComboBox
        // 
        sourceDirectoryComboBox.Location = new Point(22, 70);
        sourceDirectoryComboBox.Margin = new Padding(6);
        sourceDirectoryComboBox.Name = "sourceDirectoryComboBox";
        sourceDirectoryComboBox.Size = new Size(517, 40);
        sourceDirectoryComboBox.TabIndex = 1;
        sourceDirectoryComboBox.TextChanged += SourceDirectoryComboBox_TextChanged;
        // 
        // sourceDirectoryBrowseButton
        // 
        sourceDirectoryBrowseButton.Location = new Point(553, 68);
        sourceDirectoryBrowseButton.Margin = new Padding(6);
        sourceDirectoryBrowseButton.Name = "sourceDirectoryBrowseButton";
        sourceDirectoryBrowseButton.Size = new Size(139, 53);
        sourceDirectoryBrowseButton.TabIndex = 2;
        sourceDirectoryBrowseButton.Text = "Обзор...";
        sourceDirectoryBrowseButton.UseVisualStyleBackColor = true;
        sourceDirectoryBrowseButton.Click += SourceDirectoryBrowseButton_Click;
        // 
        // sourceDirectoryScanButton
        // 
        sourceDirectoryScanButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        sourceDirectoryScanButton.Location = new Point(22, 512);
        sourceDirectoryScanButton.Margin = new Padding(6);
        sourceDirectoryScanButton.Name = "sourceDirectoryScanButton";
        sourceDirectoryScanButton.Size = new Size(186, 53);
        sourceDirectoryScanButton.TabIndex = 3;
        sourceDirectoryScanButton.Text = "Сканировать";
        sourceDirectoryScanButton.UseVisualStyleBackColor = true;
        sourceDirectoryScanButton.Click += SourceDirectoryScanButton_Click;
        // 
        // sourceDirectoryTreeView
        // 
        sourceDirectoryTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        sourceDirectoryTreeView.Location = new Point(22, 132);
        sourceDirectoryTreeView.Margin = new Padding(6);
        sourceDirectoryTreeView.Name = "sourceDirectoryTreeView";
        sourceDirectoryTreeView.ShowNodeToolTips = true;
        sourceDirectoryTreeView.Size = new Size(665, 358);
        sourceDirectoryTreeView.TabIndex = 4;
        sourceDirectoryTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
        // 
        // targetDirectoryLabel
        // 
        targetDirectoryLabel.AutoSize = true;
        targetDirectoryLabel.Location = new Point(724, 32);
        targetDirectoryLabel.Margin = new Padding(6, 0, 6, 0);
        targetDirectoryLabel.Name = "targetDirectoryLabel";
        targetDirectoryLabel.Size = new Size(250, 32);
        targetDirectoryLabel.TabIndex = 5;
        targetDirectoryLabel.Text = "Целевая директория:";
        // 
        // targetDirectoryComboBox
        // 
        targetDirectoryComboBox.Location = new Point(724, 70);
        targetDirectoryComboBox.Margin = new Padding(6);
        targetDirectoryComboBox.Name = "targetDirectoryComboBox";
        targetDirectoryComboBox.Size = new Size(517, 40);
        targetDirectoryComboBox.TabIndex = 6;
        targetDirectoryComboBox.TextChanged += TargetDirectoryComboBox_TextChanged;
        // 
        // targetDirectoryBrowseButton
        // 
        targetDirectoryBrowseButton.Location = new Point(1255, 68);
        targetDirectoryBrowseButton.Margin = new Padding(6);
        targetDirectoryBrowseButton.Name = "targetDirectoryBrowseButton";
        targetDirectoryBrowseButton.Size = new Size(139, 53);
        targetDirectoryBrowseButton.TabIndex = 7;
        targetDirectoryBrowseButton.Text = "Обзор...";
        targetDirectoryBrowseButton.UseVisualStyleBackColor = true;
        targetDirectoryBrowseButton.Click += TargetDirectoryBrowseButton_Click;
        // 
        // targetDirectoryScanButton
        // 
        targetDirectoryScanButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        targetDirectoryScanButton.Location = new Point(724, 512);
        targetDirectoryScanButton.Margin = new Padding(6);
        targetDirectoryScanButton.Name = "targetDirectoryScanButton";
        targetDirectoryScanButton.Size = new Size(186, 53);
        targetDirectoryScanButton.TabIndex = 8;
        targetDirectoryScanButton.Text = "Сканировать";
        targetDirectoryScanButton.UseVisualStyleBackColor = true;
        targetDirectoryScanButton.Click += TargetDirectoryScanButton_Click;
        // 
        // targetDirectoryTreeView
        // 
        targetDirectoryTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        targetDirectoryTreeView.Location = new Point(724, 132);
        targetDirectoryTreeView.Margin = new Padding(6);
        targetDirectoryTreeView.Name = "targetDirectoryTreeView";
        targetDirectoryTreeView.ShowNodeToolTips = true;
        targetDirectoryTreeView.Size = new Size(665, 358);
        targetDirectoryTreeView.TabIndex = 9;
        targetDirectoryTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
        // 
        // startSyncButton
        // 
        startSyncButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        startSyncButton.Location = new Point(22, 597);
        startSyncButton.Margin = new Padding(6);
        startSyncButton.Name = "startSyncButton";
        startSyncButton.Size = new Size(314, 64);
        startSyncButton.TabIndex = 10;
        startSyncButton.Text = "Начать синхронизацию";
        startSyncButton.UseVisualStyleBackColor = true;
        startSyncButton.Click += StartSyncButton_Click;
        // 
        // stopSyncButton
        // 
        stopSyncButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        stopSyncButton.Enabled = false;
        stopSyncButton.Location = new Point(369, 597);
        stopSyncButton.Margin = new Padding(6);
        stopSyncButton.Name = "stopSyncButton";
        stopSyncButton.Size = new Size(309, 64);
        stopSyncButton.TabIndex = 11;
        stopSyncButton.Text = "Остановить";
        stopSyncButton.UseVisualStyleBackColor = true;
        stopSyncButton.Click += StopSyncButton_Click;
        // 
        // progressBar
        // 
        progressBar.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(22, 742);
        progressBar.Margin = new Padding(6);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(1371, 49);
        progressBar.TabIndex = 13;
        // 
        // progressLabel
        // 
        progressLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        progressLabel.AutoSize = true;
        progressLabel.Location = new Point(22, 704);
        progressLabel.Margin = new Padding(6, 0, 6, 0);
        progressLabel.Name = "progressLabel";
        progressLabel.Size = new Size(122, 32);
        progressLabel.TabIndex = 12;
        progressLabel.Text = "Прогресс:";
        // 
        // statusTextBox
        // 
        statusTextBox.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusTextBox.Location = new Point(22, 870);
        statusTextBox.Margin = new Padding(6);
        statusTextBox.Multiline = true;
        statusTextBox.Name = "statusTextBox";
        statusTextBox.ReadOnly = true;
        statusTextBox.ScrollBars = ScrollBars.Vertical;
        statusTextBox.Size = new Size(1367, 251);
        statusTextBox.TabIndex = 15;
        // 
        // statusLabel
        // 
        statusLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        statusLabel.AutoSize = true;
        statusLabel.Location = new Point(22, 832);
        statusLabel.Margin = new Padding(6, 0, 6, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(89, 32);
        statusLabel.TabIndex = 14;
        statusLabel.Text = "Статус:";
        // 
        // SyncForm
        // 
        AutoScaleDimensions = new SizeF(13F, 32F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1430, 1152);
        Controls.Add(statusTextBox);
        Controls.Add(statusLabel);
        Controls.Add(progressBar);
        Controls.Add(progressLabel);
        Controls.Add(stopSyncButton);
        Controls.Add(startSyncButton);
        Controls.Add(targetDirectoryTreeView);
        Controls.Add(targetDirectoryScanButton);
        Controls.Add(targetDirectoryBrowseButton);
        Controls.Add(targetDirectoryComboBox);
        Controls.Add(targetDirectoryLabel);
        Controls.Add(sourceDirectoryTreeView);
        Controls.Add(sourceDirectoryScanButton);
        Controls.Add(sourceDirectoryBrowseButton);
        Controls.Add(sourceDirectoryComboBox);
        Controls.Add(sourceDirectoryLabel);
        Margin = new Padding(6);
        MaximizeBox = false;
        Name = "SyncForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Синхронизация директорий";
        ResumeLayout(false);
        PerformLayout();
    }

    #endregion
}
