using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpaceSnoop.Wpf.Agent;

public static class AgentCli
{
    public static AgentCliInfo? Detect(IReadOnlyList<string> executableNames, string? overridePath, IReadOnlyList<string> extraDirectories)
    {
        var path = ResolveExecutable(executableNames, overridePath, CandidateDirectories(extraDirectories), File.Exists);

        return path is null ? null : new AgentCliInfo(path, DetectVersion(path));
    }

    public static IReadOnlyList<string> ExecutableNames(string cliName)
    {
        return [$"{cliName}.exe", $"{cliName}.cmd", $"{cliName}.bat"];
    }

    internal static string? ResolveExecutable(
        IReadOnlyList<string> executableNames,
        string? overridePath,
        IReadOnlyList<string> directories,
        Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(overridePath) && IsKnownExecutableName(executableNames, overridePath) && fileExists(overridePath))
        {
            return overridePath;
        }

        foreach (var directory in directories)
        {
            foreach (var name in executableNames)
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

    private static bool IsKnownExecutableName(IReadOnlyList<string> executableNames, string path)
    {
        var fileName = Path.GetFileName(path);
        return executableNames.Any(name => string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase));
    }

    internal static string ParseVersion(string rawOutput)
    {
        var trimmed = rawOutput.Trim();

        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        return Array.Find(words, word => char.IsAsciiDigit(word[0])) ?? words[0];
    }

    private static IReadOnlyList<string> CandidateDirectories(IReadOnlyList<string> extraDirectories)
    {
        var directories = new List<string>();

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        directories.AddRange(pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
        directories.AddRange(extraDirectories);

        return directories;
    }

    // TODO: claude.cmd/claude.bat находятся, но запускаются напрямую (Process.Start без cmd.exe) –
    //       на этой машине реальные claude.exe и codex.exe, npm-шимы не проверялись; апгрейд –
    //       запуск через "cmd.exe /c" для .cmd/.bat, если такая установка встретится на практике.
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
