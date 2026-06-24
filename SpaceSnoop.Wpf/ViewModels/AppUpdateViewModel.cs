using KeepShell.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class AppUpdateViewModel : ObservableObject
{
    private static readonly HttpClient Http = CreateClient();

    private readonly UpdatePreferences _preferences;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
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

    public AppUpdateViewModel(UpdatePreferences preferences, ISettingsStore settings, IDialogService dialogs, ILogger<AppUpdateViewModel> logger)
    {
        _preferences = preferences;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        _preferences.PropertyChanged += OnPreferencesChanged;
    }

    public string PillText =>
        IsDownloading ? $"Загрузка {DownloadPercent}%"
        : _downloadedPath is not null ? "Готово к установке"
        : $"Доступна версия {VersionLabel}";

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

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.Repository) && _started)
        {
            _ = CheckAsync();
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new(AppInfo.Name, AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        return client;
    }

    private static bool IsSelfContained()
    {
        var path = Environment.ProcessPath;

        // TODO: размер exe – единственный надёжный признак self-contained для single-file (рантайм встроен, рядом hostfxr нет); порог в AppDefaults
        return path is not null && File.Exists(path) && new FileInfo(path).Length > AppDefaults.SelfContainedExeThreshold;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task CheckAsync(bool announce = false)
    {
        if (_checking)
        {
            return;
        }

        var repo = string.IsNullOrWhiteSpace(_preferences.Repository) ? AppInfo.RepoSlug : _preferences.Repository.Trim();

        _checking = true;
        ResetState();
        CheckStatus = "Проверяем обновления…";

        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            var root = json.RootElement;

            _latestTag = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            _releaseUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            if (!UpdateCheck.IsNewer(_latestTag, AppInfo.Version))
            {
                CheckStatus = $"Установлена последняя версия ({AppInfo.Version})";
                _logger.UpdateUpToDate(AppInfo.Version, _latestTag ?? "—");
                return;
            }

            ResolveAsset(root);

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

        _assetName = UpdateCheck.PickAsset(names, AppInfo.Name, IsSelfContained());

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
            OpenFolder(_downloadedPath);
            return;
        }

        if (_assetUrl is not null)
        {
            var message = $"Доступна версия {_latestTag}.{Environment.NewLine}{Environment.NewLine}Скачать «{_assetName}» рядом с программой?";

            if (_dialogs.Confirm("Доступно обновление", message))
            {
                await DownloadAsync(announce: true);
            }

            return;
        }

        var browse = $"Доступна версия {_latestTag}.{Environment.NewLine}{Environment.NewLine}Открыть страницу загрузки в браузере?";

        if (_dialogs.Confirm("Доступно обновление", browse))
        {
            OpenUrl(_releaseUrl ?? AppInfo.ReleasesUrl);
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

            using var response = await Http.GetAsync(_assetUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1;

            await using (var source = await response.Content.ReadAsStreamAsync())
            await using (var destination = File.Create(temp))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;

                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, read));
                    received += read;

                    if (total > 0)
                    {
                        DownloadPercent = (int)(received * 100 / total);
                    }
                }
            }

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
                OpenFolder(target);
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

    private void OpenFolder(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _logger.OpenExplorerFailed(ex, filePath);
        }
    }

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.OpenUrlFailed(ex, url);
        }
    }
}
