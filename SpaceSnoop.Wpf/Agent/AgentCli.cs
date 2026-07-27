using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

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

        foreach (var name in executableNames)
        {
            foreach (var directory in directories)
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

    // TODO: .cmd/.bat находятся, но запускаются напрямую (Process.Start без cmd.exe) и падают
    //       Win32Exception; сейчас спасает порядок поиска (.exe по всем каталогам раньше шимов),
    //       апгрейд – запуск через "cmd.exe /c", если встретится установка вообще без .exe.
    public static string Run(string path, params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

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

            return output;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string DetectVersion(string path)
    {
        return ParseVersion(Run(path, "--version"));
    }
}
