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
            _uiLogsTextBox = new TextBox();
            SuspendLayout();
            // 
            // _hardDiskComboBox
            // 
            _hardDiskComboBox.FormattingEnabled = true;
            _hardDiskComboBox.Location = new Point(12, 12);
            _hardDiskComboBox.Name = "_hardDiskComboBox";
            _hardDiskComboBox.Size = new Size(203, 23);
            _hardDiskComboBox.TabIndex = 0;
            // 
            // _startButton
            // 
            _startButton.Location = new Point(12, 41);
            _startButton.Name = "_startButton";
            _startButton.Size = new Size(203, 23);
            _startButton.TabIndex = 1;
            _startButton.Text = "start";
            _startButton.UseVisualStyleBackColor = true;
            _startButton.Click += OnStartButtonClicked;
            // 
            // _stopButton
            // 
            _stopButton.Location = new Point(12, 70);
            _stopButton.Name = "_stopButton";
            _stopButton.Size = new Size(203, 23);
            _stopButton.TabIndex = 2;
            _stopButton.Text = "stop";
            _stopButton.UseVisualStyleBackColor = true;
            _stopButton.Click += OnStopButtonClicked;
            // 
            // _directoriesTreeView
            // 
            _directoriesTreeView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _directoriesTreeView.Location = new Point(221, 12);
            _directoriesTreeView.Name = "_directoriesTreeView";
            _directoriesTreeView.Size = new Size(596, 533);
            _directoriesTreeView.TabIndex = 3;
            _directoriesTreeView.BeforeExpand += OnDirectoriesTreeViewBeforeExpanded;
            // 
            // _uiLogsTextBox
            // 
            _uiLogsTextBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
            _uiLogsTextBox.Location = new Point(12, 99);
            _uiLogsTextBox.Multiline = true;
            _uiLogsTextBox.Name = "_uiLogsTextBox";
            _uiLogsTextBox.ScrollBars = ScrollBars.Vertical;
            _uiLogsTextBox.Size = new Size(203, 446);
            _uiLogsTextBox.TabIndex = 4;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(829, 557);
            Controls.Add(_uiLogsTextBox);
            Controls.Add(_directoriesTreeView);
            Controls.Add(_stopButton);
            Controls.Add(_startButton);
            Controls.Add(_hardDiskComboBox);
            Name = "MainForm";
            Text = "SpaceSnoop";
            Load += OnFormLoaded;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private ComboBox _hardDiskComboBox;
        private Button _startButton;
        private Button _stopButton;
        private TreeView _directoriesTreeView;
        private TextBox _uiLogsTextBox;
    }
}
