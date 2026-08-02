using MahApps.Metro.IconPacks;
using SpaceSnoop.Core.Git;

namespace SpaceSnoop.Wpf.ViewModels.Sync;

public sealed partial class SyncGitViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ILogger _logger;
    private readonly GitService _git = new();

    private GitRepoState? _leftGit;
    private GitRepoState? _rightGit;
    private string? _leftPath;
    private string? _rightPath;
    private bool _gitHistoryLoaded;

    [ObservableProperty]
    private int _gitHistoryCount;

    public SyncGitViewModel(ISettingsStore settings, ILogger logger)
    {
        _settings = settings;
        _logger = logger;
        _gitHistoryCount = settings.GetInt(SettingsKeys.SyncGitHistoryCount, AppDefaults.GitHistoryCountDefault);
    }

    public event Action? StateChanged;

    public static int[] GitHistoryCounts { get; } = [4, 8, 16, 32];

    public bool HasGit => _leftGit is not null || _rightGit is not null;

    public bool LeftIsRepo => _leftGit is not null;

    public bool RightIsRepo => _rightGit is not null;

    public string LeftGitBranch => FormatBranch(_leftGit);

    public string RightGitBranch => FormatBranch(_rightGit);

    public string LeftGitHead => FormatHead(_leftGit);

    public string RightGitHead => FormatHead(_rightGit);

    public string LeftGitDirty => FormatDirty(_leftGit);

    public string RightGitDirty => FormatDirty(_rightGit);

    public bool LeftGitIsDirty => _leftGit?.IsDirty == true;

    public bool RightGitIsDirty => _rightGit?.IsDirty == true;

    public string LeftGitUpstream => FormatUpstream(_leftGit);

    public string RightGitUpstream => FormatUpstream(_rightGit);

    public bool GitInSync =>
        _leftGit is not null
        && _rightGit is not null
        && _leftGit.HasCommits
        && _rightGit.HasCommits
        && string.Equals(_leftGit.Oid, _rightGit.Oid, StringComparison.OrdinalIgnoreCase)
        && !_leftGit.IsDirty
        && !_rightGit.IsDirty;

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
            if (_leftGit is null || _rightGit is null)
            {
                return "одна сторона не репозиторий";
            }

            if (!_leftGit.HasCommits || !_rightGit.HasCommits)
            {
                return "нет коммитов";
            }

            if (!string.Equals(_leftGit.Oid, _rightGit.Oid, StringComparison.OrdinalIgnoreCase))
            {
                var newer = DescribeNewer(_leftGit.CommittedAt, _rightGit.CommittedAt);

                return newer.Length == 0 ? "разные коммиты" : $"разные коммиты, {newer}";
            }

            return _leftGit.IsDirty || _rightGit.IsDirty ? "тот же коммит, есть изменения" : "синхронны";
        }
    }

    public IReadOnlyList<GitCommit> LeftGitLog { get; private set; } = [];

    public IReadOnlyList<GitCommit> RightGitLog { get; private set; } = [];

    public bool GitHistoryExpanded { get; private set; }

    public PackIconLucideKind GitHistoryIconKind => GitHistoryExpanded ? PackIconLucideKind.ChevronUp : PackIconLucideKind.ChevronDown;

    public bool LeftGitLogEmpty => _gitHistoryLoaded && LeftGitLog.Count == 0;

    public bool RightGitLogEmpty => _gitHistoryLoaded && RightGitLog.Count == 0;

    public string LeftGitLogEmptyText => _leftGit is null ? "не репозиторий" : "нет коммитов";

    public string RightGitLogEmptyText => _rightGit is null ? "не репозиторий" : "нет коммитов";

    public string? GitTooltip
    {
        get
        {
            if (_leftGit is not { HasCommits: true } left
                || _rightGit is not { HasCommits: true } right
                || string.Equals(left.Oid, right.Oid, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return $"Коммит слева: {FormatStamp(left.CommittedAt?.LocalDateTime)}, справа: {FormatStamp(right.CommittedAt?.LocalDateTime)}.{Environment.NewLine}"
                   + "«Новее» – по дате коммита, не по истории веток.";
        }
    }

    internal GitRepoState? LeftState => _leftGit;

    internal GitRepoState? RightState => _rightGit;

    internal int GitNewerSign
    {
        get
        {
            if (_leftGit is not { HasCommits: true } left
                || _rightGit is not { HasCommits: true } right
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

        var word = (days % 100) is >= 11 and <= 14
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
        _leftPath = left;
        _rightPath = right;

        try
        {
            (_leftGit, _rightGit) = await Task.Run(
                async () => (await _git.ReadAsync(left, cancellationToken), await _git.ReadAsync(right, cancellationToken)),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.GitStateFailed(ex.Unwrap());
            Clear();
            return;
        }

        if (_leftGit is not null || _rightGit is not null)
        {
            _logger.GitStateRead(FormatBranch(_leftGit), FormatBranch(_rightGit));
        }

        ResetHistory();
        NotifyChanged();

        if (GitHistoryExpanded)
        {
            await LoadHistoryAsync();
        }
    }

    internal void Clear()
    {
        _leftPath = null;
        _rightPath = null;
        _leftGit = null;
        _rightGit = null;
        GitHistoryExpanded = false;
        ResetHistory();
        NotifyChanged();
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

    private static string FormatBranch(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        return git.IsDetached ? "detached" : git.Branch;
    }

    private static string FormatHead(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        if (!git.HasCommits)
        {
            return "нет коммитов";
        }

        return string.IsNullOrEmpty(git.Subject) ? git.ShortHash : $"{git.ShortHash} · {git.Subject}";
    }

    private static string FormatDirty(GitRepoState? git)
    {
        if (git is null)
        {
            return string.Empty;
        }

        return git.IsDirty ? $"{git.DirtyCount} изм." : "чисто";
    }

    private static string FormatUpstream(GitRepoState? git)
    {
        if (git is null || !git.HasUpstream)
        {
            return string.Empty;
        }

        var parts = new List<string>(2);

        if (git.Ahead > 0)
        {
            parts.Add($"↑{git.Ahead}");
        }

        if (git.Behind > 0)
        {
            parts.Add($"↓{git.Behind}");
        }

        return string.Join(" ", parts);
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

        try
        {
            (LeftGitLog, RightGitLog) = await Task.Run(
                async () => (await _git.ReadHistoryAsync(left, count, CancellationToken.None), await _git.ReadHistoryAsync(right, count, CancellationToken.None)),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.GitStateFailed(ex.Unwrap());
            LeftGitLog = [];
            RightGitLog = [];
        }

        _gitHistoryLoaded = true;
        NotifyChanged();
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
