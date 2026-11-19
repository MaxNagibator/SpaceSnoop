using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace SpaceSnoop.Core.Cleanup;

public enum DotNetCliOutcome
{
    None = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    TimedOut = 4,
    LaunchFailed = 5,
    LoggingFailed = 6,
}

internal sealed record DotNetCliResult(
    DotNetCliOutcome Outcome,
    int? ExitCode,
    string OutputTail,
    string LogPath,
    bool OutputTruncated,
    string? Error)
{
    public bool Succeeded => Outcome == DotNetCliOutcome.Succeeded;
}

internal sealed class DotNetCli
{
    private const int TailLimit = 24_000;
    private readonly Func<string?> _path = () => Environment.GetEnvironmentVariable("PATH");
    private readonly Func<string, bool> _fileExists = File.Exists;

    private readonly Func<ProcessStartInfo, Process> _start = info => new()
    {
        StartInfo = info,
    };

    private readonly Func<string, Stream> _openLog = path => new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);

    internal DotNetCli()
    {
    }

    internal DotNetCli(Func<string?> path, Func<string, bool> fileExists, Func<ProcessStartInfo, Process> start)
        : this(path, fileExists, start, logPath => new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
    {
    }

    internal DotNetCli(
        Func<string?> path,
        Func<string, bool> fileExists,
        Func<ProcessStartInfo, Process> start,
        Func<string, Stream> openLog)
    {
        _path = path;
        _fileExists = fileExists;
        _start = start;
        _openLog = openLog;
    }

    internal async Task<DotNetCliResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string logPath,
        TimeSpan timeout,
        CancellationToken token,
        IReadOnlyDictionary<string, string>? processEnvironment = null)
    {
        var tail = new StringBuilder();
        var gate = new object();
        var outputTruncated = false;
        StreamWriter? log = null;
        Process? process = null;
        var processStarted = false;
        Task? stdout = null;
        Task? stderr = null;
        var pumpFailure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        var outcome = DotNetCliOutcome.LaunchFailed;
        int? exitCode = null;
        string? error = null;
        var startedUtc = DateTimeOffset.UtcNow;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            log = new(_openLog(logPath), new UTF8Encoding(false));
            log.WriteLine($"UTC start: {startedUtc:O}");
            log.WriteLine($"Executable: {executable}");
            log.WriteLine($"Arguments: {JsonSerializer.Serialize(arguments)}");
            log.WriteLine($"Working directory: {Path.GetFullPath(workingDirectory)}");
            log.WriteLine();
            await log.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetFullPath(workingDirectory),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }

            info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
            info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            info.Environment["DOTNET_NOLOGO"] = "1";
            info.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true";
            if (processEnvironment is not null)
            {
                foreach (var (key, value) in processEnvironment)
                {
                    info.Environment[key] = value;
                }
            }

            process = _start(info);
            token.ThrowIfCancellationRequested();
            if (!process.Start())
            {
                throw new Win32Exception("dotnet did not start.");
            }

            processStarted = true;
            stdout = PumpAsync(process.StandardOutput, "stdout", log, tail, gate, () => outputTruncated = true, pumpFailure);
            stderr = PumpAsync(process.StandardError, "stderr", log, tail, gate, () => outputTruncated = true, pumpFailure);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutSource.CancelAfter(timeout);
            try
            {
                var processExit = process.WaitForExitAsync(timeoutSource.Token);
                var finished = await Task.WhenAny(processExit, pumpFailure.Task).ConfigureAwait(false);
                if (finished == pumpFailure.Task)
                {
                    throw await pumpFailure.Task.ConfigureAwait(false);
                }

                await processExit.ConfigureAwait(false);
                try
                {
                    await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    outcome = DotNetCliOutcome.LoggingFailed;
                    error = "Потоки вывода dotnet не завершились в течение 5 секунд.";
                    var stopError = await StopOwnedProcessAsync(process, stdout, stderr, log, gate, processStarted).ConfigureAwait(false);
                    if (stopError is not null)
                    {
                        error += Environment.NewLine + stopError;
                    }
                }

                await log.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                exitCode = process.ExitCode;
                if (outcome != DotNetCliOutcome.LoggingFailed)
                {
                    outcome = exitCode == 0 ? DotNetCliOutcome.Succeeded : DotNetCliOutcome.Failed;
                }
            }
            catch (OperationCanceledException)
            {
                outcome = token.IsCancellationRequested ? DotNetCliOutcome.Cancelled : DotNetCliOutcome.TimedOut;
                if (process is not null)
                {
                    error = await StopOwnedProcessAsync(process, stdout, stderr, log, gate, processStarted).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                outcome = DotNetCliOutcome.LoggingFailed;
                error = exception.Message;
                var stopError = await StopOwnedProcessAsync(process, stdout, stderr, log, gate, processStarted).ConfigureAwait(false);
                if (stopError is not null)
                {
                    error += Environment.NewLine + stopError;
                }
            }
        }
        catch (OperationCanceledException)
        {
            outcome = DotNetCliOutcome.Cancelled;
            if (process is not null)
            {
                error = await StopOwnedProcessAsync(process, stdout, stderr, log, gate, processStarted).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            outcome = exception is IOException or UnauthorizedAccessException ? DotNetCliOutcome.LoggingFailed : DotNetCliOutcome.LaunchFailed;
            error = exception.Message;
            if (process is not null)
            {
                var stopError = await StopOwnedProcessAsync(process, stdout, stderr, log, gate, processStarted).ConfigureAwait(false);
                if (stopError is not null)
                {
                    error += Environment.NewLine + stopError;
                }
            }
        }
        finally
        {
            if (log is not null)
            {
                try
                {
                    log.WriteLine();
                    log.WriteLine($"UTC end: {DateTimeOffset.UtcNow:O}");
                    log.WriteLine($"Outcome: {outcome}");
                    log.WriteLine($"Exit code: {exitCode?.ToString() ?? "(none)"}");
                    log.WriteLine($"Error: {error ?? "(none)"}");
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    error ??= exception.Message;
                    outcome = DotNetCliOutcome.LoggingFailed;
                }

                try
                {
                    await log.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    error ??= exception.Message;
                    outcome = DotNetCliOutcome.LoggingFailed;
                }

                try
                {
                    await log.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    error ??= exception.Message;
                    outcome = DotNetCliOutcome.LoggingFailed;
                }
            }

            process?.Dispose();
        }

        var captured = tail.ToString();
        if (outputTruncated)
        {
            var firstLineEnd = captured.IndexOf('\n');
            captured = firstLineEnd >= 0 ? captured[(firstLineEnd + 1)..] : string.Empty;
        }

        return new(outcome, exitCode, captured, logPath, outputTruncated, error);
    }

    internal string ResolveExecutable()
    {
        foreach (var entry in (_path() ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(entry))
            {
                continue;
            }

            var candidate = Path.Combine(entry, "dotnet.exe");
            if (_fileExists(candidate))
            {
                return candidate;
            }
        }

        throw new Win32Exception("Не удалось найти dotnet.exe в абсолютных каталогах PATH.");
    }

    private static async Task PumpAsync(
        StreamReader reader,
        string name,
        StreamWriter log,
        StringBuilder tail,
        object gate,
        Action onTruncated,
        TaskCompletionSource<Exception> failure)
    {
        try
        {
            var buffer = new char[2048];
            while (true)
            {
                var read = await reader.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                if (read == 0)
                {
                    return;
                }

                lock (gate)
                {
                    log.Write('[');
                    log.Write(name);
                    log.Write("] ");
                    log.Write(buffer, 0, read);
                    tail.Append(buffer, 0, read);
                    if (tail.Length > TailLimit)
                    {
                        tail.Remove(0, tail.Length - TailLimit);
                        onTruncated();
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            failure.TrySetResult(exception);
            throw;
        }
    }

    private static async Task<string?> StopOwnedProcessAsync(
        Process process,
        Task? stdout,
        Task? stderr,
        StreamWriter? log,
        object gate,
        bool processStarted)
    {
        string? error = null;
        try
        {
            if (processStarted && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (Win32Exception exception)
        {
            error = exception.Message;
        }

        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        if (processStarted)
        {
            try
            {
                await process.WaitForExitAsync(bound.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                error = "Не удалось дождаться остановки процесса. Проверьте его состояние перед повторным запуском.";
            }
            catch (InvalidOperationException)
            {
            }
        }

        try
        {
            await Task.WhenAll(stdout ?? Task.CompletedTask, stderr ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            if (processStarted)
            {
                try
                {
                    process.StandardOutput.BaseStream.Dispose();
                }
                catch (InvalidOperationException)
                {
                }

                try
                {
                    process.StandardError.BaseStream.Dispose();
                }
                catch (InvalidOperationException)
                {
                }
            }

            try
            {
                await Task.WhenAll(stdout ?? Task.CompletedTask, stderr ?? Task.CompletedTask)
                    .WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or IOException or ObjectDisposedException)
            {
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
        }

        if (log is not null)
        {
            try
            {
                lock (gate)
                {
                    log.Flush();
                }
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
                error ??= exception.Message;
            }
        }

        return error;
    }
}
