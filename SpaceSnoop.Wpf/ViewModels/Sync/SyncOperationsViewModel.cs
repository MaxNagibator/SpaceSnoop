using KeepShell.Services;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Diff;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncOperationsViewModel : ObservableObject
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;

    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly CompareDirectoriesUseCase _compare;
    private readonly ExecuteSyncUseCase _sync;
    private readonly ToastNotifier _notifier;
    private readonly IFilePicker _filePicker;
    private readonly SyncSetupViewModel _setup;
    private readonly SyncSessionViewModel _session;
    private readonly SyncGitViewModel _git;
    private readonly SyncLedgerViewModel _ledger;
    private readonly Action<string> _reportSummary;

    private ComparisonResult? _result;
    private SyncReport? _lastReport;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private bool _gitPromptDeclined;
    private bool _hashesCompared;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllSkipCommand))]
    private bool _hasPending;

    internal SyncOperationsViewModel(
        ISettingsStore settings,
        IDialogService dialogs,
        ILogger logger,
        CompareDirectoriesUseCase compare,
        ExecuteSyncUseCase sync,
        ToastNotifier notifier,
        IFilePicker filePicker,
        SyncSetupViewModel setup,
        SyncSessionViewModel session,
        SyncGitViewModel git,
        SyncLedgerViewModel ledger,
        Action<string> reportSummary)
    {
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        _compare = compare;
        _sync = sync;
        _notifier = notifier;
        _filePicker = filePicker;
        _setup = setup;
        _session = session;
        _git = git;
        _ledger = ledger;
        _reportSummary = reportSummary;
    }

    internal event Action<SyncComparisonChange>? ComparisonChanged;

    internal event Action<SyncProfileRun>? ProfileRunCompleted;

    internal ComparisonResult? Result => _result;

    internal Dictionary<object, SyncOutcome> Outcomes => _outcomes;

    internal Dictionary<DirectoryComparison, (long Left, long Right)>? DirSizeCache => _dirSizeCache;

    internal bool HashesCompared => _hashesCompared;

    internal TimeSpan LastSyncElapsed { get; private set; }

    internal SyncVerifyState LastVerifyState { get; private set; }

    internal static string AddGitExclusion(string exclusions)
    {
        var parts = exclusions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Any(static part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)))
        {
            return exclusions;
        }

        return string.IsNullOrWhiteSpace(exclusions) ? ".git" : $"{exclusions.TrimEnd()},.git";
    }

    public void NotifyBusyChanged()
    {
        CompareCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
        ExportComparisonCommand.NotifyCanExecuteChanged();
    }

    public void NotifyPlanChanged()
    {
        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
        ExportComparisonCommand.NotifyCanExecuteChanged();
    }

    public void ReapplyMode()
    {
        if (_result is null)
        {
            return;
        }

        _result.ApplyMode(_setup.CurrentMode, _setup.Mirror, _setup.CurrentWinner);
        RaiseComparisonChanged(SyncComparisonChange.Rebuilt);
    }

    public void DiscardComparisonIfPathChanged()
    {
        if (_result is null)
        {
            return;
        }

        if (!string.Equals(_setup.LeftPath.Trim(), _result.LeftPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_setup.RightPath.Trim(), _result.RightPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearComparison();
        }
    }

    internal void RefreshPending()
    {
        HasPending = _result?.HasPendingResolution() ?? false;
    }

    internal void ClearComparison()
    {
        _result = null;
        _lastReport = null;
        _dirSizeCache = null;
        _outcomes = [];
        _hashesCompared = false;
        _git.Clear();
        RaiseComparisonChanged(SyncComparisonChange.Reloaded);
    }

    internal void AdoptComparison(ComparisonResult comparison)
    {
        _result = comparison;
        _dirSizeCache = SyncRowsProjector.BuildDirSizeCache(comparison.Root);
        _outcomes = [];
        _hashesCompared = false;
        comparison.ApplyMode(_setup.CurrentMode, _setup.Mirror, _setup.CurrentWinner);
        RaiseComparisonChanged(SyncComparisonChange.Reloaded);
        _reportSummary("Результат сравнения перенесён со страницы «Обзор».");
        _ = ReadGitStateAsync();
    }

    internal Task CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteCompareAsync(cancellationToken);
    }

    internal Task<SyncRunResult?> SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteSyncAsync(false, cancellationToken);
    }

    internal ComparisonExportModel? BuildExportModel(int entryLimit)
    {
        return CaptureExportBuilder(entryLimit)?.Invoke();
    }

    internal Func<SyncPlanExportModel>? CapturePlanBuilder(int entryLimit)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var options = new ComparisonExportOptions(_setup.CurrentMode, _setup.CurrentWinner, _setup.Mirror, _setup.Exclusions.Trim());

        return () => SyncPlanExport.Build(result, options, AppInfo.Version, entryLimit);
    }

    internal Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var options = new ComparisonExportOptions(_setup.CurrentMode, _setup.CurrentWinner, _setup.Mirror, _setup.Exclusions.Trim());

        var git = _git.LeftState is null && _git.RightState is null
            ? null
            : new ComparisonExportGit(_git.LeftState, _git.RightState, _git.GitVerdictText);

        var lastSync = _lastReport is null
            ? null
            : new ComparisonExportSync(_lastReport.CopiedCount, _lastReport.DeletedCount, _lastReport.Errors, _lastReport.Mismatches);

        return () => ComparisonExport.Build(result, options, AppInfo.Version, entryLimit) with
        {
            Git = git,
            LastSync = lastSync,
        };
    }

    internal async Task CompareContentAsync(FileComparison file)
    {
        if (_result is null)
        {
            return;
        }

        var leftPath = Path.Combine(_result.LeftPath, file.RelativePath);
        var rightPath = Path.Combine(_result.RightPath, file.RelativePath);

        FileDiffResult built;

        try
        {
            built = await Task.Run(() => BuildContentDiff(leftPath, rightPath), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.ContentCompareFailed(ex, file.RelativePath);
            _dialogs.Error("Сравнение содержимого", ex.Message);
            return;
        }

        _logger.ContentCompareOpened(file.RelativePath, built.Added, built.Removed);

        var dialog = new FileDiffDialogViewModel(_settings, file, leftPath, rightPath, built.Lines, built.Added, built.Removed, built.Unavailable);
        await _dialogs.ShowAsync(dialog);
    }

    private static FileDiffResult BuildContentDiff(string leftPath, string rightPath)
    {
        var leftInfo = new FileInfo(leftPath);
        var rightInfo = new FileInfo(rightPath);

        if (!leftInfo.Exists && !rightInfo.Exists)
        {
            throw new InvalidOperationException($"Файл не найден ни с одной стороны: {leftPath}");
        }

        if (DiffUnavailable(leftInfo, out var reason) || DiffUnavailable(rightInfo, out reason))
        {
            return FileDiffResult.Unreadable(reason);
        }

        var lines = TextDiff.Compute(ReadLines(leftInfo), ReadLines(rightInfo));
        var added = lines.Count(static l => l.Kind == DiffLineKind.Added);
        var removed = lines.Count(static l => l.Kind == DiffLineKind.Removed);
        return new(lines, added, removed, null);

        static string[] ReadLines(FileInfo info)
        {
            return info.Exists ? File.ReadAllLines(info.FullName) : [];
        }
    }

    // TODO: бинарь определяем по NUL-байту; кодировку доверяем File.ReadAllLines (BOM → UTF-8)
    private static bool DiffUnavailable(FileInfo info, out string reason)
    {
        if (!info.Exists)
        {
            reason = string.Empty;
            return false;
        }

        if (info.Length > MaxDiffBytes)
        {
            reason = "Файл велик для построчного сравнения (> 5 МБ) – показано только сводное различие.";
            return true;
        }

        if (Array.IndexOf(File.ReadAllBytes(info.FullName), (byte)0) >= 0)
        {
            reason = "Файл выглядит двоичным – построчное сравнение недоступно, показано сводное различие.";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static int CountGitDirectories(DirectoryComparison dir)
    {
        var count = 0;

        foreach (var sub in dir.SubDirectories)
        {
            if (string.Equals(sub.Name, ".git", StringComparison.OrdinalIgnoreCase))
            {
                count++;
                continue;
            }

            count += CountGitDirectories(sub);
        }

        return count;
    }

    private void HashModifiedFiles(DirectoryComparison dir, string leftBase, string rightBase, IProgress<OperationProgress> progress, ref int done, CancellationToken token)
    {
        foreach (var file in dir.Files.Where(static f => f.Status == ComparisonStatus.Modified))
        {
            token.ThrowIfCancellationRequested();

            var leftPath = Path.Combine(leftBase, file.RelativePath);
            var rightPath = Path.Combine(rightBase, file.RelativePath);

            try
            {
                file.LeftHash = FileHasher.ComputeHash(leftPath, token);
                file.RightHash = FileHasher.ComputeHash(rightPath, token);

                if (file.LeftHash == file.RightHash)
                {
                    file.Status = ComparisonStatus.Identical;
                    file.Action = SyncAction.Skip;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.HashFileFailed(ex, leftPath);
            }

            progress.Report(new(++done, file.RelativePath));
        }

        foreach (var sub in dir.SubDirectories)
        {
            HashModifiedFiles(sub, leftBase, rightBase, progress, ref done, token);
        }
    }

    private void WriteSyncLog(SyncReport report, bool interactive = true)
    {
        var origin = interactive ? string.Empty : " (запуск агентом через MCP)";

        try
        {
            SyncLog.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Синхронизация{origin}: {report.SuccessCount} успешно, {report.Errors.Count} ошибок", report);
        }
        catch (Exception ex)
        {
            _logger.SyncLogWriteFailed(ex);
        }
    }

    private bool CanRun()
    {
        return !_session.IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CompareAsync()
    {
        return ExecuteCompareAsync(CancellationToken.None);
    }

    private async Task ExecuteCompareAsync(CancellationToken external)
    {
        var left = _setup.LeftPath.Trim();
        var right = _setup.RightPath.Trim();

        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            _dialogs.Warning("Сравнение", "Укажите обе директории.");
            return;
        }

        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            _dialogs.Warning("Сравнение", "Одна из директорий не существует.");
            return;
        }

        if (SyncProfile.PathsOverlap(left, right))
        {
            _dialogs.Warning("Сравнение", "Каталоги совпадают или вложены друг в друга – синхронизация невозможна.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        _logger.CompareStarted(left, right);

        var request = new CompareDirectoriesRequest(left, right, _setup.Exclusions, _setup.CurrentMode, _setup.CurrentWinner, _setup.Mirror);

        var prepared = await _session.RunAsync("Сравнение каталогов:", (token, progress) =>
        {
            var compared = _compare.Execute(request, token, progress);
            return new ComparePreparation(compared, SyncRowsProjector.BuildDirSizeCache(compared.Root));
        }, external: external);

        stopwatch.Stop();

        if (prepared is null)
        {
            return;
        }

        _result = prepared.Result;
        _dirSizeCache = prepared.Sizes;
        _outcomes = [];
        _hashesCompared = false;
        RaiseComparisonChanged(SyncComparisonChange.Reloaded);
        Report($"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с");

        _logger.CompareFinished(_ledger.Total, (long)stopwatch.Elapsed.TotalMilliseconds);
        RaiseProfileRun(_result, null, (long)stopwatch.Elapsed.TotalMilliseconds);

        await ReadGitStateAsync();
        await OfferToSkipGitAsync();
    }

    private async Task OfferToSkipGitAsync()
    {
        if (_result is null || _gitPromptDeclined)
        {
            return;
        }

        var gitFolders = CountGitDirectories(_result.Root);

        if (gitFolders == 0)
        {
            return;
        }

        var choice = _settings.GetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Ask);

        if (choice == GitFolderPromptChoice.Keep)
        {
            return;
        }

        if (choice == GitFolderPromptChoice.Ask)
        {
            var prompt = new GitFolderPromptViewModel(gitFolders);
            var skip = await _dialogs.ShowAsync(prompt);

            if (prompt.Choice != GitFolderPromptChoice.Ask)
            {
                _settings.SetEnum(SettingsKeys.SyncGitFolders, prompt.Choice);
            }

            if (!skip)
            {
                _gitPromptDeclined = true;
                return;
            }
        }

        var updatedExclusions = AddGitExclusion(_setup.Exclusions);

        if (string.Equals(updatedExclusions, _setup.Exclusions, StringComparison.Ordinal))
        {
            return;
        }

        _logger.SyncGitFoldersSkipped(gitFolders);
        _setup.Exclusions = updatedExclusions;
        await CompareAsync();
    }

    private Task ReadGitStateAsync()
    {
        return _result is null
            ? Task.CompletedTask
            : _git.ReadAsync(_result.LeftPath, _result.RightPath, CancellationToken.None);
    }

    private bool CanHash()
    {
        return !_session.IsBusy && _result is not null;
    }

    [RelayCommand(CanExecute = nameof(CanHash))]
    private async Task HashAsync()
    {
        if (_result is null)
        {
            return;
        }

        var result = _result;
        var stopwatch = Stopwatch.StartNew();

        _logger.HashStarted();

        var sizes = await _session.RunAsync("Вычисление хешей:", (token, progress) =>
        {
            var done = 0;
            HashModifiedFiles(result.Root, result.LeftPath, result.RightPath, progress, ref done, token);
            return SyncRowsProjector.BuildDirSizeCache(result.Root);
        }, _ledger.ModifiedCount, CancellationToken.None);

        stopwatch.Stop();

        if (sizes is null)
        {
            return;
        }

        _dirSizeCache = sizes;
        _outcomes = [];
        _hashesCompared = true;
        RaiseComparisonChanged(SyncComparisonChange.Recomputed);
        Report($"Хеши вычислены за {stopwatch.Elapsed.TotalSeconds:F2} с");

        _logger.HashFinished((long)stopwatch.Elapsed.TotalMilliseconds);
    }

    private bool CanSync()
    {
        return !_session.IsBusy && _result is not null && !HasPending && _ledger.HasActionableChanges();
    }

    [RelayCommand(CanExecute = nameof(CanSync))]
    private async Task SyncAsync()
    {
        if (_result is null)
        {
            return;
        }

        if (_result.HasPendingResolution())
        {
            _dialogs.Warning("Неподтверждённые элементы", "Разрешите все неподтверждённые элементы перед синхронизацией.");
            return;
        }

        var hashes = new ConfirmChoice("Сверить хеши", ConfirmChoiceKind.Secondary);

        while (true)
        {
            if (_result is not { } current)
            {
                return;
            }

            var confirm = _ledger.BuildSyncConfirmation(current, hashes);

            if (!await _dialogs.ShowAsync(confirm))
            {
                return;
            }

            if (!ReferenceEquals(confirm.Chosen, hashes))
            {
                break;
            }

            await HashCommand.ExecuteAsync(null);

            if (!_ledger.HasActionableChanges() || _result is not { } hashed || hashed.HasPendingResolution())
            {
                return;
            }
        }

        await ExecuteSyncAsync(true, CancellationToken.None);
    }

    private async Task<SyncRunResult?> ExecuteSyncAsync(bool interactive, CancellationToken external = default)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var planned = _ledger.CurrentPlan;
        var stopwatch = Stopwatch.StartNew();

        _logger.SyncStarted(_setup.CurrentMode);

        var verify = _setup.Verify;

        var request = new ExecuteSyncRequest(result, SyncConflictPolicy.None, SyncDeleteUi.Interactive, verify);

        var report = await _session.RunAsync("Синхронизация:",
            (token, progress) => _sync.Execute(request, token, progress),
            planned.Total,
            external,
            planned.CopyBytes);

        stopwatch.Stop();

        if (report is null)
        {
            return null;
        }

        LastSyncElapsed = stopwatch.Elapsed;
        LastVerifyState = SyncLedgerViewModel.ResolveVerify(verify, report);

        _logger.SyncFinished(report.SuccessCount, report.Errors.Count, (long)stopwatch.Elapsed.TotalMilliseconds);

        if (verify)
        {
            _logger.SyncVerified(report.Applied.Count, report.Mismatches.Count);
        }

        WriteSyncLog(report, interactive);
        _lastReport = report;
        _outcomes = SyncOutcomes.Build(result, report.Errors, report.Mismatches);
        RaiseComparisonChanged(SyncComparisonChange.Applied);
        RaiseProfileRun(null, report, (long)stopwatch.Elapsed.TotalMilliseconds);
        await ReadGitStateAsync();

        var verifyText = SyncLedgerViewModel.DescribeVerify(verify, report);
        var volumeText = report.CopiedBytes > 0 ? $" Перенесено: {SizeFormatter.Format(report.CopiedBytes)}." : string.Empty;
        var rateText = SyncSessionViewModel.DescribeRate("Синхронизация", report, stopwatch.Elapsed);
        Report($"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с.{volumeText}{rateText} Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}");

        var toastVolume = report.CopiedBytes > 0 ? $" · {SizeFormatter.Format(report.CopiedBytes)}" : string.Empty;

        var syncToastMessage = report switch
        {
            { Errors.Count: > 0 } => $"Синхронизация: применено {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}",
            { Mismatches.Count: > 0 } => $"Синхронизация: применено {report.SuccessCount:N0} · расхождений: {report.Mismatches.Count:N0}",
            _ => $"Синхронизация завершена: применено {report.SuccessCount:N0}{toastVolume}",
        };

        var syncToastSeverity = report switch
        {
            { Errors.Count: > 0 } => StatusSeverity.Error,
            { Mismatches.Count: > 0 } => StatusSeverity.Warning,
            _ => StatusSeverity.Success,
        };

        _notifier.Notify(syncToastMessage, syncToastSeverity);

        if (interactive)
        {
            ShowSyncOutcome(report);
        }

        return new(report, stopwatch.Elapsed, LastVerifyState);
    }

    private void ShowSyncOutcome(SyncReport report)
    {
        if (report.Errors.Count > 0)
        {
            const int MaxShown = 20;
            var list = string.Join(Environment.NewLine, report.Errors.Take(MaxShown).Select(e => $"  {e.RelativePath}: {e.Message}"));

            if (report.Errors.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Errors.Count - MaxShown}";
            }

            _dialogs.Warning("Ошибки", $"Ошибки при синхронизации:{Environment.NewLine}{list}");
        }
        else if (report.Mismatches.Count > 0)
        {
            const int MaxShown = 20;
            var list = string.Join(Environment.NewLine, report.Mismatches.Take(MaxShown).Select(m => $"  {m.RelativePath}: {m.Reason}"));

            if (report.Mismatches.Count > MaxShown)
            {
                list += $"{Environment.NewLine}  …и ещё {report.Mismatches.Count - MaxShown}";
            }

            _dialogs.Warning("Расхождения после синхронизации", $"После применения проверка нашла расхождения:{Environment.NewLine}{list}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllToRight()
    {
        ResolveAll(SyncAction.CopyToRight);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllToLeft()
    {
        ResolveAll(SyncAction.CopyToLeft);
    }

    [RelayCommand(CanExecute = nameof(HasPending))]
    private void ResolveAllSkip()
    {
        ResolveAll(SyncAction.Skip);
    }

    private void ResolveAll(SyncAction action)
    {
        if (_result is null)
        {
            return;
        }

        var count = _result.ResolveAllConflicts(action);
        RaiseComparisonChanged(SyncComparisonChange.Rebuilt);
        _session.StatusCaption = $"Разрешено элементов: {count}.";
    }

    private bool CanExport()
    {
        return !_session.IsBusy && _result is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportComparison()
    {
        if (_result is null)
        {
            return;
        }

        var path = _filePicker.SaveFile(new FileSaveRequest("Экспорт сравнения", "JSON (*.json)|*.json")
        {
            DefaultExtension = ".json",
            FileName = BuildExportFileName(),
        });

        if (path is null)
        {
            return;
        }

        try
        {
            var model = BuildExportModel(ComparisonExport.DefaultEntryLimit)!;

            File.WriteAllText(path, ComparisonExport.ToJson(model));
            _logger.ComparisonExported(path, model.Entries.Count, model.OmittedEntries);

            var omitted = model.OmittedEntries > 0 ? $", пропущено {model.OmittedEntries:N0}" : string.Empty;
            _session.StatusCaption = $"Сравнение выгружено: {Path.GetFileName(path)}";
            _notifier.Notify($"Сравнение выгружено: записей {model.Entries.Count:N0}{omitted}", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.ComparisonExportFailed(ex, path);
            _dialogs.Error("Экспорт сравнения", ex.Message);
        }
    }

    private string BuildExportFileName()
    {
        var trimmed = _setup.LeftPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var raw = Path.GetFileName(trimmed);
        var name = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        return $"sync-compare-{(name.Length == 0 ? "root" : name)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    }

    private void RaiseProfileRun(ComparisonResult? comparison, SyncReport? report, long elapsedMs)
    {
        if (_setup.ActiveProfileId is { } id)
        {
            ProfileRunCompleted?.Invoke(new(id, comparison, report, elapsedMs));
        }
    }

    private void RaiseComparisonChanged(SyncComparisonChange change)
    {
        ComparisonChanged?.Invoke(change);
    }

    private void Report(string summary)
    {
        _reportSummary(summary);
        _session.StatusCaption = summary;
    }

    private sealed record ComparePreparation(ComparisonResult Result, Dictionary<DirectoryComparison, (long Left, long Right)> Sizes);

    private sealed record FileDiffResult(IReadOnlyList<DiffLine> Lines, int Added, int Removed, string? Unavailable)
    {
        public static FileDiffResult Unreadable(string reason)
        {
            return new([], 0, 0, reason);
        }
    }
}
