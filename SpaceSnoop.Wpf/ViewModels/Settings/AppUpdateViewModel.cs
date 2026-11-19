using KeepShell.Services;
using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

public sealed partial class AppUpdateViewModel : ObservableObject
{
    private readonly UpdatePreferences _preferences;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shell;
    private readonly ILogger<AppUpdateViewModel> _logger;

    private string? _latestTag;
    private string? _releaseUrl;
    private string? _assetName;
    private string? _assetUrl;
    private string? _downloadedPath;
    private bool _started;
    private bool _checking;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _hasPendingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private int _downloadPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    [NotifyPropertyChangedFor(nameof(ActionText))]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheckStatus))]
    private string _checkStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private string _releaseNotes = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ReleaseNoteViewModel> _pendingEntries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangelog))]
    private string _changelog = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangelog))]
    private IReadOnlyList<ReleaseNoteViewModel> _changelogEntries = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangelogStatus))]
    private string _changelogStatus = string.Empty;

    [ObservableProperty]
    private bool _isChangelogLoading;

    public string PillText => (IsDownloading, _downloadedPath) switch
    {
        (true, _) => $"Загрузка {DownloadPercent}%",
        (false, not null) => "Готово к установке",
        _ => $"Доступна версия {VersionLabel}",
    };

    public string ActionText => (IsDownloading, _downloadedPath) switch
    {
        (true, _) => $"Загрузка {DownloadPercent}%",
        (false, not null) => "Показать в проводнике",
        _ => "Что нового и скачать",
    };

    public bool HasCheckStatus => !string.IsNullOrWhiteSpace(CheckStatus);

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public bool HasChangelog => ChangelogEntries.Count > 0 || !string.IsNullOrWhiteSpace(Changelog);

    public bool HasChangelogStatus => !string.IsNullOrWhiteSpace(ChangelogStatus);

    public string RepositoryUrl => $"https://github.com/{Repository}";

    public string ReleasesUrl => $"https://github.com/{Repository}/releases";

    private string Repository => string.IsNullOrWhiteSpace(_preferences.Repository) ? AppInfo.RepoSlug : _preferences.Repository.Trim();

    public AppUpdateViewModel(UpdatePreferences preferences, ISettingsStore settings, IDialogService dialogs, IShellLauncher shell, ILogger<AppUpdateViewModel> logger)
    {
        _preferences = preferences;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;

        _preferences.PropertyChanged += OnPreferencesChanged;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        if (ChecksOnStartup(_preferences.CheckOnStartup, _settings.GetBool(SettingsKeys.WelcomePending)))
        {
            _ = CheckAsync();
        }
    }

    [RelayCommand]
    public Task LoadChangelog()
    {
        return HasChangelog || IsChangelogLoading ? Task.CompletedTask : FetchChangelogAsync();
    }

    [RelayCommand]
    public Task ReloadChangelog()
    {
        return IsChangelogLoading ? Task.CompletedTask : FetchChangelogAsync();
    }

    [RelayCommand]
    public void OpenReleases()
    {
        _shell.Open(ReleasesUrl);
    }

    internal static bool ChecksOnStartup(bool checkOnStartup, bool welcomePending)
    {
        return checkOnStartup && !welcomePending;
    }

    internal static bool IsRateLimited(Exception exception)
    {
        return exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests };
    }

    internal ConfirmDialogViewModel BuildUpdateDialog()
    {
        return ReleaseChangelog.BuildUpdateDialog(_latestTag, _assetName, _assetUrl, PendingEntries, ReleaseNotes);
    }

    internal void ApplyChangelogForAutomation(IReadOnlyList<ReleaseNoteViewModel> entries)
    {
        CheckStatus = $"Установлена последняя версия ({AppInfo.Version})";
        ApplyChangelog(entries);
    }

    private async Task FetchChangelogAsync()
    {
        try
        {
            IsChangelogLoading = true;
            ChangelogStatus = "Загружаем историю изменений…";

            using var json = await ReleaseFeed.GetReleasesAsync(Repository);
            var releases = json.RootElement;
            var entries = releases.ValueKind == JsonValueKind.Array ? ReleaseChangelog.BuildChangelogEntries(releases) : [];

            ApplyChangelog(entries);
        }
        catch (Exception ex)
        {
            ChangelogStatus = IsRateLimited(ex)
                ? "GitHub временно ограничил запросы – историю изменений можно будет загрузить через час"
                : "Не удалось загрузить историю изменений";

            _logger.UpdateCheckFailed(ex);
        }
        finally
        {
            IsChangelogLoading = false;
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UpdatePreferences.Repository))
        {
            return;
        }

        OnPropertyChanged(nameof(RepositoryUrl));
        OnPropertyChanged(nameof(ReleasesUrl));

        if (_started)
        {
            _ = CheckAsync();
        }
    }

    private void ApplyChangelog(IReadOnlyList<ReleaseNoteViewModel> entries)
    {
        ChangelogEntries = entries;
        Changelog = ReleaseChangelog.BuildChangelog(entries);
        ChangelogStatus = HasChangelog ? string.Empty : "Релизы не найдены";
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.UpdateTempFileLeft(exception, path);
        }
    }

    private async Task CheckAsync(bool announce = false)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        ResetState();
        CheckStatus = "Проверяем обновления…";

        try
        {
            using var json = await ReleaseFeed.GetReleasesAsync(Repository);
            var releases = json.RootElement;

            if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
            {
                CheckStatus = "Релизы не найдены";
                return;
            }

            var latest = releases.EnumerateArray().FirstOrDefault();
            _latestTag = latest.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            _releaseUrl = latest.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            ReleaseNotes = ReleaseChangelog.BuildReleaseNotes(releases);
            PendingEntries = ReleaseChangelog.BuildPendingEntries(releases);
            ApplyChangelog(ReleaseChangelog.BuildChangelogEntries(releases));

            if (!UpdateCheck.IsNewer(_latestTag, AppInfo.Version))
            {
                CheckStatus = $"Установлена последняя версия ({AppInfo.Version})";
                _logger.UpdateUpToDate(AppInfo.Version, _latestTag ?? "–");
                return;
            }

            ResolveAsset(latest);

            var dismissed = string.Equals(_latestTag, _settings.GetStringValue(SettingsKeys.UpdateDismissedVersion), StringComparison.OrdinalIgnoreCase);

            if (dismissed && !announce)
            {
                CheckStatus = $"Доступна версия {_latestTag}";
                return;
            }

            CheckStatus = $"Доступна версия {_latestTag}";
            VersionLabel = _latestTag!;
            HasPendingUpdate = true;
            _logger.UpdateAvailable(_latestTag!, AppInfo.Version);

            if (_preferences.AutoDownload && _assetUrl is not null)
            {
                await DownloadAsync(announce: false);
            }
        }
        catch (Exception ex)
        {
            CheckStatus = IsRateLimited(ex)
                ? "GitHub временно ограничил проверки – повторите через час"
                : "Не удалось проверить обновления";

            _logger.UpdateCheckFailed(ex);
        }
        finally
        {
            _checking = false;
        }
    }

    private void ResetState()
    {
        HasPendingUpdate = false;
        _latestTag = null;
        _releaseUrl = null;
        _assetName = null;
        _assetUrl = null;
        _downloadedPath = null;
        DownloadPercent = 0;
        VersionLabel = string.Empty;
        ReleaseNotes = string.Empty;
        PendingEntries = [];
        OnPropertyChanged(nameof(ActionText));
    }

    private void ResolveAsset(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var names = new List<string>();
        var urls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var downloadUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(downloadUrl))
            {
                names.Add(name);
                urls[name] = downloadUrl;
            }
        }

        _assetName = UpdateCheck.PickAsset(names, AppInfo.ReleaseAssetProduct, UpdateCheck.IsSelfContained());

        if (_assetName is not null)
        {
            _assetUrl = urls[_assetName];
        }
    }

    [RelayCommand]
    private async Task ShowDetails()
    {
        if (IsDownloading)
        {
            return;
        }

        if (_downloadedPath is not null && File.Exists(_downloadedPath))
        {
            _shell.Reveal(_downloadedPath);
            return;
        }

        var dialog = BuildUpdateDialog();

        if (!await _dialogs.ShowAsync(dialog))
        {
            return;
        }

        if (dialog.Chosen?.IsPrimary == true && _assetUrl is not null)
        {
            await DownloadAsync(announce: true);
            return;
        }

        _shell.Open(_releaseUrl ?? ReleasesUrl);
    }

    [RelayCommand]
    private Task CheckNow()
    {
        return CheckAsync(announce: true);
    }

    [RelayCommand]
    private void Dismiss()
    {
        if (!string.IsNullOrEmpty(_latestTag))
        {
            _settings.SetValue(SettingsKeys.UpdateDismissedVersion, _latestTag);
        }

        HasPendingUpdate = false;
    }

    private async Task DownloadAsync(bool announce)
    {
        if (_assetUrl is null || _assetName is null || IsDownloading)
        {
            return;
        }

        var target = Path.Combine(AppStorage.PortableDirectory, _assetName);
        var temp = target + ".part";

        try
        {
            IsDownloading = true;
            DownloadPercent = 0;

            await ReleaseFeed.FetchAssetAsync(_assetUrl, temp, new Progress<int>(percent => DownloadPercent = percent));

            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(temp, target);

            _downloadedPath = target;
            OnPropertyChanged(nameof(PillText));
            OnPropertyChanged(nameof(ActionText));
            _logger.UpdateDownloaded(target);

            if (announce && await _dialogs.ShowAsync(ReleaseChangelog.BuildDownloadedDialog(target)))
            {
                _shell.Reveal(target);
            }
        }
        catch (Exception ex)
        {
            _logger.UpdateDownloadFailed(ex, _assetUrl);
            TryDelete(temp);

            if (announce)
            {
                _dialogs.Error(
                    "Обновление",
                    IsRateLimited(ex)
                        ? "GitHub временно ограничил загрузки – повторите через час."
                        : "Не удалось скачать обновление. Проверьте подключение к интернету и повторите попытку.");
            }
        }
        finally
        {
            IsDownloading = false;
        }
    }
}
