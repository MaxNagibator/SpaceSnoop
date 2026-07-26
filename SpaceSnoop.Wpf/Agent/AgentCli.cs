using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Agent;

public static class AgentCli
{
    private static readonly string[] ExecutableNames = ["claude.exe", "claude.cmd", "claude.bat"];

    public static AgentCliInfo? Detect(string? overridePath)
    {
        var path = ResolveExecutable(overridePath, CandidateDirectories(), File.Exists);

        return path is null ? null : new AgentCliInfo(path, DetectVersion(path));
    }

    internal static string? ResolveExecutable(string? overridePath, IReadOnlyList<string> directories, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && IsKnownExecutableName(overridePath) && fileExists(overridePath))
        {
            return overridePath;
        }

        foreach (var directory in directories)
        {
            foreach (var name in ExecutableNames)
            {
                var candidate = Path.Combine(directory, name);

                if (fileExists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool IsKnownExecutableName(string path)
    {
        var fileName = Path.GetFileName(path);
        return Array.Exists(ExecutableNames, name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ParseVersion(string rawOutput)
    {
        var trimmed = rawOutput.Trim();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var spaceIndex = trimmed.IndexOf(' ');
        return spaceIndex < 0 ? trimmed : trimmed[..spaceIndex];
    }

    private static IReadOnlyList<string> CandidateDirectories()
    {
        var directories = new List<string>();

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        directories.AddRange(pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(userProfile))
        {
            directories.Add(Path.Combine(userProfile, ".local", "bin"));
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (!string.IsNullOrEmpty(appData))
        {
            directories.Add(Path.Combine(appData, "npm"));
        }

        return directories;
    }

    // TODO: claude.cmd/claude.bat находятся, но запускаются напрямую (Process.Start без cmd.exe) –
    //       на этой машине реальный claude.exe, npm-шимы не проверялись; апгрейд – запуск через
    //       "cmd.exe /c" для .cmd/.bat, если такая установка встретится на практике.
    private static string DetectVersion(string path)
    {
        try
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            info.ArgumentList.Add("--version");

            using var process = Process.Start(info);

            if (process is null)
            {
                return string.Empty;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(AppDefaults.AgentDetectTimeoutSeconds * 1000))
            {
                process.Kill(entireProcessTree: true);
                return string.Empty;
            }

            var output = outputTask.GetAwaiter().GetResult();
            errorTask.GetAwaiter().GetResult();

            return ParseVersion(output);
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return string.Empty;
        }
    }
}
