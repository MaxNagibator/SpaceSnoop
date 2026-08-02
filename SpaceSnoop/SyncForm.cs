using SpaceSnoop.Services;

namespace SpaceSnoop;

public partial class SyncForm : Form
{
    private static readonly string SettingsFile =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync-settings.ini");

    private readonly SyncWorkerService _workerService;

    private ComparisonResult? _comparisonResult;
    private CancellationTokenSource? _cancellationTokenSource;

    public SyncForm(string? initialLeftPath = null)
    {
        _workerService = new();
        _workerService.CompareCompleted += OnCompareCompleted;
        _workerService.SyncCompleted += OnSyncCompleted;
        _workerService.HashCompleted += OnHashCompleted;

        InitializeComponent();
        LoadSettings();

        if (initialLeftPath != null)
        {
            _leftPathTextBox.Text = initialLeftPath;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _workerService.CompareCompleted -= OnCompareCompleted;
        _workerService.SyncCompleted -= OnSyncCompleted;
        _workerService.HashCompleted -= OnHashCompleted;
        _workerService.Dispose();
        base.OnFormClosing(e);
    }

    private void OnLeftBrowseClicked(object? sender, EventArgs e)
    {
        BrowsePath(_leftPathTextBox);
    }

    private void OnRightBrowseClicked(object? sender, EventArgs e)
    {
        BrowsePath(_rightPathTextBox);
    }

    private void OnCompareClicked(object? sender, EventArgs e)
    {
        var leftPath = _leftPathTextBox.Text.Trim();
        var rightPath = _rightPathTextBox.Text.Trim();

        if (string.IsNullOrEmpty(leftPath) || string.IsNullOrEmpty(rightPath))
        {
            MessageBox.Show(this, "Укажите обе директории.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Directory.Exists(leftPath) || !Directory.Exists(rightPath))
        {
            MessageBox.Show(this, "Одна из директорий не существует.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _comparisonResult = null;
        _diffView.SetData(null);
        _syncButton.Enabled = false;
        _compareButton.Enabled = false;
        _hashButton.Enabled = false;
        _resolveConflictsButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _statusLabel.Text = "Сравнение...";

        ResetCancellationTokenSource();
        _workerService.StartCompare(new(leftPath, rightPath, _exclusionTextBox.Text, CurrentMode(), SyncWinner.Newest, false), _cancellationTokenSource!.Token);
    }

    private void OnCompareCompleted(object? sender, SyncWorkerService.CompareResponse? response)
    {
        _progressBar.Style = ProgressBarStyle.Blocks;
        _compareButton.Enabled = true;

        if (response?.Error != null)
        {
            _statusLabel.Text = $"Ошибка: {response.Error}";
            return;
        }

        if (response?.Result == null)
        {
            _statusLabel.Text = "Сравнение отменено.";
            return;
        }

        _comparisonResult = response.Result;
        _statusLabel.Text = $"Сравнение завершено за {response.Elapsed.TotalSeconds:F2} с";

        ApplyCurrentMode();
        _diffView.SetData(_comparisonResult);
        UpdateSummary();
    }

    private void OnSyncModeChanged(object? sender, EventArgs e)
    {
        if (_comparisonResult == null)
        {
            return;
        }

        ApplyCurrentMode();
        _diffView.RefreshView();
        UpdateSummary();
    }

    private void OnShowIdenticalChanged(object? sender, EventArgs e)
    {
        _diffView.ShowIdentical = _showIdenticalCheckBox.Checked;
    }

    private void OnHashCheckClicked(object? sender, EventArgs e)
    {
        if (_comparisonResult == null)
        {
            return;
        }

        _statusLabel.Text = "Вычисление хешей...";
        _compareButton.Enabled = false;
        _syncButton.Enabled = false;
        _hashButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;

        ResetCancellationTokenSource();
        _workerService.StartHash(_comparisonResult, _cancellationTokenSource!.Token);
    }

    private void OnResolveConflictsClicked(object? sender, EventArgs e)
    {
        _resolveConflictsMenu.Show(_resolveConflictsButton,
            new(0, _resolveConflictsButton.Height));
    }

    private void OnResolveAllToRightClicked(object? sender, EventArgs e)
    {
        ResolveAllConflicts(SyncAction.CopyToRight);
    }

    private void OnResolveAllToLeftClicked(object? sender, EventArgs e)
    {
        ResolveAllConflicts(SyncAction.CopyToLeft);
    }

    private void OnResolveAllSkipClicked(object? sender, EventArgs e)
    {
        ResolveAllConflicts(SyncAction.Skip);
    }

    private void OnHashCompleted(object? sender, SyncWorkerService.HashResponse? response)
    {
        _progressBar.Style = ProgressBarStyle.Blocks;
        _compareButton.Enabled = true;
        _hashButton.Enabled = true;

        if (response?.Error != null)
        {
            _statusLabel.Text = $"Ошибка вычисления хешей: {response.Error}";
            UpdateSummary();
            return;
        }

        if (response?.Result == null)
        {
            _statusLabel.Text = "Вычисление хешей отменено.";
            UpdateSummary();
            return;
        }

        _diffView.RefreshView();
        UpdateSummary();
        _statusLabel.Text = $"Хеши вычислены за {response.Elapsed.TotalSeconds:F2} с.";
    }

    private void OnShowSizesChanged(object? sender, EventArgs e)
    {
        _diffView.ShowSizes = _showSizesCheckBox.Checked;
    }

    private void OnShowAbsentAsEmptyChanged(object? sender, EventArgs e)
    {
        _diffView.ShowAbsentAsEmpty = _showAbsentAsEmptyCheckBox.Checked;
    }

    private void OnDiffViewActionChanged(object? sender, EventArgs e)
    {
        UpdateSummary();
    }

    private void OnHelpClicked(object? sender, EventArgs e)
    {
        using var helpForm = new SyncHelpForm();
        helpForm.ShowDialog(this);
    }

    private void OnSyncClicked(object? sender, EventArgs e)
    {
        if (_comparisonResult == null)
        {
            return;
        }

        if (_comparisonResult.HasPendingResolution())
        {
            MessageBox.Show(this, "Разрешите все неподтверждённые элементы перед синхронизацией.",
                "Неподтверждённые элементы", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            return;
        }

        var result = MessageBox.Show(this, "Начать синхронизацию?",
            "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            return;
        }

        _syncButton.Enabled = false;
        _compareButton.Enabled = false;
        _progressBar.Style = ProgressBarStyle.Marquee;
        _statusLabel.Text = "Синхронизация...";

        ResetCancellationTokenSource();
        _workerService.StartSync(_comparisonResult, _cancellationTokenSource!.Token);
    }

    private void OnSyncCompleted(object? sender, SyncWorkerService.SyncResponse? response)
    {
        _progressBar.Style = ProgressBarStyle.Blocks;
        _compareButton.Enabled = true;

        if (response?.Error != null)
        {
            _statusLabel.Text = $"Ошибка: {response.Error}";
            return;
        }

        if (response?.Report == null)
        {
            _statusLabel.Text = "Синхронизация отменена.";
            return;
        }

        var report = response.Report;
        _statusLabel.Text = $"Готово за {response.Elapsed.TotalSeconds:F2} с. "
                            + $"Успешно: {report.SuccessCount}, Ошибок: {report.Errors.Count}";

        WriteSyncLog(report);

        if (report.Errors.Count <= 0)
        {
            return;
        }

        var errorList = string.Join(Environment.NewLine,
            report.Errors.Select(e => $"  {e.RelativePath}: {e.Message}"));

        MessageBox.Show(this, $"Ошибки при синхронизации:\n{errorList}",
            "Ошибки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private static void BrowsePath(TextBox target)
    {
        using var dialog = new FolderBrowserDialog();

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
        }
    }

    private static void WriteSyncLog(SyncReport report)
    {
        var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync-log.txt");

        using var writer = new StreamWriter(logPath, true);
        writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация: {report.SuccessCount} успешно, {report.Errors.Count} ошибок");
        report.WriteDetails(writer);
    }

    private static Dictionary<string, string> ParseIni(string[] lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var sep = line.IndexOf('=');

            if (sep > 0)
            {
                result[line[..sep].Trim()] = line[(sep + 1)..];
            }
        }

        return result;
    }

    private void ResolveAllConflicts(SyncAction action)
    {
        if (_comparisonResult == null)
        {
            return;
        }

        var count = _comparisonResult.ResolveAllConflicts(action);
        _diffView.RefreshView();
        UpdateSummary();
        _statusLabel.Text = $"Разрешено элементов: {count}.";
    }

    private void ResetCancellationTokenSource()
    {
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new();
    }

    private SyncMode CurrentMode()
    {
        return _syncModeComboBox.SelectedIndex switch
        {
            0 => SyncMode.LeftToRight,
            1 => SyncMode.RightToLeft,
            2 => SyncMode.Bidirectional,
            _ => SyncMode.LeftToRight,
        };
    }

    private void ApplyCurrentMode()
    {
        if (_comparisonResult == null)
        {
            return;
        }

        _comparisonResult.ApplyMode(CurrentMode());
    }

    private void UpdateSummary()
    {
        if (_comparisonResult == null)
        {
            return;
        }

        var stats = _comparisonResult.GetStatistics();

        _summaryLabel.Text = $"Одинаковых: {stats[ComparisonStatus.Identical]}, "
                             + $"Только слева: {stats[ComparisonStatus.LeftOnly]}, "
                             + $"Только справа: {stats[ComparisonStatus.RightOnly]}, "
                             + $"Изменённых: {stats[ComparisonStatus.Modified]}, "
                             + $"Конфликтов: {stats[ComparisonStatus.Conflict]}";

        var hasPending = _comparisonResult.HasPendingResolution();

        _syncButton.Enabled = !hasPending
                              && stats.Any(kv => kv.Key != ComparisonStatus.Identical && kv.Value > 0);

        _resolveConflictsButton.Enabled = hasPending;
        _hashButton.Enabled = stats[ComparisonStatus.Modified] > 0;
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile))
            {
                return;
            }

            var ini = ParseIni(File.ReadAllLines(SettingsFile));

            _leftPathTextBox.Text = ini.GetValueOrDefault("LeftPath", "");
            _rightPathTextBox.Text = ini.GetValueOrDefault("RightPath", "");
            _exclusionTextBox.Text = ini.GetValueOrDefault("Exclusions", "");

            if (int.TryParse(ini.GetValueOrDefault("SyncMode", "0"), out var mode))
            {
                _syncModeComboBox.SelectedIndex = Math.Clamp(mode, 0, 2);
            }

            if (bool.TryParse(ini.GetValueOrDefault("ShowIdentical"), out var showIdentical))
            {
                _showIdenticalCheckBox.Checked = showIdentical;
            }

            if (bool.TryParse(ini.GetValueOrDefault("ShowSizes"), out var showSizes))
            {
                _showSizesCheckBox.Checked = showSizes;
            }

            if (bool.TryParse(ini.GetValueOrDefault("ShowAbsentAsEmpty"), out var showAbsentAsEmpty))
            {
                _showAbsentAsEmptyCheckBox.Checked = showAbsentAsEmpty;
            }
        }
        catch
        {
        }
    }

    private void SaveSettings()
    {
        try
        {
            var lines = new[]
            {
                $"LeftPath={_leftPathTextBox.Text}",
                $"RightPath={_rightPathTextBox.Text}",
                $"Exclusions={_exclusionTextBox.Text}",
                $"SyncMode={_syncModeComboBox.SelectedIndex}",
                $"ShowIdentical={_showIdenticalCheckBox.Checked}",
                $"ShowSizes={_showSizesCheckBox.Checked}",
                $"ShowAbsentAsEmpty={_showAbsentAsEmptyCheckBox.Checked}",
            };

            File.WriteAllLines(SettingsFile, lines);
        }
        catch
        {
        }
    }
}
