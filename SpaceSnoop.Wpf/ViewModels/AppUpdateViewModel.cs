using KeepShell.Services;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace SpaceSnoop.Wpf.ViewModels;

public sealed partial class AppUpdateViewModel : ObservableObject
{
    private static readonly HttpClient Http = CreateClient();

    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<AppUpdateViewModel> _logger;

    private string? _latestTag;
    private string? _releaseUrl;
    private bool _started;

    [ObservableProperty]
    private bool _hasPendingUpdate;

    [ObservableProperty]
    private string _versionLabel = string.Empty;

    public AppUpdateViewModel(ISettingsStore settings, IDialogService dialogs, ILogger<AppUpdateViewModel> logger)
    {
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = CheckAsync();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new(AppInfo.Name, AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        return client;
    }

    private async Task CheckAsync()
    {
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.RepoSlug}/releases/latest");
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            var root = json.RootElement;

            _latestTag = root.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            _releaseUrl = root.TryGetProperty("html_url", out var url) ? url.GetString() : null;

            if (!UpdateCheck.IsNewer(_latestTag, AppInfo.Version))
            {
                _logger.UpdateUpToDate(AppInfo.Version, _latestTag ?? "—");
                return;
            }

            if (string.Equals(_latestTag, _settings.GetStringValue(SettingsKeys.UpdateDismissedVersion), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            VersionLabel = _latestTag!;
            HasPendingUpdate = true;
            _logger.UpdateAvailable(_latestTag!, AppInfo.Version);
        }
        catch (Exception ex)
        {
            _logger.UpdateCheckFailed(ex);
        }
    }

    [RelayCommand]
    private void ShowDetails()
    {
        var message = $"Доступна версия {_latestTag}.{Environment.NewLine}{Environment.NewLine}Открыть страницу загрузки в браузере?";

        if (_dialogs.Confirm("Доступно обновление", message))
        {
            OpenUrl(_releaseUrl ?? AppInfo.ReleasesUrl);
        }
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
