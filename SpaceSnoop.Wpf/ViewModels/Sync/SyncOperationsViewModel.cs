using KeepShell.Services;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncOperationsViewModel : ObservableObject
{
    internal const string CompareCaption = "Сравнение каталогов:";

    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly CompareDirectoriesUseCase _compare;
    private readonly ExecuteSyncUseCase _sync;
    private readonly ToastNotifier _notifier;
    private readonly SyncSetupViewModel _setup;
    private readonly SyncSessionViewModel _session;
    private readonly SyncGitViewModel _git;
    private readonly SyncLedgerViewModel _ledger;
    private readonly Action<string> _reportSummary;

    private ComparisonResult? _result;
    private SyncReport? _lastReport;

    private Dictionary<object, SyncOutcome> _outcomes = [];
    private Dictionary<DirectoryComparison, (long Left, long Right)>? _dirSizeCache;
    private bool _hashesCompared;
    private SyncPlanFreshnessState _planState = new(SyncPlanFreshness.Fresh);

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
        _setup = setup;
        _session = session;
        _git = git;
        _ledger = ledger;
        _reportSummary = reportSummary;
    }

    internal event Action<SyncComparisonChange>? ComparisonChanged;

    internal event Action<SyncProfileRun>? ProfileRunCompleted;

    internal ComparisonResult? Result => _result;

    internal SyncReport? LastReport => _lastReport;

    internal Dictionary<object, SyncOutcome> Outcomes => _outcomes;

    internal Dictionary<DirectoryComparison, (long Left, long Right)>? DirSizeCache => _dirSizeCache;

    internal bool HashesCompared => _hashesCompared;

    internal SyncPlanFreshness PlanFreshness => _planState.Freshness;

    internal TimeSpan LastSyncElapsed { get; private set; }

    internal SyncVerifyState LastVerifyState { get; private set; }

    public void NotifyBusyChanged()
    {
        CompareCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
        SyncCommand.NotifyCanExecuteChanged();
    }

    public void NotifyPlanChanged()
    {
        SyncCommand.NotifyCanExecuteChanged();
        HashCommand.NotifyCanExecuteChanged();
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

        if (!string.Equals(_setup.LeftPath.Trim(), _result.LeftPath, StringComparison.Ordinal)
            || !string.Equals(_setup.RightPath.Trim(), _result.RightPath, StringComparison.Ordinal))
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
        ApplyPlanState(_planState.AfterComparison());
        _git.Clear();
        RaiseComparisonChanged(SyncComparisonChange.Reloaded);
    }

    internal void AdoptComparison(ComparisonResult comparison)
    {
        _result = comparison;
        _dirSizeCache = SyncRowsProjector.BuildDirSizeCache(comparison.Root);
        _outcomes = [];
        _hashesCompared = false;
        ApplyPlanState(_planState.AfterComparison());
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
            built = await Task.Run(() => SyncContentDiff.Build(leftPath, rightPath), CancellationToken.None);
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

        var prepared = await _session.RunAsync(CompareCaption, (token, progress) =>
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
        ApplyPlanState(_planState.AfterComparison());
        RaiseComparisonChanged(SyncComparisonChange.Reloaded);
        var unreadable = _result.IncompleteDirectories();
        var incomplete = SyncPlanNarrative.DescribeIncomplete(_result);
        Report($"Сравнение завершено за {stopwatch.Elapsed.TotalSeconds:F2} с{(incomplete is null ? string.Empty : $". {incomplete}")}");

        if (incomplete is not null)
        {
            _logger.CompareIncomplete(unreadable.Count, unreadable[0]);
            _notifier.Notify(incomplete, StatusSeverity.Warning);
        }

        ReportSkippedLinks(_result);

        _logger.CompareFinished(_ledger.Total, (long)stopwatch.Elapsed.TotalMilliseconds);
        RaiseProfileRun(_result, null, (long)stopwatch.Elapsed.TotalMilliseconds);

        await ReadGitStateAsync();

        if (_result is not null && await _git.OfferToSkipAsync(_result, () => _setup.Exclusions) is { } exclusions)
        {
            _setup.Exclusions = exclusions;
            await CompareAsync();
        }
    }

    private void ReportSkippedLinks(ComparisonResult result)
    {
        var paths = result.SkippedLinks();

        if (paths.Count == 0)
        {
            return;
        }

        _logger.CompareLinksSkipped(paths.Count, paths[0]);
        _notifier.Notify(SyncPlanNarrative.DescribeSkippedLinks(paths), StatusSeverity.Info);
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
            SyncHasher.HashModified(result.Root, result.LeftPath, result.RightPath, progress, ref done, token, _logger);
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
        return !_session.IsBusy
            && _result is not null
            && _planState.IsExecutable
            && !HasPending
            && _ledger.HasActionableChanges();
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

    private void RaiseProfileRun(ComparisonResult? comparison, SyncReport? report, long elapsedMs, SyncVerifyState verify = SyncVerifyState.None)
    {
        if (_setup.ActiveProfileId is { } id)
        {
            ProfileRunCompleted?.Invoke(new(id, comparison, report, elapsedMs, verify));
        }
    }

    private void RaiseComparisonChanged(SyncComparisonChange change)
    {
        ComparisonChanged?.Invoke(change);
    }

    private void ApplyPlanState(SyncPlanFreshnessState state)
    {
        _planState = state;
        _ledger.SetPlanState(state);
        SyncCommand.NotifyCanExecuteChanged();
    }

    private void Report(string summary)
    {
        _reportSummary(summary);
        _session.StatusCaption = summary;
    }

    private sealed record ComparePreparation(ComparisonResult Result, Dictionary<DirectoryComparison, (long Left, long Right)> Sizes);
}
