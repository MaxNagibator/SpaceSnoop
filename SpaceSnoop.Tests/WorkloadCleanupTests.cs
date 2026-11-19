using SpaceSnoop.Core.Cleanup;
using System.Diagnostics;
using System.Text;

namespace SpaceSnoop.Tests;

[TestFixture]
public sealed class WorkloadCleanupTests
{
    private string _scratch = null!;

    [SetUp]
    public void SetUp()
    {
        _scratch = Path.Combine(RepositoryRoot(), ".kc", "scratch", "workload-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, true);
        }
    }

    [Test]
    public async Task Runner_записывает_stdout_stderr_ненулевой_код_и_ограничивает_хвост()
    {
        var script = WriteScript("[Console]::Write('out-'); [Console]::Error.Write('err-'); [Console]::Write('x' * 30000); exit 7");
        var runner = CreateRunner(script);
        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "runner.log"),
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        var log = await File.ReadAllTextAsync(result.LogPath);
        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.Failed));
            Assert.That(result.ExitCode, Is.EqualTo(7));
            Assert.That(result.OutputTruncated, Is.True);
            Assert.That(result.OutputTail.Length, Is.LessThanOrEqualTo(24_000));
            Assert.That(log, Does.Contain("out-"));
            Assert.That(log, Does.Contain("err-"));
            Assert.That(log, Does.Contain("Executable: " + Path.Combine(_scratch, "dotnet.exe")));
            Assert.That(log, Does.Contain("Arguments: [\"probe\"]"));
            Assert.That(log, Does.Contain("Working directory: " + Path.GetFullPath(_scratch)));
            Assert.That(log, Does.Contain("UTC start:"));
            Assert.That(log, Does.Contain("UTC end:"));
        });
    }

    [Test]
    public async Task Runner_не_запускает_процесс_при_предварительной_отмене()
    {
        var launched = false;
        var runner = new DotNetCli(
            () => _scratch,
            _ => true,
            info =>
            {
                launched = true;
                return new()
                {
                    StartInfo = info,
                };
            });

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "cancelled-before-start.log"),
            TimeSpan.FromSeconds(2),
            new(true));

        Assert.Multiple(() =>
        {
            Assert.That(launched, Is.False);
            Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.Cancelled));
        });
    }

    [Test]
    public async Task Runner_ограничивает_время_процесса()
    {
        var script = WriteScript("Start-Sleep -Seconds 60");
        var runner = CreateRunner(script);

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "timeout.log"),
            TimeSpan.FromMilliseconds(300),
            CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.TimedOut));
    }

    [Test]
    public async Task Runner_останавливает_свой_процесс_если_полный_журнал_перестал_записываться()
    {
        var stream = new FailAfterStartStream();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new DotNetCli(
            () => _scratch,
            _ => true,
            _ =>
            {
                stream.FailWrites = true;
                var process = StartPowerShell("Write-Output ('x' * 50000); Start-Sleep -Seconds 60");
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => exited.TrySetResult();
                return process;
            },
            _ => stream);

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "write-failure.log"),
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        await exited.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.LoggingFailed));
            Assert.That(result.Error, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Runner_отменяет_только_запущенный_процесс()
    {
        var script = WriteScript("Start-Sleep -Seconds 60");
        var runner = CreateRunner(script);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "cancel.log"),
            TimeSpan.FromSeconds(10),
            cancellation.Token);

        Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.Cancelled));
    }

    [Test]
    public async Task Runner_передаёт_аргументы_каталог_и_нейтральные_параметры_процесса()
    {
        var script = WriteScript("Write-Output 'ready'");
        var observed = false;
        var runner = new DotNetCli(
            () => _scratch,
            _ => true,
            info =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        info.ArgumentList,
                        Is.EqualTo(
                            new[]
                            {
                                "probe", "one value",
                            }));

                    Assert.That(info.WorkingDirectory, Is.EqualTo(Path.GetFullPath(_scratch)));
                    Assert.That(info.UseShellExecute, Is.False);
                    Assert.That(info.Environment["DOTNET_CLI_UI_LANGUAGE"], Is.EqualTo("en"));
                    Assert.That(info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"], Is.EqualTo("1"));
                    Assert.That(info.Environment["DOTNET_NOLOGO"], Is.EqualTo("1"));
                    Assert.That(info.Environment["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"], Is.EqualTo("true"));
                });

                observed = true;
                return StartPowerShellFile(script);
            });

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe", "one value"],
            _scratch,
            Path.Combine(_scratch, "context.log"),
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(observed, Is.True);
            Assert.That(result.Succeeded, Is.True);
        });
    }

    [Test]
    public async Task Runner_сообщает_об_ошибке_создания_полного_журнала_до_запуска()
    {
        var launched = false;
        var runner = new DotNetCli(
            () => _scratch,
            _ => true,
            _ =>
            {
                launched = true;
                return StartPowerShell("exit 0");
            });

        var logPath = Path.Combine(_scratch, "existing.log");
        await File.WriteAllTextAsync(logPath, "preexisting");

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            logPath,
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.LoggingFailed));
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(launched, Is.False);
        });
    }

    [Test]
    public async Task Runner_сообщает_отказ_запуска_без_ожидания_несуществующего_процесса()
    {
        var runner = new DotNetCli(
            () => _scratch,
            _ => true,
            info => new()
            {
                StartInfo = info,
            });

        var result = await runner.RunAsync(
            runner.ResolveExecutable(),
            ["probe"],
            _scratch,
            Path.Combine(_scratch, "launch-failure.log"),
            TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.That(result.Outcome, Is.EqualTo(DotNetCliOutcome.LaunchFailed));
    }

    [Test]
    public async Task Изменившийся_список_workloads_отклоняет_план()
    {
        var calls = new List<string[]>();
        var installed = "sdk-manifest-1";
        var service = CreateService(calls, () => installed);
        var plan = await service.InspectAsync(CancellationToken.None);
        installed = "sdk-manifest-2";

        var report = await service.CleanAsync(plan, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.Error, Does.Contain("изменились"));
            Assert.That(
                calls.Any(args => args.SequenceEqual(
                    new[]
                    {
                        "workload", "clean",
                    })),
                Is.False);
        });
    }

    [Test]
    public async Task Отмена_до_очистки_не_запускает_команду()
    {
        var calls = new List<string[]>();
        var service = CreateService(calls);
        var plan = await service.InspectAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = await service.CleanAsync(plan, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(report.Cancelled, Is.True);
            Assert.That(
                calls.Any(args => args.SequenceEqual(
                    new[]
                    {
                        "workload", "clean",
                    })),
                Is.False);
        });
    }

    [Test]
    public async Task Очистка_запускает_только_обычную_команду_workload_clean()
    {
        var calls = new List<string[]>();
        var service = CreateService(calls);
        var plan = await service.InspectAsync(CancellationToken.None);

        var report = await service.CleanAsync(plan, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.True);
            Assert.That(
                calls,
                Does.Contain(
                    new[]
                    {
                        "workload", "clean",
                    }));

            Assert.That(calls.SelectMany(args => args), Does.Not.Contain("--all"));
            Assert.That(report.OutputTail, Does.Contain("workload clean"));
        });
    }

    [Test]
    public async Task Ошибка_проверки_sdk_не_создаёт_план()
    {
        var service = CreateService([], versionExitCode: 5);

        await Assert.ThrowsAsync<WorkloadInspectionException>(() => service.InspectAsync(CancellationToken.None));
    }

    [Test]
    public async Task План_истекает_и_используется_один_раз()
    {
        var calls = new List<string[]>();
        var now = DateTime.UtcNow;
        var service = CreateService(calls, utcNow: () => now);
        var expired = await service.InspectAsync(CancellationToken.None);
        now = now.AddMinutes(16);
        var expiredReport = await service.CleanAsync(expired, CancellationToken.None);
        now = now.AddMinutes(-16);
        var singleUse = await service.InspectAsync(CancellationToken.None);
        var first = await service.CleanAsync(singleUse, CancellationToken.None);
        var second = await service.CleanAsync(singleUse, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(expiredReport.Error, Does.Contain("устарел"));
            Assert.That(first.Succeeded, Is.True);
            Assert.That(second.Error, Does.Contain("уже использован"));
        });
    }

    [TestCase(DotNetCliOutcome.Cancelled)]
    [TestCase(DotNetCliOutcome.TimedOut)]
    [TestCase(DotNetCliOutcome.LoggingFailed)]
    public async Task Повторная_проверка_сохраняет_исход_и_журнал_отказа(DotNetCliOutcome outcome)
    {
        var rechecking = false;
        var cleaned = false;
        var service = new WorkloadCleanupService(
            () => Path.Combine(_scratch, "dotnet.exe"),
            (executable, args, directory, log, timeout, token, environment) =>
            {
                cleaned |= args.SequenceEqual(
                    new[]
                    {
                        "workload", "clean",
                    });

                return Task.FromResult(
                    rechecking
                        ? new(outcome, null, "partial inspection", log, false, "controlled failure")
                        : new DotNetCliResult(
                            DotNetCliOutcome.Succeeded,
                            0,
                            args.SequenceEqual(
                                new[]
                                {
                                    "--version",
                                })
                                ? "10.0.303"
                                : "",
                            log,
                            false,
                            null));
            },
            () => _scratch,
            name => Path.Combine(_scratch, name + ".log"),
            () => DateTime.UtcNow);

        var plan = await service.InspectAsync(CancellationToken.None);
        rechecking = true;
        var report = await service.CleanAsync(plan, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(report.Outcome, Is.EqualTo(outcome));
            Assert.That(report.OutputTail, Is.EqualTo("partial inspection"));
            Assert.That(report.LogPath, Does.EndWith("inspect-version.log"));
            Assert.That(cleaned, Is.False);
        });
    }

    [Test]
    public async Task Пустой_список_workloads_не_запрещает_обычную_очистку()
    {
        var calls = new List<string[]>();
        var service = CreateService(calls, () => string.Empty);
        var plan = await service.InspectAsync(CancellationToken.None);

        var report = await service.CleanAsync(plan, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(plan.InstalledWorkloads, Is.Empty);
            Assert.That(report.Succeeded, Is.True);
            Assert.That(
                calls,
                Does.Contain(
                    new[]
                    {
                        "workload", "clean",
                    }));
        });
    }

    private WorkloadCleanupService CreateService(
        List<string[]> calls,
        Func<string>? installed = null,
        Func<DateTime>? utcNow = null,
        int versionExitCode = 0)
    {
        var cli = CreateRunner(
            null,
            info =>
            {
                var args = info.ArgumentList.ToArray();
                calls.Add(args);
                var output = args.SequenceEqual(
                    new[]
                    {
                        "--version",
                    })
                    ? "[Console]::WriteLine('10.0.303'); exit " + versionExitCode
                    : args.SequenceEqual(
                        new[]
                        {
                            "workload", "list",
                        })
                        ? "[Console]::WriteLine('" + (installed?.Invoke() ?? "sdk-manifest-1") + "')"
                        : "[Console]::WriteLine('workload clean'); exit 0";

                return StartPowerShell(output);
            });

        return new(
            cli,
            () => _scratch,
            name => Path.Combine(_scratch, name + "-" + Guid.NewGuid().ToString("N") + ".log"),
            utcNow ?? (() => DateTime.UtcNow));
    }

    private DotNetCli CreateRunner(string? scriptPath, Func<ProcessStartInfo, Process>? start = null)
    {
        return new(() => _scratch, _ => true, info => start?.Invoke(info) ?? StartPowerShellFile(scriptPath!));
    }

    private string WriteScript(string body)
    {
        var path = Path.Combine(_scratch, Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(path, body);
        return path;
    }

    private static Process StartPowerShell(string command)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var process = new Process
        {
            StartInfo = new(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);
        return process;
    }

    private static Process StartPowerShellFile(string script)
    {
        var process = StartPowerShell(string.Empty);
        process.StartInfo.ArgumentList.Clear();
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(script);
        return process;
    }

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "SpaceSnoop.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FailAfterStartStream : MemoryStream
    {
        internal bool FailWrites { get; set; }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (FailWrites)
            {
                throw new IOException("Controlled log write failure.");
            }

            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (FailWrites)
            {
                throw new IOException("Controlled log write failure.");
            }

            base.Write(buffer);
        }
    }
}
