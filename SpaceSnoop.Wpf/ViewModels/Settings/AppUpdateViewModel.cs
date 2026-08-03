using KeepShell.Services;
using System.ComponentModel;
using System.IO;
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
    private bool _hasPendingUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    private bool _isDownloading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    private int _downloadPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PillText))]
    private string _versionLabel = string.Empty;

    [ObservableProperty]
    private string _checkStatus = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReleaseNotes))]
    private string _releaseNotes = string.Empty;

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

    public AppUpdateViewModel(UpdatePreferences preferences, ISettingsStore settings, IDialogService dialogs, IShellLauncher shell, ILogger<AppUpdateViewModel> logger)
    {
        _preferences = preferences;
        _settings = settings;
        _dialogs = dialogs;
        _shell = shell;
        _logger = logger;

        _preferences.PropertyChanged += OnPreferencesChanged;
    }

    public string PillText => (IsDownloading, _downloadedPath) switch
    {
        (true, _) => $"Загрузка {DownloadPercent}%",
        (false, not null) => "Готово к установке",
        _ => $"Доступна версия {VersionLabel}",
    };

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public bool HasChangelog => ChangelogEntries.Count > 0 || !string.IsNullOrWhiteSpace(Changelog);

    public bool HasChangelogStatus => !string.IsNullOrWhiteSpace(ChangelogStatus);

    private string Repository => string.IsNullOrWhiteSpace(_preferences.Repository) ? AppInfo.RepoSlug : _preferences.Repository.Trim();

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        if (_preferences.CheckOnStartup)
        {
            _ = CheckAsync();
        }
    }

    [RelayCommand]
    public async Task LoadChangelog()
    {
        if (HasChangelog || IsChangelogLoading)
        {
            return;
        }

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
            ChangelogStatus = "Не удалось загрузить историю изменений";
            _logger.UpdateCheckFailed(ex);
        }
        finally
        {
            IsChangelogLoading = false;
        }
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.Repository) && _started)
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
            ApplyChangelog(ReleaseChangelog.BuildChangelogEntries(releases));

            if (!UpdateCheck.IsNewer(_latestTag, AppInfo.Version))
            {
                CheckStatus = $"Установлена последняя версия ({AppInfo.Version})";
                _logger.UpdateUpToDate(AppInfo.Version, _latestTag ?? "—");
                return;
            }

            ResolveAsset(latest);

            var dismissed = string.Equals(_latestTag, _settings.GetStringValue(SettingsKeys.UpdateDismissedVersion), StringComparison.OrdinalIgnoreCase);

            if (dismissed && !announce)
            {
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
            CheckStatus = "Не удалось проверить обновления";
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

        _assetName = UpdateCheck.PickAsset(names, AppInfo.Name, UpdateCheck.IsSelfContained());

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

        if (_assetUrl is not null)
        {
            var message = $"Доступна версия {_latestTag}.{Environment.NewLine}{ReleaseNotesBlock()}{Environment.NewLine}Скачать «{_assetName}» рядом с программой?";

            if (_dialogs.Confirm("Доступно обновление", message))
            {
                await DownloadAsync(announce: true);
            }

            return;
        }

        var browse = $"Доступна версия {_latestTag}.{Environment.NewLine}{ReleaseNotesBlock()}{Environment.NewLine}Открыть страницу загрузки в браузере?";

        if (_dialogs.Confirm("Доступно обновление", browse))
        {
            _shell.Open(_releaseUrl ?? AppInfo.ReleasesUrl);
        }
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

        var target = Path.Combine(AppContext.BaseDirectory, _assetName);
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
            _logger.UpdateDownloaded(target);

            if (announce && _dialogs.Confirm("Обновление скачано", $"Файл сохранён рядом с программой:{Environment.NewLine}{target}{Environment.NewLine}{Environment.NewLine}Открыть папку?"))
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
                _dialogs.Error("Обновление", $"Не удалось скачать обновление.{Environment.NewLine}{ex.Message}");
            }
        }
        finally
        {
            IsDownloading = false;
        }
    }

    private string ReleaseNotesBlock()
    {
        if (string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            return Environment.NewLine;
        }

        var notes = ReleaseNotes.Length <= 1200 ? ReleaseNotes : ReleaseNotes[..1200] + "…";
        return $"{Environment.NewLine}Изменения:{Environment.NewLine}{notes}{Environment.NewLine}";
    }
}
