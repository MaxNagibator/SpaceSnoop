using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Security;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SpaceSnoop.Core.Cleanup;

public sealed class DotNetCleanService
{
    private static readonly IReadOnlyDictionary<string, string> ProcessEnvironment =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>
            {
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0",
            });

    private readonly Guid _owner = Guid.NewGuid();
    private readonly Func<string> _resolveExecutable;

    private readonly Func<string, IReadOnlyList<string>, string, string, TimeSpan, CancellationToken,
        IReadOnlyDictionary<string, string>?, Task<DotNetCliResult>> _run;

    private readonly Func<string, string> _logPath;
    private readonly Func<DateTime> _utcNow;

    public DotNetCleanService(string logsDirectory) : this(
        new(),
        name => Path.Combine(
            logsDirectory,
            "development",
            "dotnet-clean-" + name + "-" + Guid.NewGuid().ToString("N") + ".log"),
        () => DateTime.UtcNow)
    {
    }

    internal DotNetCleanService(DotNetCli cli, Func<string, string> logPath, Func<DateTime> utcNow)
        : this(cli.ResolveExecutable, cli.RunAsync, logPath, utcNow)
    {
    }

    internal DotNetCleanService(
        Func<string> resolveExecutable,
        Func<string, IReadOnlyList<string>, string, string, TimeSpan, CancellationToken,
            IReadOnlyDictionary<string, string>?, Task<DotNetCliResult>> run,
        Func<string, string> logPath,
        Func<DateTime> utcNow)
    {
        _resolveExecutable = resolveExecutable;
        _run = run;
        _logPath = logPath;
        _utcNow = utcNow;
    }

    public async Task<DotNetCleanPlan> InspectAsync(
        string projectPath,
        DotNetCleanOptions options,
        bool trusted,
        CancellationToken token)
    {
        RequireTrust(trusted);
        token.ThrowIfCancellationRequested();
        var project = Validate(projectPath, options);
        using var source = await Task.Run(() => new SourceLease(project), token).ConfigureAwait(false);
        var executable = _resolveExecutable();
        using var executableLease = await Task.Run(() => new ExecutableLease(executable), token).ConfigureAwait(false);
        var version = await VersionAsync(executable, Path.GetDirectoryName(project)!, token).ConfigureAwait(false);
        using var current = await Task.Run(() => new SourceLease(project), token).ConfigureAwait(false);
        if (!SameInput(current.Input, source.Input))
        {
            throw new IOException("Проект или global.json изменился во время проверки. Повторите проверку.");
        }

        var arguments = new List<string>
        {
            "clean",
            project,
            "--configuration",
            options.Configuration,
        };

        if (!string.IsNullOrEmpty(options.Framework))
        {
            arguments.AddRange(["--framework", options.Framework]);
        }

        if (!string.IsNullOrEmpty(options.Runtime))
        {
            arguments.AddRange(["--runtime", options.Runtime]);
        }

        arguments.Add("--disable-build-servers");
        return new(
            _owner,
            executable,
            project,
            version.OutputTail.Trim(),
            options,
            arguments.AsReadOnly(),
            source.Input,
            executableLease.Input,
            _utcNow(),
            version.LogPath);
    }

    public async Task<DotNetCleanReport> CleanAsync(DotNetCleanPlan plan, bool trusted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!trusted)
        {
            return Failed("Доверие проекту отозвано. Команда не запущена.");
        }

        if (plan.Owner != _owner
            || Interlocked.Exchange(ref plan.Used, 1) != 0
            || (_utcNow() - plan.CreatedUtc) > CleanupDefaults.PlanLifetime)
        {
            return Failed("План устарел, использован или принадлежит другому сеансу. Повторите проверку.");
        }

        try
        {
            token.ThrowIfCancellationRequested();
            var project = Validate(plan.ProjectPath, plan.Options);
            using var source = await Task.Run(() => new SourceLease(project), token).ConfigureAwait(false);
            using var executableLease = await Task.Run(() => new ExecutableLease(plan.Executable), token).ConfigureAwait(false);
            if (!SameInput(source.Input, plan.Input)
                || executableLease.Input != plan.ExecutableInput
                || !string.Equals(_resolveExecutable(), plan.Executable, StringComparison.OrdinalIgnoreCase))
            {
                return Failed("Проект, global.json или dotnet изменился после проверки. Повторите проверку.");
            }

            var version = await VersionAsync(plan.Executable, plan.WorkingDirectory, token).ConfigureAwait(false);
            using var current = await Task.Run(() => new SourceLease(project), token).ConfigureAwait(false);
            if (version.OutputTail.Trim() != plan.SdkVersion || !SameInput(current.Input, plan.Input))
            {
                return new(
                    DotNetCliOutcome.Failed,
                    version.ExitCode,
                    version.OutputTail,
                    version.LogPath,
                    false,
                    "SDK, проект или global.json изменился после проверки. Повторите проверку.");
            }

            token.ThrowIfCancellationRequested();
            var result = await _run(
                    plan.Executable,
                    plan.Arguments,
                    plan.WorkingDirectory,
                    _logPath("run"),
                    TimeSpan.FromMinutes(30),
                    token,
                    ProcessEnvironment)
                .ConfigureAwait(false);

            return new(result.Outcome, result.ExitCode, result.OutputTail, result.LogPath, result.OutputTruncated, result.Error);
        }
        catch (OperationCanceledException)
        {
            return new(DotNetCliOutcome.Cancelled, null, string.Empty, string.Empty, false, null);
        }
        catch (DotNetCleanInspectionException exception)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException
            or InvalidOperationException or Win32Exception)
        {
            return Failed(exception.Message);
        }
    }

    private async Task<DotNetCliResult> VersionAsync(string executable, string directory, CancellationToken token)
    {
        var result = await _run(
                executable,
                ["--version"],
                directory,
                _logPath("sdk"),
                TimeSpan.FromSeconds(30),
                token,
                ProcessEnvironment)
            .ConfigureAwait(false);

        if (!result.Succeeded
            || result.OutputTruncated
            || !Regex.IsMatch(
                result.OutputTail.Trim(),
                @"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$",
                RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1)))
        {
            throw new DotNetCleanInspectionException($"Не удалось определить SDK. {result.Error} {result.OutputTail}".Trim(), result);
        }

        return result;
    }

    private static DotNetCleanReport Failed(string error)
    {
        return new(DotNetCliOutcome.Failed, null, string.Empty, string.Empty, false, error);
    }

    private static bool SameInput(DotNetCleanInput left, DotNetCleanInput right)
    {
        return left.Directory.Path == right.Directory.Path
            && left.Directory.Volume == right.Directory.Volume
            && left.Directory.Id == right.Directory.Id
            && left.Directory.Created == right.Directory.Created
            && left.Project == right.Project
            && left.ProjectHash == right.ProjectHash
            && left.GlobalPath == right.GlobalPath
            && left.Global == right.Global
            && left.GlobalHash == right.GlobalHash;
    }

    private static void RequireTrust(bool trusted)
    {
        if (!trusted)
        {
            throw new InvalidOperationException("Подтвердите доверие проекту и его импортам до запуска dotnet.");
        }
    }

    private static string Validate(string projectPath, DotNetCleanOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(options.Configuration))
        {
            throw new IOException("Укажите configuration.");
        }

        foreach (var value in new[]
        {
            options.Configuration, options.Framework, options.Runtime,
        })
        {
            if (value is null)
            {
                continue;
            }

            if (string.IsNullOrEmpty(value)
                || !Regex.IsMatch(
                    value,
                    @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$",
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)))
            {
                throw new IOException("Configuration, framework и runtime должны быть отдельными именами без параметров и выражений MSBuild.");
            }
        }

        if (!Path.IsPathFullyQualified(projectPath)
            || projectPath.Length < 3
            || projectPath[1] != ':'
            || projectPath[2] != '\\'
            || projectPath.AsSpan(2).Contains(':')
            || projectPath.Split('\\').Any(x => x.EndsWith(' ') || x.EndsWith('.')))
        {
            throw new IOException("Выберите локальный проект с однозначным абсолютным путём.");
        }

        var project = Path.GetFullPath(projectPath);
        if (Path.GetExtension(project).ToLowerInvariant() is not (".csproj" or ".fsproj" or ".vbproj"))
        {
            throw new IOException("Выберите файл csproj, fsproj или vbproj.");
        }

        return project;
    }

    private sealed class ExecutableLease : IDisposable
    {
        private readonly CacheFileAccess.DirectoryLease _directory;
        private readonly FileStream _stream;

        internal DotNetCleanExecutable Input { get; }

        internal ExecutableLease(string path)
        {
            _directory = CacheFileAccess.LockDirectory(Path.GetDirectoryName(path)!);
            try
            {
                _stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                var stamp = CacheFileAccess.Read(_stream.SafeFileHandle, path);
                Input = new(stamp, Convert.ToHexString(SHA256.HashData(_stream)));
            }
            catch
            {
                _stream?.Dispose();
                _directory.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _stream.Dispose();
            _directory.Dispose();
        }
    }

    private sealed class SourceLease : IDisposable
    {
        private readonly CacheFileAccess.DirectoryLease _directory;
        private readonly List<FileStream> _files = [];
        internal DotNetCleanInput Input { get; }

        internal SourceLease(string project)
        {
            var directory = Path.GetDirectoryName(project)!;
            _directory = CacheFileAccess.LockDirectory(directory);
            try
            {
                var (stamp, hash) = Read(project);
                string? global = null;
                for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
                {
                    var candidate = Path.Combine(current.FullName, "global.json");
                    if (File.Exists(candidate) || Directory.Exists(candidate))
                    {
                        global = candidate;
                        break;
                    }
                }

                var globalFile = global is null ? ((CacheFileStamp Stamp, string Hash)?)null : Read(global);
                Input = new(_directory.Stamp, stamp, hash, global, globalFile?.Stamp, globalFile?.Hash);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            foreach (var stream in _files)
            {
                stream.Dispose();
            }

            _directory.Dispose();
        }

        private (CacheFileStamp Stamp, string Hash) Read(string path)
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            _files.Add(stream);
            var stamp = CacheFileAccess.Read(stream.SafeFileHandle, path);
            return (stamp, Convert.ToHexString(SHA256.HashData(stream)));
        }
    }
}
