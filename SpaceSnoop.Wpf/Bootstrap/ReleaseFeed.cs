using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Bootstrap;

internal static class ReleaseFeed
{
    private static readonly HttpClient Http = CreateClient();

    internal static async Task<JsonDocument> GetReleasesAsync(string repo)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page=100");
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    internal static async Task FetchAssetAsync(string url, string path, IProgress<int> progress)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;

        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = File.Create(path);

        await CopyAsync(source, destination, total, progress);
    }

    internal static async Task CopyAsync(Stream source, Stream destination, long total, IProgress<int> progress)
    {
        var buffer = new byte[81920];
        long received = 0;
        var reported = -1;
        int read;

        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            received += read;

            if (total <= 0)
            {
                continue;
            }

            var percent = (int)(received * 100 / total);

            if (percent == reported)
            {
                continue;
            }

            reported = percent;
            progress.Report(percent);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new(AppInfo.Name, AppInfo.Version));
        client.DefaultRequestHeaders.Accept.Add(new("application/vnd.github+json"));
        return client;
    }
}
