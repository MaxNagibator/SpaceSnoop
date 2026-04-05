using SpaceSnoop.Controls;

namespace SpaceSnoop;

partial class SyncForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _leftPathTextBox = new TextBox();
        _rightPathTextBox = new TextBox();
        _leftBrowseButton = new Button();
        _rightBrowseButton = new Button();
        _exclusionTextBox = new TextBox();
        _compareButton = new Button();
        _diffView = new SyncDiffView();
        _showIdenticalCheckBox = new CheckBox();
        _hashButton = new Button();
        _resolveConflictsButton = new Button();
        _resolveConflictsMenu = new ContextMenuStrip(components = new System.ComponentModel.Container());
        _showSizesCheckBox = new CheckBox();
        _showAbsentAsEmptyCheckBox = new CheckBox();
        _syncModeComboBox = new ComboBox();
        _summaryLabel = new Label();
        _syncButton = new Button();
        _progressBar = new ProgressBar();
        _statusLabel = new Label();
        _leftPathLabel = new Label();
        _rightPathLabel = new Label();
        _exclusionLabel = new Label();
        _topLayout = new TableLayoutPanel();
        _bottomLayout = new TableLayoutPanel();
        _toolStrip = new ToolStrip();
        _helpToolButton = new ToolStripButton();
        _toolTip = new ToolTip();
        _topLayout.SuspendLayout();
        _bottomLayout.SuspendLayout();
        SuspendLayout();
        //
        // _toolTip
        //
        _toolTip.AutoPopDelay = 15000;
        _toolTip.InitialDelay = 400;
        _toolTip.ReshowDelay = 200;
        //
        // _leftPathLabel
        //
        _leftPathLabel.AutoSize = true;
        _leftPathLabel.Anchor = AnchorStyles.Left;
        _leftPathLabel.Text = "Левая:";
        //
        // _leftPathTextBox
        //
        _leftPathTextBox.Dock = DockStyle.Fill;
        _toolTip.SetToolTip(_leftPathTextBox, "Путь к исходной (левой) директории для сравнения");
        //
        // _leftBrowseButton
        //
        _leftBrowseButton.Dock = DockStyle.Fill;
        _leftBrowseButton.Text = "Обзор...";
        _leftBrowseButton.UseVisualStyleBackColor = true;
        _leftBrowseButton.Click += OnLeftBrowseClicked;
        //
        // _rightPathLabel
        //
        _rightPathLabel.AutoSize = true;
        _rightPathLabel.Anchor = AnchorStyles.Left;
        _rightPathLabel.Text = "Правая:";
        //
        // _rightPathTextBox
        //
        _rightPathTextBox.Dock = DockStyle.Fill;
        _toolTip.SetToolTip(_rightPathTextBox, "Путь к целевой (правой) директории для сравнения");
        //
        // _rightBrowseButton
        //
        _rightBrowseButton.Dock = DockStyle.Fill;
        _rightBrowseButton.Text = "Обзор...";
        _rightBrowseButton.UseVisualStyleBackColor = true;
        _rightBrowseButton.Click += OnRightBrowseClicked;
        //
        // _exclusionLabel
        //
        _exclusionLabel.AutoSize = true;
        _exclusionLabel.Anchor = AnchorStyles.Left;
        _exclusionLabel.Text = "Исключить:";
        //
        // _exclusionTextBox
        //
        _exclusionTextBox.Dock = DockStyle.Fill;
        _exclusionTextBox.PlaceholderText = "*.tmp, .git, *.bak";
        _toolTip.SetToolTip(_exclusionTextBox, "Маски файлов/папок через запятую, которые будут пропущены при сравнении.\nПример: *.tmp, .git, *.bak, node_modules");
        //
        // _compareButton
        //
        _compareButton.Dock = DockStyle.Fill;
        _compareButton.Text = "Сравнить";
        _compareButton.UseVisualStyleBackColor = true;
        _compareButton.Click += OnCompareClicked;
        _toolTip.SetToolTip(_compareButton, "Запустить рекурсивное сравнение двух директорий");
        //
        // _topLayout
        //
        _topLayout.ColumnCount = 3;
        _topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topLayout.RowCount = 3;
        _topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _topLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _topLayout.Controls.Add(_leftPathLabel, 0, 0);
        _topLayout.Controls.Add(_leftPathTextBox, 1, 0);
        _topLayout.Controls.Add(_leftBrowseButton, 2, 0);
        _topLayout.Controls.Add(_rightPathLabel, 0, 1);
        _topLayout.Controls.Add(_rightPathTextBox, 1, 1);
        _topLayout.Controls.Add(_rightBrowseButton, 2, 1);
        _topLayout.Controls.Add(_exclusionLabel, 0, 2);
        _topLayout.Controls.Add(_exclusionTextBox, 1, 2);
        _topLayout.Controls.Add(_compareButton, 2, 2);
        _topLayout.Dock = DockStyle.Top;
        _topLayout.AutoSize = true;
        _topLayout.Padding = new Padding(6, 6, 6, 4);
        //
        // _diffView
        //
        _diffView.Dock = DockStyle.Fill;
        _diffView.BorderStyle = BorderStyle.FixedSingle;
        _diffView.ActionChanged += OnDiffViewActionChanged;
        //
        // _helpToolButton
        //
        _helpToolButton.Alignment = ToolStripItemAlignment.Right;
        _helpToolButton.Text = "Справка";
        _helpToolButton.ToolTipText = "Справка по синхронизации директорий";
        _helpToolButton.Click += OnHelpClicked;
        //
        // _toolStrip
        //
        _toolStrip.GripStyle = ToolStripGripStyle.Hidden;
        _toolStrip.Items.Add(_helpToolButton);
        _toolStrip.Dock = DockStyle.Top;
        //
        // _showIdenticalCheckBox
        //
        _showIdenticalCheckBox.AutoSize = true;
        _showIdenticalCheckBox.Anchor = AnchorStyles.Left;
        _showIdenticalCheckBox.Text = "Показать одинаковые";
        _showIdenticalCheckBox.UseVisualStyleBackColor = true;
        _showIdenticalCheckBox.CheckedChanged += OnShowIdenticalChanged;
        _toolTip.SetToolTip(_showIdenticalCheckBox, "Показать файлы, одинаковые на обеих сторонах.\nПо умолчанию они скрыты для удобства.");
        //
        // _hashButton
        //
        _hashButton.AutoSize = true;
        _hashButton.Anchor = AnchorStyles.Left;
        _hashButton.Text = "Проверить хешем";
        _hashButton.Enabled = false;
        _hashButton.UseVisualStyleBackColor = true;
        _hashButton.Click += OnHashCheckClicked;
        _toolTip.SetToolTip(_hashButton, "Вычислить SHA-256 хеши для файлов со статусом «Изменён».\nЕсли хеши совпадут - файл станет «Идентичен».");
        //
        // _resolveConflictsMenu
        //
        _resolveConflictsMenu.Items.Add("Все → копировать слева направо", null, OnResolveAllToRightClicked);
        _resolveConflictsMenu.Items.Add("Все ← копировать справа налево", null, OnResolveAllToLeftClicked);
        _resolveConflictsMenu.Items.Add("Все ⊘ пропустить", null, OnResolveAllSkipClicked);
        //
        // _resolveConflictsButton
        //
        _resolveConflictsButton.AutoSize = true;
        _resolveConflictsButton.Anchor = AnchorStyles.Right;
        _resolveConflictsButton.Text = "Разрешить конфликты ▾";
        _resolveConflictsButton.Enabled = false;
        _resolveConflictsButton.UseVisualStyleBackColor = true;
        _resolveConflictsButton.Click += OnResolveConflictsClicked;
        _toolTip.SetToolTip(_resolveConflictsButton, "Массово назначить действие всем конфликтам и нерешённым элементам.");
        //
        // _showSizesCheckBox
        //
        _showSizesCheckBox.AutoSize = true;
        _showSizesCheckBox.Checked = true;
        _showSizesCheckBox.Anchor = AnchorStyles.Left;
        _showSizesCheckBox.Text = "Размеры";
        _showSizesCheckBox.UseVisualStyleBackColor = true;
        _showSizesCheckBox.CheckedChanged += OnShowSizesChanged;
        _toolTip.SetToolTip(_showSizesCheckBox, "Показать/скрыть размеры файлов и папок");
        //
        // _showAbsentAsEmptyCheckBox
        //
        _showAbsentAsEmptyCheckBox.AutoSize = true;
        _showAbsentAsEmptyCheckBox.Anchor = AnchorStyles.Left;
        _showAbsentAsEmptyCheckBox.Text = "Пустота";
        _showAbsentAsEmptyCheckBox.UseVisualStyleBackColor = true;
        _showAbsentAsEmptyCheckBox.CheckedChanged += OnShowAbsentAsEmptyChanged;
        _toolTip.SetToolTip(_showAbsentAsEmptyCheckBox, "Отображать отсутствующие элементы как пустое место\nвместо зачёркнутого имени.");
        //
        // _syncModeComboBox
        //
        _syncModeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _syncModeComboBox.Anchor = AnchorStyles.Left;
        _syncModeComboBox.Size = new Size(150, 23);
        _syncModeComboBox.Items.AddRange(new object[] { "Левая → Правая", "Правая → Левая", "Двусторонняя" });
        _syncModeComboBox.SelectedIndex = 0;
        _syncModeComboBox.SelectedIndexChanged += OnSyncModeChanged;
        _toolTip.SetToolTip(_syncModeComboBox, "Режим синхронизации:\n• Левая → Правая - копировать из левой в правую\n• Правая → Левая - копировать из правой в левую\n• Двусторонняя - привести обе стороны к одинаковому состоянию");
        //
        // _syncButton
        //
        _syncButton.AutoSize = true;
        _syncButton.Enabled = false;
        _syncButton.Dock = DockStyle.Fill;
        _syncButton.Text = "Синхронизировать";
        _syncButton.UseVisualStyleBackColor = true;
        _syncButton.Click += OnSyncClicked;
        _toolTip.SetToolTip(_syncButton, "Выполнить синхронизацию согласно выбранным действиям.\nНеактивна при наличии неразрешённых конфликтов.");
        //
        // _summaryLabel
        //
        _summaryLabel.AutoSize = true;
        _summaryLabel.Anchor = AnchorStyles.Left;
        _summaryLabel.Text = "";
        //
        // _progressBar
        //
        _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _progressBar.Margin = new Padding(0, 2, 0, 2);
        //
        // _statusLabel
        //
        _statusLabel.AutoSize = true;
        _statusLabel.Anchor = AnchorStyles.Left;
        _statusLabel.Text = "";
        //
        // _bottomLayout
        //
        _bottomLayout.ColumnCount = 6;
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _bottomLayout.RowCount = 4;
        _bottomLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bottomLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bottomLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bottomLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _bottomLayout.Controls.Add(_showIdenticalCheckBox, 0, 0);
        _bottomLayout.Controls.Add(_hashButton, 1, 0);
        _bottomLayout.Controls.Add(_showSizesCheckBox, 2, 0);
        _bottomLayout.Controls.Add(_showAbsentAsEmptyCheckBox, 3, 0);
        _bottomLayout.Controls.Add(_syncModeComboBox, 4, 0);
        _bottomLayout.Controls.Add(_syncButton, 5, 0);
        _bottomLayout.Controls.Add(_summaryLabel, 0, 1);
        _bottomLayout.SetColumnSpan(_summaryLabel, 5);
        _bottomLayout.Controls.Add(_resolveConflictsButton, 5, 1);
        _bottomLayout.Controls.Add(_progressBar, 0, 2);
        _bottomLayout.SetColumnSpan(_progressBar, 6);
        _bottomLayout.Controls.Add(_statusLabel, 0, 3);
        _bottomLayout.SetColumnSpan(_statusLabel, 6);
        _bottomLayout.Dock = DockStyle.Bottom;
        _bottomLayout.AutoSize = true;
        _bottomLayout.Padding = new Padding(6, 2, 6, 2);
        //
        // SyncForm
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(884, 561);
        Controls.Add(_diffView);
        Controls.Add(_bottomLayout);
        Controls.Add(_topLayout);
        Controls.Add(_toolStrip);
        MinimumSize = new Size(700, 400);
        Name = "SyncForm";
        Text = "Синхронизация директорий";
        _topLayout.ResumeLayout(false);
        _topLayout.PerformLayout();
        _bottomLayout.ResumeLayout(false);
        _bottomLayout.PerformLayout();
        ResumeLayout(false);
        PerformLayout();
    }

    private TableLayoutPanel _topLayout;
    private TableLayoutPanel _bottomLayout;
    private Label _leftPathLabel;
    private TextBox _leftPathTextBox;
    private Button _leftBrowseButton;
    private Label _rightPathLabel;
    private TextBox _rightPathTextBox;
    private Button _rightBrowseButton;
    private Label _exclusionLabel;
    private TextBox _exclusionTextBox;
    private Button _compareButton;
    private SyncDiffView _diffView;
    private CheckBox _showIdenticalCheckBox;
    private Button _hashButton;
    private Button _resolveConflictsButton;
    private ContextMenuStrip _resolveConflictsMenu;
    private CheckBox _showSizesCheckBox;
    private CheckBox _showAbsentAsEmptyCheckBox;
    private ComboBox _syncModeComboBox;
    private Label _summaryLabel;
    private Button _syncButton;
    private ToolStrip _toolStrip;
    private ToolStripButton _helpToolButton;
    private ProgressBar _progressBar;
    private Label _statusLabel;
    private ToolTip _toolTip;
}
