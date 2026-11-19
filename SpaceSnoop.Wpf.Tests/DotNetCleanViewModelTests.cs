using KeepShell.Services;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class DotNetCleanViewModelTests
{
    private string _root = null!;
    private string _project = null!;
    private List<string[]> _calls = null!;

    private Func<string, IReadOnlyList<string>, string, string, TimeSpan, CancellationToken,
        IReadOnlyDictionary<string, string>?, Task<DotNetCliResult>>? _runOverride;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "SpaceSnoop.sln")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "dotnet-clean-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "Sample.csproj");
        File.WriteAllText(_project, "<Project />");
        File.WriteAllText(Path.Combine(_root, "dotnet.exe"), "test executable");
        _calls = [];
        _runOverride = null;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task Без_доверия_проверка_и_выбор_файла_не_запускают_dotnet()
    {
        var picker = new FakeFilePicker
        {
            OpenPath = _project,
        };

        var vm = Create(picker: picker);
        vm.PickProjectCommand.Execute(null);
        await vm.InspectCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.ProjectPath, Is.EqualTo(_project));
            Assert.That(vm.IsTrusted, Is.False);
            Assert.That(_calls, Is.Empty);
        });
    }

    [TestCase("path")]
    [TestCase("configuration")]
    [TestCase("framework")]
    [TestCase("runtime")]
    [TestCase("trust")]
    public async Task Изменение_входа_в_подтверждении_запрещает_запуск(string change)
    {
        DotNetCleanViewModel? vm = null;
        var dialogs = new NoopDialogs(
            show: _ =>
            {
                switch (change)
                {
                    case "path":
                        vm!.ProjectPath = Path.Combine(_root, "Other.csproj");
                        break;

                    case "configuration":
                        vm!.Configuration = "Release";
                        break;

                    case "framework":
                        vm!.Framework = "net10.0";
                        break;

                    case "runtime":
                        vm!.Runtime = "win-x64";
                        break;

                    case "trust":
                        vm!.IsTrusted = false;
                        break;
                }

                return true;
            });

        vm = Create(dialogs);
        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(_calls.Any(x => x.Contains("clean")), Is.False);
            Assert.That(vm.CanClean, Is.False);
            Assert.That(vm.StatusText, Does.Contain("изменился"));
        });
    }

    [Test]
    public async Task Нормализованный_путь_проекта_не_выдаётся_за_смену_входа()
    {
        var vm = Create();
        vm.ProjectPath = _root + @"\\" + Path.GetFileName(_project);
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(_calls.Any(x => x.Contains("clean")), Is.True, vm.StatusText);
    }

    [Test]
    public async Task Отмена_запущенной_команды_передаёт_токен_и_оставляет_частичный_исход()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runOverride = async (exe, args, cwd, log, timeout, token, env) =>
        {
            if (args.Contains("clean"))
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            return new(DotNetCliOutcome.Succeeded, 0, "10.0.303", log, false, null);
        };

        var vm = Create();
        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        var clean = vm.CleanCommand.ExecuteAsync(null);
        await started.Task;
        vm.CancelCommand!.Execute(null);
        await clean;
        Assert.That(vm.ResultText, Does.Contain("частичным"));
    }

    [Test]
    public async Task Проверка_показывает_контекст_и_изменение_параметров_сбрасывает_план()
    {
        var vm = Create();
        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        Assert.That(vm.DisplayPlan!.Command, Does.Contain("--configuration"));
        Assert.That(_calls, Has.Count.EqualTo(1));
        vm.Framework = "net10.0";
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasPlan, Is.False);
            Assert.That(vm.CanClean, Is.False);
            Assert.That(vm.LogPath, Is.Null);
        });
    }

    [Test]
    public async Task Родительская_страница_блокирует_соседние_очистки_во_время_проверки()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runOverride = async (exe, args, cwd, log, timeout, token, env) =>
        {
            if (args.Contains("--version"))
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            return new(DotNetCliOutcome.Succeeded, 0, "10.0.303", log, false, null);
        };

        var projects = new ProjectCleanupViewModel(
            new(),
            new NoopDialogs(),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            new MemorySettings(),
            NullLogger<ProjectCleanupViewModel>.Instance);

        var workloads = new WorkloadCleanupViewModel(
            new(_root),
            new NoopDialogs(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            NullLogger<WorkloadCleanupViewModel>.Instance);

        var vm = Create();
        var parent = new DeveloperCleanupViewModel(
            new(Path.Combine(_root, "logs")),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            projects,
            workloads,
            vm);

        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        var inspect = vm.InspectCommand.ExecuteAsync(null);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Multiple(() =>
            {
                Assert.That(parent.IsBusy, Is.True);
                Assert.That(parent.RefreshCommand.CanExecute(null), Is.False);
                Assert.That(workloads.InspectCommand.CanExecute(null), Is.False);
                Assert.That(projects.FindCommand.CanExecute(null), Is.False);
            });

            Assert.That(parent.CancelCommand?.CanExecute(null), Is.True);
            parent.CancelCommand!.Execute(null);
            await inspect.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(vm.StatusText, Is.EqualTo("Проверка отменена."));
        }
        finally
        {
            if (!inspect.IsCompleted)
            {
                vm.CancelCommand?.Execute(null);
                await inspect;
            }
        }
    }

    [Test]
    public async Task Смена_пути_сбрасывает_доверие_а_отказ_не_запускает_очистку()
    {
        ConfirmDialogViewModel? confirmation = null;
        var cancelWasEnabled = true;
        DotNetCleanViewModel? vm = null;
        var dialogs = new NoopDialogs(
            show: model =>
            {
                confirmation = model as ConfirmDialogViewModel;
                cancelWasEnabled = vm!.CanCancel;
                return false;
            });

        vm = Create(dialogs);
        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        vm.ProjectPath = Path.Combine(_root, "Other.csproj");
        Assert.Multiple(() =>
        {
            Assert.That(confirmation!.Warning, Does.Contain("вне каталога проекта"));
            Assert.That(cancelWasEnabled, Is.False);
            Assert.That(_calls.Any(x => x.Contains("clean")), Is.False);
            Assert.That(vm.IsTrusted, Is.False);
            Assert.That(vm.CanClean, Is.False);
        });
    }

    [Test]
    public async Task Успешное_и_ошибочное_выполнение_сохраняют_вывод_и_путь_журнала()
    {
        var vm = Create();
        vm.ProjectPath = _project;
        vm.IsTrusted = true;
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(vm.ResultText, Does.Contain("Завершено"));
        Assert.That(vm.LogPath, Does.Contain("run.log"));

        var failed = Create();
        failed.ProjectPath = _project;
        failed.IsTrusted = true;
        await failed.InspectCommand.ExecuteAsync(null);
        _runOverride = (exe, args, cwd, log, timeout, token, env) => Task.FromResult(
            new DotNetCliResult(
                args.Contains("--version") ? DotNetCliOutcome.Succeeded : DotNetCliOutcome.Failed,
                args.Contains("--version") ? 0 : 7,
                args.Contains("--version") ? "10.0.303" : "failure tail",
                log,
                false,
                "failed"));

        await failed.CleanCommand.ExecuteAsync(null);
        Assert.That(failed.ResultText, Does.Contain("failure tail"));
    }

    private DotNetCleanService CreateService()
    {
        return new(
            () => Path.Combine(_root, "dotnet.exe"),
            (executable, args, workingDirectory, logPath, timeout, token, environment) =>
            {
                _calls.Add(args.ToArray());
                if (_runOverride is not null)
                {
                    return _runOverride(executable, args, workingDirectory, logPath, timeout, token, environment);
                }

                var text = args.SequenceEqual(
                    new[]
                    {
                        "--version",
                    })
                    ? "10.0.303"
                    : "partial clean output";

                return Task.FromResult(new DotNetCliResult(DotNetCliOutcome.Succeeded, 0, text, logPath, false, null));
            },
            name => Path.Combine(_root, name + ".log"),
            () => DateTime.UtcNow);
    }

    private DotNetCleanViewModel Create(IDialogService? dialogs = null, FakeFilePicker? picker = null)
    {
        return new(
            CreateService(),
            dialogs ?? new NoopDialogs(showResult: true),
            picker ?? new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            NullLogger<DotNetCleanViewModel>.Instance);
    }
}
