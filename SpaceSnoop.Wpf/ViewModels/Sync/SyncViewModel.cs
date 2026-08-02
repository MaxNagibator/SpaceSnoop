using KeepShell.Services;
using System.ComponentModel;
using System.Windows.Input;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncViewModel : ObservableObject, IPageHeader, IPageStatus
{
    private readonly ISettingsStore _settings;

    [ObservableProperty]
    private string _summaryText = "Сравнение не выполнялось.";

    public SyncViewModel(ISettingsStore settings, IDialogService dialogs, OperationPreferences operations, AgentPreferences agent, ILogger<SyncViewModel> logger, CompareDirectoriesUseCase compare, ExecuteSyncUseCase sync, ToastNotifier notifier, PerformanceMonitor performance)
    {
        _settings = settings;

        Session = new(dialogs, logger, notifier, performance, summary => SummaryText = summary);
        Session.PropertyChanged += OnSessionPropertyChanged;

        Git = new(settings, logger);

        Setup = new(settings, dialogs, operations, () => !IsBusy, message => Session.StatusCaption = message);
        Setup.ProfileSelected += ApplyProfile;

        Ledger = new(Git, () => new(Setup.DirectionIconKind, Setup.DirectionText(), Setup.CurrentMode == SyncMode.Bidirectional));
        Ledger.PropertyChanged += OnLedgerPropertyChanged;

        Operations = new(settings, dialogs, logger, compare, sync, notifier, Setup, Session, Git, Ledger, summary => SummaryText = summary);
        Operations.ComparisonChanged += OnComparisonChanged;
        Operations.ProfileRunCompleted += RaiseProfileRun;

        Setup.PathChanged += Operations.DiscardComparisonIfPathChanged;
        Setup.ModeChanged += Operations.ReapplyMode;

        Rows = new(settings, operations, agent, Operations.CompareContentAsync, AskAgentAbout);
        Rows.ActionsChanged += UpdateSummary;

        _settings.Changed += OnSettingsChanged;
    }

    public event Action<string>? AskAgentRequested;

    public event Action<SyncProfileRun>? ProfileRunCompleted;

    public SyncGitViewModel Git { get; }

    public SyncSessionViewModel Session { get; }

    public SyncSetupViewModel Setup { get; }

    public SyncOperationsViewModel Operations { get; }

    public SyncRowsViewModel Rows { get; }

    public SyncLedgerViewModel Ledger { get; }

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
        Operations.ClearComparison();
        Setup.Apply(profile);

        if (comparison is null)
        {
            Session.StatusCaption = $"Профиль применён: {profile.Name}.";
            return;
        }

        Operations.AdoptComparison(comparison);
        Session.StatusCaption = $"Профиль применён: {profile.Name}. Результат сравнения перенесён.";
    }

    private void OnSettingsChanged(object? sender, string key)
    {
        if (key == SettingsKeys.ScheduleProfiles)
        {
            Setup.Profiles.Load();
        }
        else if (key == SettingsKeys.SyncGroupFolders && Operations.Result is not null && Rows.FlatView)
        {
            Rows.Rebuild();
        }
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

    private void RaiseProfileRun(SyncProfileRun run)
    {
        ProfileRunCompleted?.Invoke(run);
    }

    private void OnComparisonChanged(SyncComparisonChange change)
    {
        switch (change)
        {
            case SyncComparisonChange.Reloaded:
                Rows.Update(Operations.Result, Operations.Outcomes, Operations.DirSizeCache, collapseAll: true);
                UpdateSummary();
                break;

            case SyncComparisonChange.Recomputed:
                Rows.Update(Operations.Result, Operations.Outcomes, Operations.DirSizeCache);
                UpdateSummary();
                break;

            case SyncComparisonChange.Rebuilt:
                Rows.Rebuild();
                UpdateSummary();
                break;

            case SyncComparisonChange.Applied:
                Rows.Update(Operations.Result, Operations.Outcomes, Operations.DirSizeCache);
                Ledger.RefreshAfterSync(Operations.Outcomes);
                break;

            default:
                break;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IsBusy):
                OnPropertyChanged(nameof(IsBusy));
                Setup.NotifyBusyChanged();
                Operations.NotifyBusyChanged();
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
        if (e.PropertyName == nameof(SyncLedgerViewModel.SyncIsPrimary))
        {
            Operations.NotifyPlanChanged();
        }
    }

    private void UpdateSummary()
    {
        Operations.RefreshPending();
        Ledger.Update(Operations.Result, Operations.HashesCompared);

        SummaryText = Operations.Result is null
            ? "Сравнение не выполнялось."
            : $"Одинаковых: {Ledger.IdenticalCount}, "
              + $"только слева: {Ledger.LeftOnlyCount}, "
              + $"только справа: {Ledger.RightOnlyCount}, "
              + $"изменённых: {Ledger.ModifiedCount}, "
              + $"конфликтов: {Ledger.ConflictCount}";
    }
}
