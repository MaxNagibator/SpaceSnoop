using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Core.Cleanup;

public sealed class WorkloadCleanupService
{
    private static readonly TimeSpan InspectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanTimeout = TimeSpan.FromMinutes(30);
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Func<string> _resolveExecutable;

    private readonly Func<string, IReadOnlyList<string>, string, string, TimeSpan, CancellationToken,
        IReadOnlyDictionary<string, string>?, Task<DotNetCliResult>> _run;

    private readonly Func<string> _workingDirectory;
    private readonly Func<string, string> _logPath;
    private readonly Func<DateTime> _utcNow;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public WorkloadCleanupService(string logsDirectory) : this(
        new(),
        () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        name => CreateLogPath(logsDirectory, name),
        () => DateTime.UtcNow,
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>()))
    {
    }

    internal WorkloadCleanupService(
        DotNetCli cli,
        Func<string> workingDirectory,
        Func<string, string> logPath,
        Func<DateTime> utcNow,
        IReadOnlyDictionary<string, string>? environment = null)
        : this(cli.ResolveExecutable, cli.RunAsync, workingDirectory, logPath, utcNow, environment)
    {
    }

    internal WorkloadCleanupService(
        Func<string> resolveExecutable,
        Func<string, IReadOnlyList<string>, string, string, TimeSpan, CancellationToken,
            IReadOnlyDictionary<string, string>?, Task<DotNetCliResult>> run,
        Func<string> workingDirectory,
        Func<string, string> logPath,
        Func<DateTime> utcNow,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        _resolveExecutable = resolveExecutable;
        _run = run;
        _workingDirectory = workingDirectory;
        _logPath = logPath;
        _utcNow = utcNow;
        _environment = environment ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    }

    public async Task<WorkloadCleanupPlan> InspectAsync(CancellationToken token)
    {
        var directory = Path.GetFullPath(_workingDirectory());
        var executable = _resolveExecutable();
        var versionLog = _logPath("inspect-version");
        var version = await _run(executable, ["--version"], directory, versionLog, InspectTimeout, token, _environment).ConfigureAwait(false);
        var sdk = RequireSuccess(version, "Не удалось определить активную версию SDK.").Trim();
        if (!Regex.IsMatch(sdk, @"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
        {
            throw new WorkloadInspectionException("dotnet --version вернул некорректную версию SDK.", version);
        }

        var workloads = await _run(executable, ["workload", "list"], directory, _logPath("inspect-workloads"), InspectTimeout, token, _environment).ConfigureAwait(false);
        var installed = RequireSuccess(workloads, "Не удалось получить список workloads.", true);
        return new(_owner, executable, directory, sdk, installed.Trim(), _utcNow(), workloads.LogPath);
    }

    public async Task<WorkloadCleanupReport> CleanAsync(WorkloadCleanupPlan plan, CancellationToken token)
    {
        if (plan.Owner != _owner || Interlocked.Exchange(ref plan.Used, 1) != 0)
        {
            return new(DotNetCliOutcome.Failed, null, string.Empty, string.Empty, false, "План уже использован или принадлежит другому сеансу. Повторите проверку.");
        }

        if ((_utcNow() - plan.CreatedUtc) > CleanupDefaults.PlanLifetime)
        {
            return new(DotNetCliOutcome.Failed, null, string.Empty, string.Empty, false, "План устарел. Повторите проверку.");
        }

        if (token.IsCancellationRequested)
        {
            return new(DotNetCliOutcome.Cancelled, null, string.Empty, string.Empty, false, null);
        }

        try
        {
            var current = await InspectAsync(token).ConfigureAwait(false);
            if (!SameContext(plan, current))
            {
                return new(
                    DotNetCliOutcome.Failed,
                    null,
                    current.InstalledWorkloads,
                    current.WorkloadListLogPath,
                    false,
                    "SDK или список workloads изменились после проверки. Повторите проверку.");
            }

            token.ThrowIfCancellationRequested();
            var result = await _run(
                    plan.Executable,
                    ["workload", "clean"],
                    plan.WorkingDirectory,
                    _logPath("workload-clean"),
                    CleanTimeout,
                    token,
                    _environment)
                .ConfigureAwait(false);

            return new(result.Outcome, result.ExitCode, result.OutputTail, result.LogPath, result.OutputTruncated, result.Error);
        }
        catch (OperationCanceledException)
        {
            return new(DotNetCliOutcome.Cancelled, null, string.Empty, string.Empty, false, null);
        }
        catch (WorkloadInspectionException exception)
        {
            var outcome = exception.Outcome == DotNetCliOutcome.Succeeded ? DotNetCliOutcome.Failed : exception.Outcome;
            return new(
                outcome,
                exception.ExitCode,
                exception.OutputTail,
                exception.LogPath,
                exception.OutputTruncated,
                outcome == DotNetCliOutcome.Cancelled ? null : exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException or Win32Exception)
        {
            return new(DotNetCliOutcome.Failed, null, string.Empty, string.Empty, false, exception.Message);
        }
    }

    private static string RequireSuccess(DotNetCliResult result, string message, bool allowEmpty = false)
    {
        if (!result.Succeeded || result.OutputTruncated || (!allowEmpty && string.IsNullOrWhiteSpace(result.OutputTail)))
        {
            throw new WorkloadInspectionException($"{message} {result.Error} {result.OutputTail}".Trim(), result);
        }

        return result.OutputTail;
    }

    private static bool SameContext(WorkloadCleanupPlan left, WorkloadCleanupPlan right)
    {
        return string.Equals(left.Executable, right.Executable, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.WorkingDirectory, right.WorkingDirectory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.SdkVersion, right.SdkVersion, StringComparison.Ordinal)
            && string.Equals(left.InstalledWorkloads, right.InstalledWorkloads, StringComparison.Ordinal);
    }

    private static string CreateLogPath(string logsDirectory, string name)
    {
        return Path.Combine(
            logsDirectory,
            "development",
            "dotnet-workloads-" + name + "-" + Guid.NewGuid().ToString("N") + ".log");
    }
}
