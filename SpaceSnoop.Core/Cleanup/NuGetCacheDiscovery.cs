using System.ComponentModel;
using System.Security.Principal;

namespace SpaceSnoop.Core.Cleanup;

internal static class NuGetCacheDiscovery
{
    internal static readonly IReadOnlyDictionary<NuGetCacheKind, string> Names = new Dictionary<NuGetCacheKind, string>
    {
        [NuGetCacheKind.HttpCache] = "http-cache",
        [NuGetCacheKind.Temp] = "temp",
        [NuGetCacheKind.PluginsCache] = "plugins-cache",
        [NuGetCacheKind.GlobalPackages] = "global-packages",
    };

    internal static async Task<NuGetCacheLocations> DiscoverAsync(string logsDirectory, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        using var identity = WindowsIdentity.GetCurrent();
        var logPath = Path.Combine(logsDirectory, "development", "nuget-locals-" + Guid.NewGuid().ToString("N") + ".log");
        return await DiscoverAsync(new(), home, $"{identity.User?.Value}|{home}", logPath, token).ConfigureAwait(false);
    }

    internal static async Task<NuGetCacheLocations> DiscoverAsync(
        DotNetCli cli,
        string home,
        string context,
        string logPath,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string executable;
        try
        {
            executable = cli.ResolveExecutable();
        }
        catch (Win32Exception exception)
        {
            throw new InvalidOperationException("Не удалось найти dotnet. Для определения кэшей нужен .NET SDK.", exception);
        }

        var result = await cli.RunAsync(
                executable,
                ["nuget", "locals", "all", "--list", "--force-english-output"],
                home,
                logPath,
                TimeSpan.FromSeconds(30),
                token,
                new Dictionary<string, string>
                {
                    ["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1",
                })
            .ConfigureAwait(false);

        if (result.Outcome == DotNetCliOutcome.Cancelled)
        {
            throw new OperationCanceledException(token);
        }

        if (result.Outcome == DotNetCliOutcome.TimedOut)
        {
            throw new TimeoutException($"dotnet не ответил за 30 секунд. Журнал: {result.LogPath}");
        }

        if (!result.Succeeded || result.OutputTruncated)
        {
            throw new InvalidOperationException($"Не удалось определить кэши NuGet. {result.Error} {result.OutputTail} Журнал: {result.LogPath}");
        }

        return Parse(result.OutputTail, context);
    }

    internal static NuGetCacheLocations Parse(string output, string context)
    {
        var paths = new Dictionary<NuGetCacheKind, string>();
        foreach (var line in output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var (kind, name) in Names)
            {
                if (!line.StartsWith(name + ":", StringComparison.Ordinal))
                {
                    continue;
                }

                var path = line[(name.Length + 1)..].Trim();
                if (!paths.TryAdd(kind, NuGetCleanupService.Normalize(path)))
                {
                    throw new InvalidOperationException($"Неоднозначный путь NuGet: {name}.");
                }
            }
        }

        if (paths.Count != Names.Count)
        {
            throw new InvalidOperationException("dotnet вернул неполный список кэшей NuGet.");
        }

        return new(context, paths);
    }
}
