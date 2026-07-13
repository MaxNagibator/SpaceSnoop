using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SpaceSnoop.Core.Git;

public sealed class GitService
{
    public async Task<GitRepoState?> ReadAsync(string path, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        var status = await RunAsync(path, "status --porcelain=2 --branch -- .", cancel);

        if (status.Failed)
        {
            return null;
        }

        var log = await RunAsync(path, "log -1 --format=%h%x09%cI%x09%s", cancel);

        return GitRepoState.Parse(status.StdOut, log.Failed ? string.Empty : log.StdOut);
    }

    public async Task<IReadOnlyList<GitCommit>> ReadHistoryAsync(string path, int count, CancellationToken cancel = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path) || count <= 0)
        {
            return [];
        }

        var log = await RunAsync(path, $"log -{count} --format=%h%x09%cI%x09%an%x09%s", cancel);

        return log.Failed ? [] : GitCommit.ParseLog(log.StdOut);
    }

    private static readonly string Executable = ResolveExecutable();

    private static string ResolveExecutable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        ];

        return Array.Find(candidates, File.Exists) ?? "git";
    }

    private static async Task<ProcessRun> RunAsync(string workingDirectory, string arguments, CancellationToken cancel)
    {
        var psi = new ProcessStartInfo(Executable, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();

            var stdOut = process.StandardOutput.ReadToEndAsync(cancel);
            var stdErr = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);

            return new(process.ExitCode, await stdOut, await stdErr);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return new(-1, string.Empty, string.Empty);
        }
    }

    private sealed record ProcessRun(int ExitCode, string StdOut, string StdErr)
    {
        public bool Failed => ExitCode != 0;
    }
}
