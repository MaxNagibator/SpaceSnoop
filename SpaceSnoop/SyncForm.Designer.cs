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

    private TableLayoutPanel mainTableLayoutPanel;
    private GroupBox sourceDirectoryGroupBox;
    private Label sourceDirectoryLabel;
    private ComboBox sourceDirectoryComboBox;
    private Button sourceDirectoryBrowseButton;
    private TreeView sourceDirectoryTreeView;
    private GroupBox targetDirectoryGroupBox;
    private Label targetDirectoryLabel;
    private ComboBox targetDirectoryComboBox;
    private Button targetDirectoryBrowseButton;
    private TreeView targetDirectoryTreeView;
    private GroupBox syncOptionsGroupBox;
    private Button startSyncButton;
    private Button stopSyncButton;
    private GroupBox progressStatusGroupBox;
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
        mainTableLayoutPanel = new TableLayoutPanel();
        sourceDirectoryGroupBox = new GroupBox();
        sourceDirectoryTreeView = new TreeView();
        sourceDirectoryBrowseButton = new Button();
        sourceDirectoryComboBox = new ComboBox();
        sourceDirectoryLabel = new Label();
        targetDirectoryGroupBox = new GroupBox();
        targetDirectoryTreeView = new TreeView();
        targetDirectoryBrowseButton = new Button();
        targetDirectoryComboBox = new ComboBox();
        targetDirectoryLabel = new Label();
        syncOptionsGroupBox = new GroupBox();
        stopSyncButton = new Button();
        startSyncButton = new Button();
        progressStatusGroupBox = new GroupBox();
        statusTextBox = new TextBox();
        statusLabel = new Label();
        progressBar = new ProgressBar();
        progressLabel = new Label();
        mainTableLayoutPanel.SuspendLayout();
        sourceDirectoryGroupBox.SuspendLayout();
        targetDirectoryGroupBox.SuspendLayout();
        syncOptionsGroupBox.SuspendLayout();
        progressStatusGroupBox.SuspendLayout();
        SuspendLayout();
        // 
        // mainTableLayoutPanel
        // 
        mainTableLayoutPanel.ColumnCount = 2;
        mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mainTableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        mainTableLayoutPanel.Controls.Add(sourceDirectoryGroupBox, 0, 0);
        mainTableLayoutPanel.Controls.Add(targetDirectoryGroupBox, 1, 0);
        mainTableLayoutPanel.Controls.Add(syncOptionsGroupBox, 0, 1);
        mainTableLayoutPanel.Controls.Add(progressStatusGroupBox, 0, 2);
        mainTableLayoutPanel.Dock = DockStyle.Fill;
        mainTableLayoutPanel.Location = new Point(0, 0);
        mainTableLayoutPanel.Margin = new Padding(6);
        mainTableLayoutPanel.Name = "mainTableLayoutPanel";
        mainTableLayoutPanel.Padding = new Padding(12);
        mainTableLayoutPanel.RowCount = 3;
        mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
        mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 110F));
        mainTableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 40F));
        mainTableLayoutPanel.Size = new Size(1584, 1441);
        mainTableLayoutPanel.TabIndex = 0;
        // 
        // sourceDirectoryGroupBox
        // 
        sourceDirectoryGroupBox.Controls.Add(sourceDirectoryTreeView);
        sourceDirectoryGroupBox.Controls.Add(sourceDirectoryBrowseButton);
        sourceDirectoryGroupBox.Controls.Add(sourceDirectoryComboBox);
        sourceDirectoryGroupBox.Controls.Add(sourceDirectoryLabel);
        sourceDirectoryGroupBox.Dock = DockStyle.Fill;
        sourceDirectoryGroupBox.Location = new Point(18, 18);
        sourceDirectoryGroupBox.Margin = new Padding(6);
        sourceDirectoryGroupBox.Name = "sourceDirectoryGroupBox";
        sourceDirectoryGroupBox.Padding = new Padding(6);
        sourceDirectoryGroupBox.Size = new Size(768, 772);
        sourceDirectoryGroupBox.TabIndex = 0;
        sourceDirectoryGroupBox.TabStop = false;
        sourceDirectoryGroupBox.Text = "Исходная директория";
        // 
        // sourceDirectoryTreeView
        // 
        sourceDirectoryTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        sourceDirectoryTreeView.Location = new Point(12, 132);
        sourceDirectoryTreeView.Margin = new Padding(6);
        sourceDirectoryTreeView.Name = "sourceDirectoryTreeView";
        sourceDirectoryTreeView.ShowNodeToolTips = true;
        sourceDirectoryTreeView.Size = new Size(744, 622);
        sourceDirectoryTreeView.TabIndex = 3;
        sourceDirectoryTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
        // 
        // sourceDirectoryBrowseButton
        // 
        sourceDirectoryBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        sourceDirectoryBrowseButton.Location = new Point(631, 74);
        sourceDirectoryBrowseButton.Margin = new Padding(6);
        sourceDirectoryBrowseButton.Name = "sourceDirectoryBrowseButton";
        sourceDirectoryBrowseButton.Size = new Size(125, 44);
        sourceDirectoryBrowseButton.TabIndex = 2;
        sourceDirectoryBrowseButton.Text = "Обзор...";
        sourceDirectoryBrowseButton.UseVisualStyleBackColor = true;
        sourceDirectoryBrowseButton.Click += SourceDirectoryBrowseButton_Click;
        // 
        // sourceDirectoryComboBox
        // 
        sourceDirectoryComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        sourceDirectoryComboBox.Location = new Point(12, 76);
        sourceDirectoryComboBox.Margin = new Padding(6);
        sourceDirectoryComboBox.Name = "sourceDirectoryComboBox";
        sourceDirectoryComboBox.Size = new Size(607, 40);
        sourceDirectoryComboBox.TabIndex = 1;
        sourceDirectoryComboBox.TextChanged += SourceDirectoryComboBox_TextChanged;
        // 
        // sourceDirectoryLabel
        // 
        sourceDirectoryLabel.AutoSize = true;
        sourceDirectoryLabel.Location = new Point(12, 38);
        sourceDirectoryLabel.Margin = new Padding(6, 0, 6, 0);
        sourceDirectoryLabel.Name = "sourceDirectoryLabel";
        sourceDirectoryLabel.Size = new Size(70, 32);
        sourceDirectoryLabel.TabIndex = 0;
        sourceDirectoryLabel.Text = "Путь:";
        // 
        // targetDirectoryGroupBox
        // 
        targetDirectoryGroupBox.Controls.Add(targetDirectoryTreeView);
        targetDirectoryGroupBox.Controls.Add(targetDirectoryBrowseButton);
        targetDirectoryGroupBox.Controls.Add(targetDirectoryComboBox);
        targetDirectoryGroupBox.Controls.Add(targetDirectoryLabel);
        targetDirectoryGroupBox.Dock = DockStyle.Fill;
        targetDirectoryGroupBox.Location = new Point(798, 18);
        targetDirectoryGroupBox.Margin = new Padding(6);
        targetDirectoryGroupBox.Name = "targetDirectoryGroupBox";
        targetDirectoryGroupBox.Padding = new Padding(6);
        targetDirectoryGroupBox.Size = new Size(768, 772);
        targetDirectoryGroupBox.TabIndex = 1;
        targetDirectoryGroupBox.TabStop = false;
        targetDirectoryGroupBox.Text = "Целевая директория";
        // 
        // targetDirectoryTreeView
        // 
        targetDirectoryTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        targetDirectoryTreeView.Location = new Point(12, 132);
        targetDirectoryTreeView.Margin = new Padding(6);
        targetDirectoryTreeView.Name = "targetDirectoryTreeView";
        targetDirectoryTreeView.ShowNodeToolTips = true;
        targetDirectoryTreeView.Size = new Size(744, 622);
        targetDirectoryTreeView.TabIndex = 3;
        targetDirectoryTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
        // 
        // targetDirectoryBrowseButton
        // 
        targetDirectoryBrowseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        targetDirectoryBrowseButton.Location = new Point(631, 74);
        targetDirectoryBrowseButton.Margin = new Padding(6);
        targetDirectoryBrowseButton.Name = "targetDirectoryBrowseButton";
        targetDirectoryBrowseButton.Size = new Size(125, 44);
        targetDirectoryBrowseButton.TabIndex = 2;
        targetDirectoryBrowseButton.Text = "Обзор...";
        targetDirectoryBrowseButton.UseVisualStyleBackColor = true;
        targetDirectoryBrowseButton.Click += TargetDirectoryBrowseButton_Click;
        // 
        // targetDirectoryComboBox
        // 
        targetDirectoryComboBox.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        targetDirectoryComboBox.Location = new Point(12, 76);
        targetDirectoryComboBox.Margin = new Padding(6);
        targetDirectoryComboBox.Name = "targetDirectoryComboBox";
        targetDirectoryComboBox.Size = new Size(607, 40);
        targetDirectoryComboBox.TabIndex = 1;
        targetDirectoryComboBox.TextChanged += TargetDirectoryComboBox_TextChanged;
        // 
        // targetDirectoryLabel
        // 
        targetDirectoryLabel.AutoSize = true;
        targetDirectoryLabel.Location = new Point(12, 38);
        targetDirectoryLabel.Margin = new Padding(6, 0, 6, 0);
        targetDirectoryLabel.Name = "targetDirectoryLabel";
        targetDirectoryLabel.Size = new Size(70, 32);
        targetDirectoryLabel.TabIndex = 0;
        targetDirectoryLabel.Text = "Путь:";
        // 
        // syncOptionsGroupBox
        // 
        mainTableLayoutPanel.SetColumnSpan(syncOptionsGroupBox, 2);
        syncOptionsGroupBox.Controls.Add(stopSyncButton);
        syncOptionsGroupBox.Controls.Add(startSyncButton);
        syncOptionsGroupBox.Dock = DockStyle.Fill;
        syncOptionsGroupBox.Location = new Point(18, 802);
        syncOptionsGroupBox.Margin = new Padding(6);
        syncOptionsGroupBox.Name = "syncOptionsGroupBox";
        syncOptionsGroupBox.Padding = new Padding(6);
        syncOptionsGroupBox.Size = new Size(1548, 98);
        syncOptionsGroupBox.TabIndex = 2;
        syncOptionsGroupBox.TabStop = false;
        syncOptionsGroupBox.Text = "Параметры синхронизации";
        // 
        // stopSyncButton
        // 
        stopSyncButton.Enabled = false;
        stopSyncButton.Location = new Point(310, 32);
        stopSyncButton.Margin = new Padding(6);
        stopSyncButton.Name = "stopSyncButton";
        stopSyncButton.Size = new Size(280, 50);
        stopSyncButton.TabIndex = 1;
        stopSyncButton.Text = "Остановить";
        stopSyncButton.UseVisualStyleBackColor = true;
        stopSyncButton.Click += StopSyncButton_Click;
        // 
        // startSyncButton
        // 
        startSyncButton.Location = new Point(12, 32);
        startSyncButton.Margin = new Padding(6);
        startSyncButton.Name = "startSyncButton";
        startSyncButton.Size = new Size(280, 50);
        startSyncButton.TabIndex = 0;
        startSyncButton.Text = "Начать";
        startSyncButton.UseVisualStyleBackColor = true;
        startSyncButton.Click += StartSyncButton_Click;
        // 
        // progressStatusGroupBox
        // 
        mainTableLayoutPanel.SetColumnSpan(progressStatusGroupBox, 2);
        progressStatusGroupBox.Controls.Add(statusTextBox);
        progressStatusGroupBox.Controls.Add(statusLabel);
        progressStatusGroupBox.Controls.Add(progressBar);
        progressStatusGroupBox.Controls.Add(progressLabel);
        progressStatusGroupBox.Dock = DockStyle.Fill;
        progressStatusGroupBox.Location = new Point(18, 912);
        progressStatusGroupBox.Margin = new Padding(6);
        progressStatusGroupBox.Name = "progressStatusGroupBox";
        progressStatusGroupBox.Padding = new Padding(6);
        progressStatusGroupBox.Size = new Size(1548, 511);
        progressStatusGroupBox.TabIndex = 3;
        progressStatusGroupBox.TabStop = false;
        progressStatusGroupBox.Text = "Прогресс и статус";
        // 
        // statusTextBox
        // 
        statusTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        statusTextBox.Location = new Point(12, 182);
        statusTextBox.Margin = new Padding(6);
        statusTextBox.Multiline = true;
        statusTextBox.Name = "statusTextBox";
        statusTextBox.ReadOnly = true;
        statusTextBox.ScrollBars = ScrollBars.Vertical;
        statusTextBox.Size = new Size(1539, 317);
        statusTextBox.TabIndex = 3;
        // 
        // statusLabel
        // 
        statusLabel.AutoSize = true;
        statusLabel.Location = new Point(12, 144);
        statusLabel.Margin = new Padding(6, 0, 6, 0);
        statusLabel.Name = "statusLabel";
        statusLabel.Size = new Size(89, 32);
        statusLabel.TabIndex = 2;
        statusLabel.Text = "Статус:";
        // 
        // progressBar
        // 
        progressBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        progressBar.Location = new Point(12, 76);
        progressBar.Margin = new Padding(6);
        progressBar.Name = "progressBar";
        progressBar.Size = new Size(1538, 49);
        progressBar.TabIndex = 1;
        // 
        // progressLabel
        // 
        progressLabel.AutoSize = true;
        progressLabel.Location = new Point(12, 38);
        progressLabel.Margin = new Padding(6, 0, 6, 0);
        progressLabel.Name = "progressLabel";
        progressLabel.Size = new Size(122, 32);
        progressLabel.TabIndex = 0;
        progressLabel.Text = "Прогресс:";
        // 
        // SyncForm
        // 
        AutoScaleDimensions = new SizeF(13F, 32F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1584, 1441);
        Controls.Add(mainTableLayoutPanel);
        Margin = new Padding(6);
        MinimumSize = new Size(1200, 700);
        Name = "SyncForm";
        StartPosition = FormStartPosition.CenterParent;
        Text = "Синхронизация директорий";
        mainTableLayoutPanel.ResumeLayout(false);
        sourceDirectoryGroupBox.ResumeLayout(false);
        sourceDirectoryGroupBox.PerformLayout();
        targetDirectoryGroupBox.ResumeLayout(false);
        targetDirectoryGroupBox.PerformLayout();
        syncOptionsGroupBox.ResumeLayout(false);
        progressStatusGroupBox.ResumeLayout(false);
        progressStatusGroupBox.PerformLayout();
        ResumeLayout(false);
    }

    #endregion
}
