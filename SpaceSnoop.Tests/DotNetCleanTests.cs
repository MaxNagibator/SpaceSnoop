using SpaceSnoop.Core.Cleanup;

namespace SpaceSnoop.Tests;

[TestFixture]
public sealed class DotNetCleanTests
{
    private readonly List<(string[] Arguments, string Directory)> _calls = [];
    private string _root = null!;
    private string _project = null!;
    private DateTime _now;
    private string _sdk = null!;
    private string _executable = null!;
    private DotNetCliOutcome _versionOutcome;
    private DotNetCliOutcome _cleanOutcome;
    private DotNetCleanService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "SpaceSnoop.sln")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "dotnet-clean-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "trusted project.csproj");
        File.WriteAllText(_project, "<Project />");
        _now = DateTime.UtcNow;
        _sdk = "10.0.302";
        _executable = Path.Combine(_root, "dotnet.exe");
        File.WriteAllText(_executable, "test dotnet");
        _versionOutcome = DotNetCliOutcome.Succeeded;
        _cleanOutcome = DotNetCliOutcome.Succeeded;
        _calls.Clear();
        _service = new(() => _executable, Run, LogPath, () => _now);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [TestCase("relative.csproj")]
    [TestCase(@"\\server\share\project.csproj")]
    [TestCase(@"C:\projects\project.csproj:stream")]
    [TestCase(@"C:\projects\project.sln")]
    public async Task Ambiguous_or_unsupported_project_is_rejected_before_process(string path)
    {
        await Assert.ThrowsAsync<IOException>(() => _service.InspectAsync(path, new(), true, default));
        Assert.That(_calls, Is.Empty);
    }

    [TestCase("project")]
    [TestCase("replace")]
    [TestCase("global-new")]
    [TestCase("global-change")]
    [TestCase("global-remove")]
    [TestCase("sdk")]
    [TestCase("executable")]
    [TestCase("executable-replace")]
    [TestCase("executable-rewrite")]
    [TestCase("expire")]
    [TestCase("trust")]
    [TestCase("foreign")]
    public async Task Changed_context_cannot_start_clean(string change)
    {
        var global = Path.Combine(_root, "global.json");
        if (change is "global-change" or "global-remove")
        {
            File.WriteAllText(global, "{}");
        }

        var plan = await _service.InspectAsync(_project, new(), true, default);
        switch (change)
        {
            case "project":
                File.AppendAllText(_project, " ");
                break;

            case "replace":
                File.Move(_project, _project + ".old");
                File.WriteAllText(_project, "<Project />");
                break;

            case "global-new":
            case "global-change":
                File.WriteAllText(global, "{\"sdk\":{\"version\":\"10.0.303\"}}");
                break;

            case "global-remove":
                File.Delete(global);
                break;

            case "sdk":
                _sdk = "10.0.303";
                break;

            case "executable":
                _executable = @"C:\other\dotnet.exe";
                break;

            case "executable-replace":
                File.Move(_executable, _executable + ".old");
                await File.WriteAllTextAsync(_executable, "test dotnet");
                break;

            case "executable-rewrite":
                var written = File.GetLastWriteTimeUtc(_executable);
                await File.WriteAllTextAsync(_executable, "fake dotnet");
                File.SetLastWriteTimeUtc(_executable, written);
                break;

            case "expire":
                _now = _now.AddMinutes(16);
                break;
        }

        var service = change == "foreign" ? new(() => _executable, Run, LogPath, () => _now) : _service;
        var report = await service.CleanAsync(plan, change != "trust", default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.Error, Is.Not.Empty);
            Assert.That(_calls.All(x => x.Arguments[0] == "--version"), Is.True);
        });
    }

    [TestCase(DotNetCliOutcome.Cancelled)]
    [TestCase(DotNetCliOutcome.TimedOut)]
    [TestCase(DotNetCliOutcome.LoggingFailed)]
    [TestCase(DotNetCliOutcome.Failed)]
    public async Task Clean_report_keeps_partial_output_and_exact_exit(DotNetCliOutcome outcome)
    {
        var plan = await _service.InspectAsync(_project, new(), true, default);
        _cleanOutcome = outcome;
        var report = await _service.CleanAsync(plan, true, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Outcome, Is.EqualTo(outcome));
            Assert.That(report.ExitCode, Is.EqualTo(7));
            Assert.That(report.OutputTail, Is.EqualTo("partial clean output"));
            Assert.That(report.LogPath, Is.Not.Empty);
        });
    }

    [Test]
    public async Task Exact_arguments_and_project_directory_are_previewed_and_executed_once()
    {
        var plan = await _service.InspectAsync(_project, new("Release", "net10.0", "win-x64"), true, default);
        var expected = new[]
        {
            "clean", _project, "--configuration", "Release", "--framework", "net10.0", "--runtime", "win-x64", "--disable-build-servers",
        };

        Assert.Multiple(() =>
        {
            Assert.That(_calls.Any(x => x.Arguments[0] == "clean"), Is.False);
            Assert.That(plan.Arguments, Is.EqualTo(expected));
            Assert.That(plan.Command, Does.StartWith("\"" + _executable + "\" \"clean\""));
            Assert.That(plan.Command, Does.Contain("\"" + _project + "\""));
            Assert.That(plan.WorkingDirectory, Is.EqualTo(_root));
            Assert.That(plan.ExpiresUtc, Is.EqualTo(_now.AddMinutes(15)));
        });

        var report = await _service.CleanAsync(plan, true, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.True);
            var clean = _calls.Single(x => x.Arguments[0] == "clean");
            Assert.That(clean.Arguments, Is.EqualTo(expected));
            Assert.That(clean.Directory, Is.EqualTo(_root));
        });

        Assert.That((await _service.CleanAsync(plan, true, default)).Succeeded, Is.False);
        Assert.That(_calls.Count(x => x.Arguments[0] == "clean"), Is.EqualTo(1));
    }

    [Test]
    public async Task Executable_cannot_be_replaced_during_revalidation_or_clean()
    {
        var attempts = 0;
        var service = new DotNetCleanService(
            () => _executable,
            (executable, arguments, directory, log, timeout, token, environment) =>
            {
                Assert.Throws<IOException>(() => File.WriteAllText(_executable, "replacement"));
                Assert.Throws<IOException>(() => File.Move(_executable, _executable + ".old"));
                attempts++;
                return Run(executable, arguments, directory, log, timeout, token, environment);
            },
            LogPath,
            () => _now);

        var plan = await service.InspectAsync(_project, new(), true, default);
        var report = await service.CleanAsync(plan, true, default);
        Assert.That(report.Succeeded, Is.True);
        Assert.That(attempts, Is.EqualTo(3));
        Assert.That(await File.ReadAllTextAsync(_executable), Is.EqualTo("test dotnet"));
    }

    [TestCase(DotNetCliOutcome.Cancelled)]
    [TestCase(DotNetCliOutcome.TimedOut)]
    [TestCase(DotNetCliOutcome.LoggingFailed)]
    [TestCase(DotNetCliOutcome.Failed)]
    public async Task Failed_revalidation_preserves_outcome_and_log(DotNetCliOutcome outcome)
    {
        var plan = await _service.InspectAsync(_project, new(), true, default);
        _versionOutcome = outcome;
        var report = await _service.CleanAsync(plan, true, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Outcome, Is.EqualTo(outcome));
            Assert.That(report.LogPath, Is.Not.Empty);
            Assert.That(report.OutputTail, Is.EqualTo(_sdk));
            Assert.That(_calls.All(x => x.Arguments[0] == "--version"), Is.True);
        });
    }

    [Test]
    public async Task No_process_or_path_access_before_explicit_trust()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.InspectAsync("invalid", new(), false, default));
        Assert.That(_calls, Is.Empty);
    }

    [TestCase("-p:DeleteEverything=true")]
    [TestCase("Debug;Other")]
    [TestCase("$(Property)")]
    [TestCase("Debug\nRelease")]
    [TestCase("")]
    public async Task Options_reject_MSBuild_expressions_and_switches(string value)
    {
        await Assert.ThrowsAsync<IOException>(() => _service.InspectAsync(_project, new(value), true, default));
        await Assert.ThrowsAsync<IOException>(() => _service.InspectAsync(_project, new("Debug", value), true, default));
        await Assert.ThrowsAsync<IOException>(() => _service.InspectAsync(_project, new("Debug", null, value), true, default));
        Assert.That(_calls, Is.Empty);
    }

    [Test]
    public async Task Precancellation_does_not_launch_process()
    {
        var plan = await _service.InspectAsync(_project, new(), true, default);
        _calls.Clear();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var report = await _service.CleanAsync(plan, true, cancellation.Token);
        Assert.That(report.Cancelled, Is.True);
        Assert.That(_calls, Is.Empty);
    }

    [Test]
    public async Task Real_MSBuild_target_runs_only_after_explicit_trust_and_clean()
    {
        var sentinel = Path.Combine(_root, "clean-owned-sentinel.txt");
        File.WriteAllText(
            _project,
            "<Project><Target Name=\"Clean\"><WriteLinesToFile File=\"clean-owned-sentinel.txt\" Lines=\"executed\" Overwrite=\"true\" /></Target></Project>");

        var service = new DotNetCleanService(new(), LogPath, () => DateTime.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InspectAsync(_project, new(), false, default));
        Assert.That(File.Exists(sentinel), Is.False);
        var plan = await service.InspectAsync(_project, new(), true, default);
        Assert.That(File.Exists(sentinel), Is.False);
        var report = await service.CleanAsync(plan, true, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.True, report.Error + report.OutputTail);
            Assert.That(File.ReadAllText(sentinel).Trim(), Is.EqualTo("executed"));
            Assert.That(File.ReadAllText(report.LogPath), Does.Contain("--disable-build-servers"));
        });
    }

    [Test]
    public async Task Same_length_source_change_with_restored_timestamp_invalidates_hash()
    {
        var plan = await _service.InspectAsync(_project, new(), true, default);
        var written = File.GetLastWriteTimeUtc(_project);
        File.WriteAllText(_project, "<Changed />");
        File.SetLastWriteTimeUtc(_project, written);
        var report = await _service.CleanAsync(plan, true, default);
        Assert.Multiple(() =>
        {
            Assert.That(report.Succeeded, Is.False);
            Assert.That(_calls.Count, Is.EqualTo(1));
        });
    }

    private string LogPath(string name)
    {
        return Path.Combine(_root, name + Guid.NewGuid().ToString("N") + ".log");
    }

    private Task<DotNetCliResult> Run(
        string executable,
        IReadOnlyList<string> arguments,
        string directory,
        string log,
        TimeSpan timeout,
        CancellationToken token,
        IReadOnlyDictionary<string, string>? environment)
    {
        _calls.Add((arguments.ToArray(), directory));
        Assert.Multiple(() =>
        {
            Assert.That(environment!["MSBUILDDISABLENODEREUSE"], Is.EqualTo("1"));
            Assert.That(environment["DOTNET_CLI_USE_MSBUILD_SERVER"], Is.EqualTo("0"));
            Assert.That(executable, Is.EqualTo(_executable));
        });

        var version = arguments.SequenceEqual(
            new[]
            {
                "--version",
            });

        var outcome = version ? _versionOutcome : _cleanOutcome;
        return Task.FromResult(
            new DotNetCliResult(
                outcome,
                outcome == DotNetCliOutcome.Succeeded ? 0 : 7,
                version ? _sdk : "partial clean output",
                log,
                false,
                null));
    }
}
