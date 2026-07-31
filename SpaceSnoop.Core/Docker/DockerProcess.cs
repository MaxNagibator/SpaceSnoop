using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SpaceSnoop.Core.Docker;

public sealed record DockerProcessOptions(
    TimeSpan Timeout,
    Encoding? OutputEncoding = null,
    string? StandardInput = null);

public sealed record DockerProcessResult(
    int ExitCode,
    string StdOut,
    string StdErr,
    string? LaunchError = null,
    bool TimedOut = false,
    bool Canceled = false)
{
    public bool Succeeded => LaunchError is null && ExitCode == 0 && !TimedOut && !Canceled;

    public bool Failed => !Succeeded;
}

public interface IDockerProcessRunner
{
    Task<DockerProcessResult> RunAsync(
        string fileName,
        string arguments,
        DockerProcessOptions options,
        CancellationToken cancel = default);
}

public sealed class SystemDockerProcessRunner : IDockerProcessRunner
{
    public async Task<DockerProcessResult> RunAsync(
        string fileName,
        string arguments,
        DockerProcessOptions options,
        CancellationToken cancel = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = options.StandardInput is not null,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = options.OutputEncoding,
                StandardErrorEncoding = options.OutputEncoding,
            },
        };

        Task<string> outputTask = Task.FromResult(string.Empty);
        Task<string> errorTask = Task.FromResult(string.Empty);
        var started = false;

        try
        {
            started = process.Start();
            if (!started)
            {
                return new(-1, string.Empty, string.Empty, "Процесс не запустился.");
            }

            outputTask = process.StandardOutput.ReadToEndAsync();
            errorTask = process.StandardError.ReadToEndAsync();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            if (options.Timeout != System.Threading.Timeout.InfiniteTimeSpan)
            {
                timeout.CancelAfter(options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromMilliseconds(1));
            }

            if (options.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(options.StandardInput.AsMemory(), timeout.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await outputTask, await errorTask);
        }
        catch (OperationCanceledException)
        {
            if (started)
            {
                TryKill(process);
                await WaitAfterKillAsync(process);
            }

            return new(
                -1,
                await ReadTaskAsync(outputTask),
                await ReadTaskAsync(errorTask),
                TimedOut: !cancel.IsCancellationRequested,
                Canceled: cancel.IsCancellationRequested);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or ArgumentException)
        {
            return new(-1, string.Empty, string.Empty, ex.Message);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return;
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or TimeoutException)
        {
            return;
        }
    }

    private static async Task<string> ReadTaskAsync(Task<string> task)
    {
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex) when (ex is ObjectDisposedException or TimeoutException)
        {
            return string.Empty;
        }
    }
}
