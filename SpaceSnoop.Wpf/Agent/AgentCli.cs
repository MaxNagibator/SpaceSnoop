using System.Buffers;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace SpaceSnoop.Wpf.Agent;

public static class AgentCli
{
    private static readonly SearchValues<char> CmdSpecials = SearchValues.Create(" \t\"&|<>^()%!");

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

    public static ProcessStartInfo CreateStartInfo(string path, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (IsScript(path))
        {
            info.FileName = CommandProcessor();
            info.Arguments = BuildScriptArguments(path, arguments);

            return info;
        }

        info.FileName = path;

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal static bool IsScript(string path)
    {
        var extension = Path.GetExtension(path);

        return string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)
               || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase);
    }

    // TODO: аргумент с `%` уезжает в cmd.exe и раскрывается как переменная окружения; апгрейд –
    //       запуск шима через сгенерированный временный .cmd с `setlocal disabledelayedexpansion`,
    //       если в аргументах появится текст человека, а не флаги и пути.
    internal static string BuildScriptArguments(string path, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder("/s /c \"").Append(Quote(path));

        foreach (var argument in arguments)
        {
            builder.Append(' ').Append(Quote(argument));
        }

        return builder.Append('"').ToString();
    }

    private static string CommandProcessor()
    {
        var comSpec = Environment.GetEnvironmentVariable("ComSpec");

        return string.IsNullOrWhiteSpace(comSpec) ? "cmd.exe" : comSpec;
    }

    internal static string Quote(string value)
    {
        if (value.Length > 0 && value.AsSpan().IndexOfAny(CmdSpecials) < 0)
        {
            return value;
        }

        var builder = new StringBuilder("\"");

        var index = 0;
        while (index < value.Length)
        {
            var slashes = 0;

            while (index < value.Length && value[index] == '\\')
            {
                slashes++;
                index++;
            }

            if (index == value.Length)
            {
                builder.Append('\\', slashes * 2);
                break;
            }

            if (value[index] == '"')
            {
                builder.Append('\\', (slashes * 2) + 1).Append('"');
            }
            else
            {
                builder.Append('\\', slashes).Append(value[index]);
            }

            index++;
        }

        return builder.Append('"').ToString();
    }

    public static string Run(string path, params string[] arguments)
    {
        try
        {
            using var process = Process.Start(CreateStartInfo(path, arguments));

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
