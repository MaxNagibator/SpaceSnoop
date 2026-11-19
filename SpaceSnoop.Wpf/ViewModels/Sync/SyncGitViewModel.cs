using KeepShell.Services;
using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Git;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncGitViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;
    private readonly GitService _git = new();

    private string? _leftPath;
    private string? _rightPath;
    private CancellationTokenSource? _read;
    private int _historyGeneration;
    private bool _gitHistoryLoaded;
    private bool _gitPromptDeclined;

    [ObservableProperty]
    private int _gitHistoryCount;

    public event Action? StateChanged;

    public static int[] GitHistoryCounts { get; } = [4, 8, 16, 32];

    public bool HasGit => LeftState is not null || RightState is not null;

    public bool LeftIsRepo => LeftState is not null;

    public bool RightIsRepo => RightState is not null;

    public string LeftGitBranch => SyncGitText.FormatBranch(LeftState);

    public string RightGitBranch => SyncGitText.FormatBranch(RightState);

    public string LeftGitHead => SyncGitText.FormatHead(LeftState);

    public string RightGitHead => SyncGitText.FormatHead(RightState);

    public string LeftGitDirty => SyncGitText.FormatDirty(LeftState);

    public string RightGitDirty => SyncGitText.FormatDirty(RightState);

    public bool LeftGitIsDirty => LeftState?.IsDirty == true;

    public bool RightGitIsDirty => RightState?.IsDirty == true;

    public string LeftGitUpstream => SyncGitText.FormatUpstream(LeftState);

    public string RightGitUpstream => SyncGitText.FormatUpstream(RightState);

    public bool GitInSync =>
        LeftState is not null
        && RightState is not null
        && LeftState.HasCommits
        && RightState.HasCommits
        && string.Equals(LeftState.Oid, RightState.Oid, StringComparison.OrdinalIgnoreCase)
        && !LeftState.IsDirty
        && !RightState.IsDirty;

    public PackIconLucideKind GitVerdictIconKind
    {
        get
        {
            if (GitInSync)
            {
                return PackIconLucideKind.Check;
            }

            return GitNewerSign switch
            {
                < 0 => PackIconLucideKind.ArrowLeft,
                > 0 => PackIconLucideKind.ArrowRight,
                _ => PackIconLucideKind.GitCompareArrows,
            };
        }
    }

    public bool GitShowsNewer => GitNewerSign != 0;

    public string GitVerdictText
    {
        get
        {
            if (LeftState is null || RightState is null)
            {
                return "одна сторона не репозиторий";
            }

            if (!LeftState.HasCommits || !RightState.HasCommits)
            {
                return SyncGitText.NoCommits;
            }

            if (!string.Equals(LeftState.Oid, RightState.Oid, StringComparison.OrdinalIgnoreCase))
            {
                var newer = DescribeNewer(LeftState.CommittedAt, RightState.CommittedAt);

                return newer.Length == 0 ? "разные коммиты" : $"разные коммиты, {newer}";
            }

            return LeftState.IsDirty || RightState.IsDirty ? "тот же коммит, есть изменения" : "синхронны";
        }
    }

    public IReadOnlyList<GitCommit> LeftGitLog { get; private set; } = [];

    public IReadOnlyList<GitCommit> RightGitLog { get; private set; } = [];

    public bool GitHistoryExpanded { get; private set; }

    public PackIconLucideKind GitHistoryIconKind => GitHistoryExpanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;

    public bool LeftGitLogEmpty => _gitHistoryLoaded && LeftGitLog.Count == 0;

    public bool RightGitLogEmpty => _gitHistoryLoaded && RightGitLog.Count == 0;

    public string LeftGitLogEmptyText => LeftState is null ? "не репозиторий" : SyncGitText.NoCommits;

    public string RightGitLogEmptyText => RightState is null ? "не репозиторий" : SyncGitText.NoCommits;

    public string? GitTooltip
    {
        get
        {
            if (LeftState is not { HasCommits: true } left
                || RightState is not { HasCommits: true } right
                || string.Equals(left.Oid, right.Oid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"Коммит слева: {FormatStamp(left.CommittedAt?.LocalDateTime)}, справа: {FormatStamp(right.CommittedAt?.LocalDateTime)}.{Environment.NewLine}"
                + "«Новее» – по дате коммита, не по истории веток.";
        }
    }

    internal GitRepoState? LeftState { get; private set; }

    internal GitRepoState? RightState { get; private set; }

    internal int GitNewerSign
    {
        get
        {
            if (LeftState is not { HasCommits: true } left
                || RightState is not { HasCommits: true } right
                || string.Equals(left.Oid, right.Oid, StringComparison.OrdinalIgnoreCase)
                || left.CommittedAt is not { } l
                || right.CommittedAt is not { } r
                || l == r)
            {
                return 0;
            }

            return l > r ? -1 : 1;
        }
    }

    public SyncGitViewModel(ISettingsStore settings, IDialogService dialogs, ILogger logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
        _gitHistoryCount = settings.GetInt(SettingsKeys.SyncGitHistoryCount, AppDefaults.GitHistoryCountDefault);
    }

    // TODO: «новее» по дате коммита, не по предкам; ancestry-вердикт требует общего хранилища объектов (cross-repo merge-base)
    internal static string DescribeNewer(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is not { } l || right is not { } r || l == r)
        {
            return string.Empty;
        }

        var side = l > r ? "слева новее" : "справа новее";

        return $"{side} на {FormatAge((l - r).Duration())}";
    }

    internal static string FormatStamp(DateTime? value)
    {
        return value is { } stamp ? stamp.ToString("yyyy-MM-dd HH:mm") : "–";
    }

    internal static string FormatAge(DateTime value)
    {
        var days = (int)(DateTime.Now.Date - value.Date).TotalDays;

        if (days <= 0)
        {
            return "сегодня";
        }

        if (days == 1)
        {
            return "вчера";
        }

        var word = days % 100 is >= 11 and <= 14
            ? "дней"
            : (days % 10) switch
            {
                1 => "день",
                2 or 3 or 4 => "дня",
                _ => "дней",
            };

        return $"{days:N0} {word} назад";
    }

    internal async Task ReadAsync(string left, string right, CancellationToken cancellationToken)
    {
        CancelRead();
        using var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _read = read;
        _leftPath = left;
        _rightPath = right;

        try
        {
            GitRepoState? leftGit;
            GitRepoState? rightGit;

            try
            {
                (leftGit, rightGit) = await Task.Run(
                    async () => (await _git.ReadAsync(left, read.Token), await _git.ReadAsync(right, read.Token)),
                    read.Token);
            }
            catch (Exception ex)
            {
                if (ReferenceEquals(_read, read))
                {
                    _logger.GitStateFailed(ex.Unwrap());
                    Clear();
                }

                return;
            }

            if (!ReferenceEquals(_read, read))
            {
                return;
            }

            (LeftState, RightState) = (leftGit, rightGit);

            if (LeftState is not null || RightState is not null)
            {
                _logger.GitStateRead(SyncGitText.FormatBranch(LeftState), SyncGitText.FormatBranch(RightState));
            }

            ResetHistory();
            NotifyChanged();

            if (GitHistoryExpanded)
            {
                await LoadHistoryAsync();
            }
        }
        finally
        {
            if (ReferenceEquals(_read, read))
            {
                _read = null;
            }
        }
    }

    internal void Clear()
    {
        CancelRead();
        _leftPath = null;
        _rightPath = null;
        LeftState = null;
        RightState = null;
        GitHistoryExpanded = false;
        ResetHistory();
        NotifyChanged();
    }

    internal async Task<string?> OfferToSkipAsync(ComparisonResult result, Func<string> exclusions)
    {
        if (_gitPromptDeclined)
        {
            return null;
        }

        var gitFolders = CountGitDirectories(result.Root);

        if (gitFolders == 0)
        {
            return null;
        }

        var choice = _settings.GetEnum(SettingsKeys.SyncGitFolders, GitFolderPromptChoice.Ask);

        if (choice == GitFolderPromptChoice.Keep)
        {
            return null;
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
                return null;
            }
        }

        var current = exclusions();
        var updated = AddGitExclusion(current);

        if (string.Equals(updated, current, StringComparison.Ordinal))
        {
            return null;
        }

        _logger.SyncGitFoldersSkipped(gitFolders);

        return updated;
    }

    internal static string AddGitExclusion(string exclusions)
    {
        var parts = exclusions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Any(static part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase)))
        {
            return exclusions;
        }

        return string.IsNullOrWhiteSpace(exclusions) ? ".git" : $"{exclusions.TrimEnd()},.git";
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

    private static string FormatAge(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return $"{(int)span.TotalDays} дн.";
        }

        if (span.TotalHours >= 1)
        {
            return $"{(int)span.TotalHours} ч.";
        }

        return span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} мин." : "<1 мин.";
    }

    [RelayCommand]
    private async Task ToggleGitHistoryAsync()
    {
        GitHistoryExpanded = !GitHistoryExpanded;
        OnPropertyChanged(nameof(GitHistoryExpanded));
        OnPropertyChanged(nameof(GitHistoryIconKind));

        if (GitHistoryExpanded && !_gitHistoryLoaded)
        {
            await LoadHistoryAsync();
        }
    }

    private async Task LoadHistoryAsync()
    {
        if (_leftPath is not { } left || _rightPath is not { } right)
        {
            return;
        }

        var count = GitHistoryCount;
        var generation = ++_historyGeneration;
        IReadOnlyList<GitCommit> leftLog;
        IReadOnlyList<GitCommit> rightLog;

        try
        {
            (leftLog, rightLog) = await Task.Run(
                async () => (await _git.ReadHistoryAsync(left, count, CancellationToken.None), await _git.ReadHistoryAsync(right, count, CancellationToken.None)),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.GitStateFailed(ex.Unwrap());
            leftLog = [];
            rightLog = [];
        }

        if (generation != _historyGeneration)
        {
            return;
        }

        (LeftGitLog, RightGitLog) = (leftLog, rightLog);
        _gitHistoryLoaded = true;
        NotifyChanged();
    }

    private void CancelRead()
    {
        _historyGeneration++;
        var read = _read;
        _read = null;
        read?.Cancel();
    }

    private void ResetHistory()
    {
        LeftGitLog = [];
        RightGitLog = [];
        _gitHistoryLoaded = false;
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(HasGit));
        OnPropertyChanged(nameof(LeftIsRepo));
        OnPropertyChanged(nameof(RightIsRepo));
        OnPropertyChanged(nameof(LeftGitBranch));
        OnPropertyChanged(nameof(RightGitBranch));
        OnPropertyChanged(nameof(LeftGitHead));
        OnPropertyChanged(nameof(RightGitHead));
        OnPropertyChanged(nameof(LeftGitDirty));
        OnPropertyChanged(nameof(RightGitDirty));
        OnPropertyChanged(nameof(LeftGitIsDirty));
        OnPropertyChanged(nameof(RightGitIsDirty));
        OnPropertyChanged(nameof(LeftGitUpstream));
        OnPropertyChanged(nameof(RightGitUpstream));
        OnPropertyChanged(nameof(GitInSync));
        OnPropertyChanged(nameof(GitShowsNewer));
        OnPropertyChanged(nameof(GitVerdictIconKind));
        OnPropertyChanged(nameof(GitVerdictText));
        OnPropertyChanged(nameof(GitTooltip));
        OnPropertyChanged(nameof(LeftGitLog));
        OnPropertyChanged(nameof(RightGitLog));
        OnPropertyChanged(nameof(GitHistoryExpanded));
        OnPropertyChanged(nameof(GitHistoryIconKind));
        OnPropertyChanged(nameof(LeftGitLogEmpty));
        OnPropertyChanged(nameof(RightGitLogEmpty));
        OnPropertyChanged(nameof(LeftGitLogEmptyText));
        OnPropertyChanged(nameof(RightGitLogEmptyText));
        StateChanged?.Invoke();
    }

    partial void OnGitHistoryCountChanged(int value)
    {
        _settings.SetValue(SettingsKeys.SyncGitHistoryCount, value.ToString());

        _gitHistoryLoaded = false;

        if (GitHistoryExpanded && HasGit)
        {
            _ = LoadHistoryAsync();
        }
    }
}
