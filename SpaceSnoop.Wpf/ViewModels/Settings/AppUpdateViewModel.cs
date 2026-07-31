using KeepShell.Services;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Wpf.ViewModels.Settings;

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

    public bool HasReleaseNotes => !string.IsNullOrWhiteSpace(ReleaseNotes);

    public bool HasChangelog => ChangelogEntries.Count > 0 || !string.IsNullOrWhiteSpace(Changelog);

    public bool HasChangelogStatus => !string.IsNullOrWhiteSpace(ChangelogStatus);

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

        var repo = string.IsNullOrWhiteSpace(_preferences.Repository) ? AppInfo.RepoSlug : _preferences.Repository.Trim();

        try
        {
            IsChangelogLoading = true;
            ChangelogStatus = "Загружаем историю изменений…";

            using var json = await GetReleasesAsync(repo);
            var releases = json.RootElement;
            var entries = releases.ValueKind == JsonValueKind.Array ? BuildChangelogEntries(releases) : [];

            ChangelogEntries = entries;
            Changelog = BuildChangelog(entries);
            ChangelogStatus = HasChangelog ? string.Empty : "Релизы не найдены";
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

    internal static string ExtractChanges(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var text = body.Replace("\r\n", "\n", StringComparison.Ordinal);
        const string marker = "## Изменения";
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            text = text[(index + marker.Length)..].Trim();
            var next = text.IndexOf("\n## ", StringComparison.Ordinal);

            if (next >= 0)
            {
                text = text[..next].Trim();
            }
        }
        else
        {
            text = RemoveSection(text, "## Скачать");
            text = RemoveSection(text, "## Статистика");
        }

        return text.Replace("\n", Environment.NewLine, StringComparison.Ordinal).Trim();
    }

    internal static IReadOnlyList<ReleaseChangeViewModel> ExtractChangeItems(string? body)
    {
        var changes = ExtractChanges(body);

        if (string.IsNullOrWhiteSpace(changes))
        {
            return [];
        }

        var items = new List<(string Summary, List<string> Details)>();

        foreach (var line in changes.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("**Полный список:**", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal))
            {
                items.Add((line[2..].Trim(), []));
                continue;
            }

            var detail = line.Trim();

            if (detail.Length == 0)
            {
                continue;
            }

            if (items.Count == 0)
            {
                items.Add((detail, []));
            }
            else
            {
                items[^1].Details.Add(detail);
            }
        }

        return items.Select(static item => new ReleaseChangeViewModel(item.Summary, item.Details)).ToList();
    }

    internal static string BuildReleaseNotes(JsonElement releases)
    {
        var notes = new List<(string Tag, string Changes)>();

        foreach (var release in releases.EnumerateArray())
        {
            var tag = release.TryGetProperty("tag_name", out var tagProperty) ? tagProperty.GetString() : null;

            if (!UpdateCheck.IsNewer(tag, AppInfo.Version))
            {
                continue;
            }

            var body = release.TryGetProperty("body", out var bodyProperty) ? bodyProperty.GetString() : null;
            var changes = ExtractChanges(body).Replace("**", string.Empty, StringComparison.Ordinal);

            if (string.IsNullOrWhiteSpace(changes))
            {
                continue;
            }

            notes.Add((tag ?? string.Empty, changes));
        }

        return notes.Count == 1
            ? notes[0].Changes
            : string.Join($"{Environment.NewLine}{Environment.NewLine}", notes.Select(static note => $"{note.Tag}{Environment.NewLine}{note.Changes}"));
    }

    internal static IReadOnlyList<ReleaseNoteViewModel> BuildChangelogEntries(JsonElement releases)
    {
        var entries = new List<ReleaseNoteViewModel>();

        foreach (var release in releases.EnumerateArray())
        {
            if (BuildChangelogEntry(release) is { } entry)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    internal static string? ExtractCompareUrl(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var match = CompareUrlRegex().Match(body);
        return match.Success ? match.Value.TrimEnd('.') : null;
    }

    private void OnPreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UpdatePreferences.Repository) && _started)
        {
            _ = CheckAsync();
        }
    }

    private static string RemoveSection(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return text;
        }

        var end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);
        return (end < 0 ? text[..start] : text[..start] + text[(end + 1)..]).Trim();
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

    private static string BuildChangelog(IReadOnlyList<ReleaseNoteViewModel> entries)
    {
        return string.Join($"{Environment.NewLine}{Environment.NewLine}", entries
            .Select(static entry =>
                $"## {entry.Title} · {entry.PublishedDate}{Environment.NewLine}{string.Join(Environment.NewLine, entry.Changes.Select(static change => FormatChange(change)))}"));
    }

    private static string FormatChange(ReleaseChangeViewModel change)
    {
        var lines = new List<string> { $"- {change.Summary}" };
        lines.AddRange(change.Details.Select(static detail => $"  {detail}"));
        return string.Join(Environment.NewLine, lines);
    }

    private static ReleaseNoteViewModel? BuildChangelogEntry(JsonElement release)
    {
        var tag = StringProperty(release, "tag_name");
        var name = StringProperty(release, "name");
        var date = StringProperty(release, "published_at");
        var body = StringProperty(release, "body");
        var title = string.IsNullOrWhiteSpace(name) ? tag : name;

        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var publishedDate = string.IsNullOrWhiteSpace(date) || date.Length < 10 ? "без даты" : date[..10];
        var changes = ExtractChangeItems(body);
        var compareUrl = ExtractCompareUrl(body);

        return new(title, publishedDate, changes.Count == 0 ? [new("Изменения не описаны.", [])] : changes, compareUrl ?? StringProperty(release, "html_url"));
    }

    private static string? StringProperty(JsonElement owner, string name)
    {
        return owner.TryGetProperty(name, out var property) ? property.GetString() : null;
    }

    [GeneratedRegex(@"https://\S+/compare/\S+")]
    private static partial Regex CompareUrlRegex();

    private static async Task<JsonDocument> GetReleasesAsync(string repo)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=100");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
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
            using var json = await GetReleasesAsync(repo);
            var releases = json.RootElement;

            if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() == 0)
            {
                CheckStatus = "Релизы не найдены";
                return;
            }

            var latest = releases.EnumerateArray().FirstOrDefault();
            _latestTag = latest.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
            _releaseUrl = latest.TryGetProperty("html_url", out var url) ? url.GetString() : null;
            var entries = BuildChangelogEntries(releases);
            ReleaseNotes = BuildReleaseNotes(releases);
            ChangelogEntries = entries;
            Changelog = BuildChangelog(entries);
            ChangelogStatus = HasChangelog ? string.Empty : "Релизы не найдены";

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

    private string ReleaseNotesBlock()
    {
        if (string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            return Environment.NewLine;
        }

        var notes = ReleaseNotes.Length <= 1200 ? ReleaseNotes : ReleaseNotes[..1200] + "…";
        return $"{Environment.NewLine}Изменения:{Environment.NewLine}{notes}{Environment.NewLine}";
    }

    private void OpenFolder(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SystemExecutable.Explorer,
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

public sealed record ReleaseNoteViewModel(string Title, string PublishedDate, IReadOnlyList<ReleaseChangeViewModel> Changes, string? CompareUrl);

public sealed record ReleaseChangeViewModel(string Summary, IReadOnlyList<string> Details);
