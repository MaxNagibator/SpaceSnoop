namespace SpaceSnoop
{
    partial class MainForm
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            _hardDiskComboBox = new ComboBox();
            _startButton = new Button();
            _stopButton = new Button();
            _directoriesTreeView = new TreeView();
            _useMultithreadingCheckBox = new CheckBox();
            _uiLogsRichTextBox = new RichTextBox();
            _calculateProgressBar = new ProgressBar();
            _sortGroupBox = new GroupBox();
            _sortModeComboBox = new ComboBox();
            _invertSortCheckBox = new CheckBox();
            _controlGroupBox = new GroupBox();
            _refreshNodesButton = new Button();
            _intensityNumericUpDown = new NumericUpDown();
            _chooseFolderButton = new Button();
            _mainTabControl = new TabControl();
            _controlTabPage = new TabPage();
            _settingsTabPage = new TabPage();
            _sortGroupBox.SuspendLayout();
            _controlGroupBox.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_intensityNumericUpDown).BeginInit();
            _mainTabControl.SuspendLayout();
            _controlTabPage.SuspendLayout();
            _settingsTabPage.SuspendLayout();
            SuspendLayout();
            // 
            // _hardDiskComboBox
            // 
            _hardDiskComboBox.FormattingEnabled = true;
            _hardDiskComboBox.Location = new Point(6, 8);
            _hardDiskComboBox.Name = "_hardDiskComboBox";
            _hardDiskComboBox.Size = new Size(90, 23);
            _hardDiskComboBox.TabIndex = 0;
            // 
            // _startButton
            // 
            _startButton.Location = new Point(101, 37);
            _startButton.Name = "_startButton";
            _startButton.Size = new Size(107, 23);
            _startButton.TabIndex = 1;
            _startButton.Text = "start";
            _startButton.UseVisualStyleBackColor = true;
            _startButton.Click += OnStartButtonClicked;
            // 
            // _stopButton
            // 
            _stopButton.Location = new Point(6, 89);
            _stopButton.Name = "_stopButton";
            _stopButton.Size = new Size(202, 23);
            _stopButton.TabIndex = 2;
            _stopButton.Text = "stop";
            _stopButton.UseVisualStyleBackColor = true;
            _stopButton.Click += OnStopButtonClicked;
            // 
            // _directoriesTreeView
            // 
            _directoriesTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _directoriesTreeView.Location = new Point(240, 12);
            _directoriesTreeView.Name = "_directoriesTreeView";
            _directoriesTreeView.Size = new Size(435, 451);
            _directoriesTreeView.TabIndex = 3;
            _directoriesTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
            _directoriesTreeView.NodeMouseClick += OnNodeMouseClicked;
            // 
            // _useMultithreadingCheckBox
            // 
            _useMultithreadingCheckBox.AutoSize = true;
            _useMultithreadingCheckBox.Location = new Point(6, 40);
            _useMultithreadingCheckBox.Name = "_useMultithreadingCheckBox";
            _useMultithreadingCheckBox.Size = new Size(90, 19);
            _useMultithreadingCheckBox.TabIndex = 5;
            _useMultithreadingCheckBox.Text = "MultiThread";
            _useMultithreadingCheckBox.UseVisualStyleBackColor = true;
            // 
            // _uiLogsRichTextBox
            // 
            _uiLogsRichTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _uiLogsRichTextBox.Location = new Point(12, 165);
            _uiLogsRichTextBox.Name = "_uiLogsRichTextBox";
            _uiLogsRichTextBox.ReadOnly = true;
            _uiLogsRichTextBox.Size = new Size(222, 298);
            _uiLogsRichTextBox.TabIndex = 6;
            _uiLogsRichTextBox.Text = "";
            // 
            // _calculateProgressBar
            // 
            _calculateProgressBar.Location = new Point(6, 66);
            _calculateProgressBar.Name = "_calculateProgressBar";
            _calculateProgressBar.Size = new Size(202, 17);
            _calculateProgressBar.TabIndex = 7;
            // 
            // _sortGroupBox
            // 
            _sortGroupBox.Controls.Add(_sortModeComboBox);
            _sortGroupBox.Controls.Add(_invertSortCheckBox);
            _sortGroupBox.Location = new Point(6, 7);
            _sortGroupBox.Name = "_sortGroupBox";
            _sortGroupBox.Size = new Size(202, 51);
            _sortGroupBox.TabIndex = 8;
            _sortGroupBox.TabStop = false;
            _sortGroupBox.Text = "Сортировка";
            // 
            // _sortModeComboBox
            // 
            _sortModeComboBox.FormattingEnabled = true;
            _sortModeComboBox.Location = new Point(81, 20);
            _sortModeComboBox.Name = "_sortModeComboBox";
            _sortModeComboBox.Size = new Size(115, 23);
            _sortModeComboBox.TabIndex = 0;
            // 
            // _invertSortCheckBox
            // 
            _invertSortCheckBox.AutoSize = true;
            _invertSortCheckBox.Location = new Point(6, 22);
            _invertSortCheckBox.Name = "_invertSortCheckBox";
            _invertSortCheckBox.Size = new Size(56, 19);
            _invertSortCheckBox.TabIndex = 9;
            _invertSortCheckBox.Text = "Invert";
            _invertSortCheckBox.UseVisualStyleBackColor = true;
            _invertSortCheckBox.CheckedChanged += OnInvertSortCheckBoxChanged;
            // 
            // _controlGroupBox
            // 
            _controlGroupBox.Controls.Add(_refreshNodesButton);
            _controlGroupBox.Controls.Add(_intensityNumericUpDown);
            _controlGroupBox.Location = new Point(6, 64);
            _controlGroupBox.Name = "_controlGroupBox";
            _controlGroupBox.Size = new Size(202, 49);
            _controlGroupBox.TabIndex = 9;
            _controlGroupBox.TabStop = false;
            _controlGroupBox.Text = "Интенсивность";
            // 
            // _refreshNodesButton
            // 
            _refreshNodesButton.Location = new Point(6, 20);
            _refreshNodesButton.Name = "_refreshNodesButton";
            _refreshNodesButton.Size = new Size(69, 23);
            _refreshNodesButton.TabIndex = 11;
            _refreshNodesButton.Text = "обновить";
            _refreshNodesButton.UseVisualStyleBackColor = true;
            _refreshNodesButton.Click += RefreshColorButtonClicked;
            // 
            // _intensityNumericUpDown
            // 
            _intensityNumericUpDown.Location = new Point(81, 20);
            _intensityNumericUpDown.Name = "_intensityNumericUpDown";
            _intensityNumericUpDown.Size = new Size(115, 23);
            _intensityNumericUpDown.TabIndex = 10;
            _intensityNumericUpDown.ValueChanged += OnIntensityChanged;
            // 
            // _chooseFolderButton
            // 
            _chooseFolderButton.Location = new Point(101, 6);
            _chooseFolderButton.Name = "_chooseFolderButton";
            _chooseFolderButton.RightToLeft = RightToLeft.Yes;
            _chooseFolderButton.Size = new Size(107, 25);
            _chooseFolderButton.TabIndex = 8;
            _chooseFolderButton.Text = "выбрать";
            _chooseFolderButton.UseVisualStyleBackColor = true;
            _chooseFolderButton.Click += OnChooseDirectoryClicked;
            // 
            // _mainTabControl
            // 
            _mainTabControl.Controls.Add(_controlTabPage);
            _mainTabControl.Controls.Add(_settingsTabPage);
            _mainTabControl.Location = new Point(12, 12);
            _mainTabControl.Name = "_mainTabControl";
            _mainTabControl.SelectedIndex = 0;
            _mainTabControl.Size = new Size(222, 147);
            _mainTabControl.TabIndex = 10;
            // 
            // _controlTabPage
            // 
            _controlTabPage.Controls.Add(_stopButton);
            _controlTabPage.Controls.Add(_calculateProgressBar);
            _controlTabPage.Controls.Add(_startButton);
            _controlTabPage.Controls.Add(_chooseFolderButton);
            _controlTabPage.Controls.Add(_hardDiskComboBox);
            _controlTabPage.Controls.Add(_useMultithreadingCheckBox);
            _controlTabPage.Location = new Point(4, 24);
            _controlTabPage.Name = "_controlTabPage";
            _controlTabPage.Padding = new Padding(3);
            _controlTabPage.Size = new Size(214, 119);
            _controlTabPage.TabIndex = 0;
            _controlTabPage.Text = "Сканирование";
            _controlTabPage.UseVisualStyleBackColor = true;
            // 
            // _settingsTabPage
            // 
            _settingsTabPage.Controls.Add(_sortGroupBox);
            _settingsTabPage.Controls.Add(_controlGroupBox);
            _settingsTabPage.Location = new Point(4, 24);
            _settingsTabPage.Name = "_settingsTabPage";
            _settingsTabPage.Padding = new Padding(3);
            _settingsTabPage.Size = new Size(214, 119);
            _settingsTabPage.TabIndex = 1;
            _settingsTabPage.Text = "Настройки";
            _settingsTabPage.UseVisualStyleBackColor = true;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(687, 475);
            Controls.Add(_mainTabControl);
            Controls.Add(_uiLogsRichTextBox);
            Controls.Add(_directoriesTreeView);
            MinimumSize = new Size(463, 316);
            Name = "MainForm";
            Text = "SpaceSnoop";
            Load += OnFormLoaded;
            _sortGroupBox.ResumeLayout(false);
            _sortGroupBox.PerformLayout();
            _controlGroupBox.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_intensityNumericUpDown).EndInit();
            _mainTabControl.ResumeLayout(false);
            _controlTabPage.ResumeLayout(false);
            _controlTabPage.PerformLayout();
            _settingsTabPage.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private ComboBox _hardDiskComboBox;
        private Button _startButton;
        private Button _stopButton;
        private TreeView _directoriesTreeView;
        private CheckBox _useMultithreadingCheckBox;
        private RichTextBox _uiLogsRichTextBox;
        private ProgressBar _calculateProgressBar;
        private GroupBox _sortGroupBox;
        private ComboBox _sortModeComboBox;
        private CheckBox _invertSortCheckBox;
        private GroupBox _controlGroupBox;
        private Button _chooseFolderButton;
        private TabControl _mainTabControl;
        private TabPage _controlTabPage;
        private TabPage _settingsTabPage;
        private NumericUpDown _intensityNumericUpDown;
        private Button _refreshNodesButton;
    }
}
