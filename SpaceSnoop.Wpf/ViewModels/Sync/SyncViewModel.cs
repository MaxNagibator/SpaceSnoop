using KeepShell.Services;
using Microsoft.Win32;
using SpaceSnoop.Core.Export;
using SpaceSnoop.Wpf.Diff;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private const long MaxDiffBytes = 5 * 1024 * 1024;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SyncViewModel> _logger;
    private readonly CompareDirectoriesUseCase _compare;
    private readonly ExecuteSyncUseCase _sync;
    private readonly ToastNotifier _notifier;

    private ComparisonResult? _result;
    private SyncReport? _lastReport;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private bool _suppressPersist;
    private bool _gitPromptDeclined;
    private bool _hashesCompared;

    [ObservableProperty]
    private bool _verify = AppDefaults.SyncVerifyDefault;

    [ObservableProperty]
    private string _summaryText = "Сравнение не выполнялось.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToRightCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllToLeftCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResolveAllSkipCommand))]
    private bool _hasPending;

    public SyncViewModel(ISettingsStore settings, IDialogService dialogs, OperationPreferences operations, AgentPreferences agent, ILogger<SyncViewModel> logger, CompareDirectoriesUseCase compare, ExecuteSyncUseCase sync, ToastNotifier notifier, PerformanceMonitor performance)
    {
        _settings = settings;
        _dialogs = dialogs;
        Operations = operations;
        _logger = logger;
        _compare = compare;
        _sync = sync;
        _notifier = notifier;

        Session = new(dialogs, logger, notifier, performance, summary => SummaryText = summary);
        Session.PropertyChanged += OnSessionPropertyChanged;

        Git = new(settings, logger);

        Setup = new(settings, dialogs, operations, () => !IsBusy, message => Session.StatusCaption = message);
        Setup.PathChanged += DiscardComparisonIfPathChanged;
        Setup.ModeChanged += ReapplyMode;
        Setup.ProfileSelected += ApplyProfile;

        Rows = new(settings, operations, agent, CompareContentAsync, AskAgentAbout);
        Rows.ActionsChanged += UpdateSummary;

        Ledger = new(Git, () => new(Setup.DirectionIconKind, Setup.DirectionText(), Setup.CurrentMode == SyncMode.Bidirectional));
        Ledger.PropertyChanged += OnLedgerPropertyChanged;

        LoadSettings();
        _settings.Changed += OnSettingsChanged;
    }

    public event Action<string>? AskAgentRequested;

    public event Action<SyncProfileRun>? ProfileRunCompleted;

    public SyncGitViewModel Git { get; }

    public SyncSessionViewModel Session { get; }

    public SyncSetupViewModel Setup { get; }

    public SyncRowsViewModel Rows { get; }

    public SyncLedgerViewModel Ledger { get; }

    public OperationPreferences Operations { get; }

    public string PageTitle => "Синхронизация";

    public string PageDescription => "Сравнение и синхронизация двух каталогов.";

    public bool IsBusy => Session.IsBusy;

    public string? StatusCaption => Session.StatusCaption;

    public bool IsIndeterminate => Session.IsIndeterminate;

    public double ProgressValue => Session.ProgressValue;

    public double ProgressMax => Session.ProgressMax;

    public ICommand CancelCommand => Session.CancelCommand;

    public void ApplyProfile(SyncProfile profile)
    {
        ApplyProfile(profile, null);
    }

    public void ApplyProfile(SyncProfile profile, ComparisonResult? comparison)
    {
        ClearComparison();
        Setup.Apply(profile);

        if (comparison is null)
        {
            Session.StatusCaption = $"Профиль применён: {profile.Name}.";
            return;
        }

        AdoptComparison(comparison);
        Session.StatusCaption = $"Профиль применён: {profile.Name}. Результат сравнения перенесён.";
    }

    internal static string AddGitExclusion(string exclusions)
    {
        var parts = exclusions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Any(static part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase))
            ? exclusions
            : string.IsNullOrWhiteSpace(exclusions)
                ? ".git"
                : $"{exclusions.TrimEnd()},.git";
    }

    internal Task CompareFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteCompareAsync(cancellationToken);
    }

    internal Task<SyncRunResult?> SyncFromAutomationAsync(CancellationToken cancellationToken)
    {
        return ExecuteSyncAsync(false, cancellationToken);
    }

    internal TimeSpan LastSyncElapsed { get; private set; }

    internal SyncVerifyState LastVerifyState { get; private set; }

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
        var options = new ComparisonExportOptions(Setup.CurrentMode, Setup.CurrentWinner, Setup.Mirror, Setup.Exclusions.Trim());

        return () => SyncPlanExport.Build(result, options, AppInfo.Version, entryLimit);
    }

    internal Func<ComparisonExportModel>? CaptureExportBuilder(int entryLimit)
    {
        if (_result is null)
        {
            return null;
        }

        var result = _result;
        var options = new ComparisonExportOptions(Setup.CurrentMode, Setup.CurrentWinner, Setup.Mirror, Setup.Exclusions.Trim());

        var git = Git.LeftState is null && Git.RightState is null
            ? null
            : new ComparisonExportGit(Git.LeftState, Git.RightState, Git.GitVerdictText);

        var lastSync = _lastReport is null
            ? null
            : new ComparisonExportSync(_lastReport.CopiedCount, _lastReport.DeletedCount, _lastReport.Errors, _lastReport.Mismatches);

        return () => ComparisonExport.Build(result, options, AppInfo.Version, entryLimit) with
        {
            Git = git,
            LastSync = lastSync,
        };
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles)
        {
            Setup.Profiles.Load();
        }
        else if (key == SettingsKeys.SyncGroupFolders && _result is not null && Rows.FlatView)
        {
            Rows.Rebuild();
        }
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

    private async Task CompareContentAsync(FileComparison file)
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

    private void AskAgentAbout(SyncNodeViewModel node)
    {
        AskAgentRequested?.Invoke(ChatQuestion.ForSyncNode(node.RelativePath,
            node.Status,
            node.IsDirectory,
            node.LeftSizeText,
            node.RightSizeText,
            node.DiffReason));
    }

    private void AdoptComparison(ComparisonResult comparison)
    {
        _result = comparison;
        _dirSizeCache = SyncRowsProjector.BuildDirSizeCache(comparison.Root);
        _outcomes = [];
        _hashesCompared = false;
        comparison.ApplyMode(Setup.CurrentMode, Setup.Mirror, Setup.CurrentWinner);
        Rows.Update(_result, _outcomes, _dirSizeCache, collapseAll: true);
        UpdateSummary();
        SummaryText = "Результат сравнения перенесён со страницы «Обзор».";
        _ = ReadGitStateAsync();
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
        return !IsBusy;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CompareAsync()
    {
        return ExecuteCompareAsync(CancellationToken.None);
    }

    private async Task ExecuteCompareAsync(CancellationToken external)
    {
        var left = Setup.LeftPath.Trim();
        var right = Setup.RightPath.Trim();

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
            _dialogs.Warning("Сравнение", "Каталоги совпадают или вложены друг в друга — синхронизация невозможна.");
            return;
        }

        var stopwatch = Stopwatch.StartNew();

        _logger.CompareStarted(left, right);

        var request = new CompareDirectoriesRequest(left, right, Setup.Exclusions, Setup.CurrentMode, Setup.CurrentWinner, Setup.Mirror);

        var prepared = await Session.RunAsync("Сравнение каталогов:", (token, progress) =>
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
        Rows.Update(_result, _outcomes, _dirSizeCache, collapseAll: true);
        UpdateSummary();
        SummaryText = $"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с";
        Session.StatusCaption = SummaryText;

        _logger.CompareFinished(Ledger.Total, (long)stopwatch.Elapsed.TotalMilliseconds);
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

        var updatedExclusions = AddGitExclusion(Setup.Exclusions);

        if (string.Equals(updatedExclusions, Setup.Exclusions, StringComparison.Ordinal))
        {
            return;
        }

        _logger.SyncGitFoldersSkipped(gitFolders);
        Setup.Exclusions = updatedExclusions;
        await CompareAsync();
    }

    private Task ReadGitStateAsync()
    {
        return _result is null
            ? Task.CompletedTask
            : Git.ReadAsync(_result.LeftPath, _result.RightPath, CancellationToken.None);
    }

    private bool CanHash()
    {
        return !IsBusy && _result is not null;
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

        var sizes = await Session.RunAsync("Вычисление хешей:", (token, progress) =>
        {
            var done = 0;
            HashModifiedFiles(result.Root, result.LeftPath, result.RightPath, progress, ref done, token);
            return SyncRowsProjector.BuildDirSizeCache(result.Root);
        }, Ledger.ModifiedCount, CancellationToken.None);

        stopwatch.Stop();

        if (sizes is null)
        {
            return;
        }

        _dirSizeCache = sizes;
        _outcomes = [];
        _hashesCompared = true;
        Rows.Update(_result, _outcomes, _dirSizeCache);
        UpdateSummary();
        SummaryText = $"Хеши вычислены за {stopwatch.Elapsed.TotalSeconds:F2} с";
        Session.StatusCaption = SummaryText;

        _logger.HashFinished((long)stopwatch.Elapsed.TotalMilliseconds);
    }

    private bool CanSync()
    {
        return !IsBusy && _result is not null && !HasPending && Ledger.HasActionableChanges();
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

            var confirm = Ledger.BuildSyncConfirmation(current, hashes);

            if (!await _dialogs.ShowAsync(confirm))
            {
                return;
            }

            if (!ReferenceEquals(confirm.Chosen, hashes))
            {
                break;
            }

            await HashCommand.ExecuteAsync(null);

            if (!Ledger.HasActionableChanges() || _result is not { } hashed || hashed.HasPendingResolution())
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
        var planned = Ledger.CurrentPlan;
        var stopwatch = Stopwatch.StartNew();

        _logger.SyncStarted(Setup.CurrentMode);

        var verify = Verify;

        var request = new ExecuteSyncRequest(result, SyncConflictPolicy.None, SyncDeleteUi.Interactive, verify);

        var report = await Session.RunAsync("Синхронизация:",
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
        Rows.Update(_result, _outcomes, _dirSizeCache);
        Ledger.RefreshAfterSync(_outcomes);
        RaiseProfileRun(null, report, (long)stopwatch.Elapsed.TotalMilliseconds);
        await ReadGitStateAsync();

        var verifyText = SyncLedgerViewModel.DescribeVerify(verify, report);
        var volumeText = report.CopiedBytes > 0 ? $" Перенесено: {SizeFormatter.Format(report.CopiedBytes)}." : string.Empty;
        var rateText = SyncSessionViewModel.DescribeRate("Синхронизация", report, stopwatch.Elapsed);
        SummaryText = $"Готово за {stopwatch.Elapsed.TotalSeconds:F2} с.{volumeText}{rateText} Успешно: {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}{verifyText}";
        Session.StatusCaption = SummaryText;

        var toastVolume = report.CopiedBytes > 0 ? $" · {SizeFormatter.Format(report.CopiedBytes)}" : string.Empty;

        var syncToastMessage = report.Errors.Count > 0
            ? $"Синхронизация: применено {report.SuccessCount:N0}, ошибок: {report.Errors.Count:N0}"
            : report.Mismatches.Count > 0
                ? $"Синхронизация: применено {report.SuccessCount:N0} · расхождений: {report.Mismatches.Count:N0}"
                : $"Синхронизация завершена: применено {report.SuccessCount:N0}{toastVolume}";

        var syncToastSeverity = report.Errors.Count > 0 ? StatusSeverity.Error
            : report.Mismatches.Count > 0 ? StatusSeverity.Warning
            : StatusSeverity.Success;

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
        Rows.Rebuild();
        UpdateSummary();
        Session.StatusCaption = $"Разрешено элементов: {count}.";
    }

    private bool CanExport()
    {
        return !IsBusy && _result is not null;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportComparison()
    {
        if (_result is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Экспорт сравнения",
            Filter = "JSON (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = BuildExportFileName(),
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var model = BuildExportModel(ComparisonExport.DefaultEntryLimit)!;

            File.WriteAllText(dialog.FileName, ComparisonExport.ToJson(model));
            _logger.ComparisonExported(dialog.FileName, model.Entries.Count, model.OmittedEntries);

            var omitted = model.OmittedEntries > 0 ? $", пропущено {model.OmittedEntries:N0}" : string.Empty;
            Session.StatusCaption = $"Сравнение выгружено: {Path.GetFileName(dialog.FileName)}";
            _notifier.Notify($"Сравнение выгружено: записей {model.Entries.Count:N0}{omitted}", StatusSeverity.Success);
        }
        catch (Exception ex)
        {
            _logger.ComparisonExportFailed(ex, dialog.FileName);
            _dialogs.Error("Экспорт сравнения", ex.Message);
        }
    }

    private string BuildExportFileName()
    {
        var trimmed = Setup.LeftPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var raw = Path.GetFileName(trimmed);
        var name = string.Join("_", raw.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

        return $"sync-compare-{(name.Length == 0 ? "root" : name)}-{DateTime.Now:yyyyMMdd-HHmmss}.json";
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IsBusy):
                OnPropertyChanged(nameof(IsBusy));
                Setup.NotifyBusyChanged();
                CompareCommand.NotifyCanExecuteChanged();
                HashCommand.NotifyCanExecuteChanged();
                SyncCommand.NotifyCanExecuteChanged();
                ExportComparisonCommand.NotifyCanExecuteChanged();
                break;

            case nameof(StatusCaption):
            case nameof(IsIndeterminate):
            case nameof(ProgressValue):
            case nameof(ProgressMax):
                OnPropertyChanged(e.PropertyName);
                break;
        }
    }

    private void OnLedgerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SyncLedgerViewModel.SyncIsPrimary))
        {
            return;
        }

        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
        ExportComparisonCommand.NotifyCanExecuteChanged();
    }

    private void ReapplyMode()
    {
        if (_result is null)
        {
            return;
        }

        _result.ApplyMode(Setup.CurrentMode, Setup.Mirror, Setup.CurrentWinner);
        Rows.Rebuild();
        UpdateSummary();
    }

    partial void OnVerifyChanged(bool value)
    {
        Persist(SettingsKeys.SyncVerify, value ? "true" : "false");
    }

    private void RaiseProfileRun(ComparisonResult? comparison, SyncReport? report, long elapsedMs)
    {
        if (Setup.ActiveProfileId is { } id)
        {
            ProfileRunCompleted?.Invoke(new(id, comparison, report, elapsedMs));
        }
    }

    private void DiscardComparisonIfPathChanged()
    {
        if (_result is null)
        {
            return;
        }

        if (!string.Equals(Setup.LeftPath.Trim(), _result.LeftPath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Setup.RightPath.Trim(), _result.RightPath, StringComparison.OrdinalIgnoreCase))
        {
            ClearComparison();
        }
    }

    private void ClearComparison()
    {
        _result = null;
        _lastReport = null;
        _dirSizeCache = null;
        _outcomes = [];
        _hashesCompared = false;
        Rows.Update(null, _outcomes, null);
        Git.Clear();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        HasPending = _result?.HasPendingResolution() ?? false;
        Ledger.Update(_result, _hashesCompared);

        SummaryText = _result is null
            ? "Сравнение не выполнялось."
            : $"Одинаковых: {Ledger.IdenticalCount}, "
              + $"только слева: {Ledger.LeftOnlyCount}, "
              + $"только справа: {Ledger.RightOnlyCount}, "
              + $"изменённых: {Ledger.ModifiedCount}, "
              + $"конфликтов: {Ledger.ConflictCount}";
    }

    private void LoadSettings()
    {
        _suppressPersist = true;

        Verify = _settings.GetBool(SettingsKeys.SyncVerify, AppDefaults.SyncVerifyDefault);

        _suppressPersist = false;
    }

    private void Persist(string key, string value)
    {
        if (_suppressPersist)
        {
            return;
        }

        _settings.SetValue(key, value);
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
