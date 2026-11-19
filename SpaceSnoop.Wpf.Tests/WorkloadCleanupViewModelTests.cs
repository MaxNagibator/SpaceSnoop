using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class WorkloadCleanupViewModelTests
{
    private string _root = null!;
    private List<string[]> _calls = null!;
    private string _installed = "sdk-manifest-1";

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
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "workload-cleanup-viewmodel-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _calls = [];
        _installed = "sdk-manifest-1";
        _runOverride = null;
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task Изменение_контекста_во_время_подтверждения_блокирует_очистку()
    {
        var vm = Create(
            new NoopDialogs(
                show: _ =>
                {
                    _installed = "sdk-manifest-2";
                    return true;
                }));

        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ResultText, Does.Contain("изменились"));
            Assert.That(
                _calls.Any(args => args.SequenceEqual(
                    new[]
                    {
                        "workload", "clean",
                    })),
                Is.False);

            Assert.That(vm.LogPath, Does.EndWith("inspect-workloads.log"));
        });
    }

    [Test]
    public async Task Отказ_от_подтверждения_не_запускает_очистку()
    {
        ConfirmDialogViewModel? confirmation = null;
        var dialogs = new NoopDialogs(
            show: model =>
            {
                confirmation = model as ConfirmDialogViewModel;
                return false;
            });

        var vm = Create(dialogs);
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(confirmation, Is.Not.Null);
            Assert.That(confirmation!.Lines.Select(x => x.PlainText), Does.Contain("Будет выполнена команда: dotnet workload clean"));
            Assert.That(confirmation.Lines.Select(x => x.PlainText).Any(text => text.Contains("необратимо")), Is.True);
            Assert.That(confirmation.Lines.Select(x => x.PlainText).Any(text => text.Contains("предварительного просмотра нет")), Is.True);
            Assert.That(confirmation.Lines.Select(x => x.PlainText).Any(text => text.Contains("SDK, Runtime, инструменты")), Is.True);
            Assert.That(vm.CleanCommand.CanExecute(null), Is.True);
            Assert.That(
                _calls.Any(args => args.SequenceEqual(
                    new[]
                    {
                        "workload", "clean",
                    })),
                Is.False);
        });
    }

    [Test]
    public async Task Отмена_запущенной_очистки_оставляет_частичный_результат_и_журнал()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runOverride = async (executable, args, workingDirectory, logPath, timeout, token, environment) =>
        {
            if (args.SequenceEqual(
                new[]
                {
                    "workload", "clean",
                }))
            {
                await File.WriteAllTextAsync(logPath, "controlled workload output");
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                }
                catch (OperationCanceledException)
                {
                    return new(DotNetCliOutcome.Cancelled, null, "partial output", logPath, false, null);
                }
            }

            var output = args.SequenceEqual(
                new[]
                {
                    "--version",
                })
                ? "10.0.303"
                : _installed;

            return new(DotNetCliOutcome.Succeeded, 0, output, logPath, false, null);
        };

        var vm = Create(new NoopDialogs(showResult: true));
        await vm.InspectCommand.ExecuteAsync(null);
        var cleaning = vm.CleanCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(vm.IsBusy, Is.True);
        vm.CancelCommand!.Execute(null);
        await cleaning;

        Assert.Multiple(() =>
        {
            Assert.That(vm.ResultText, Does.Contain("Отменено"));
            Assert.That(vm.ResultText, Does.Contain("изменения не отменены"));
            Assert.That(vm.ResultText, Does.Contain("partial output"));
            Assert.That(vm.LogPath, Does.EndWith("workload-clean.log"));
            Assert.That(File.Exists(vm.LogPath), Is.True);
        });
    }

    [Test]
    public async Task Отмена_проверки_sdk_показывается_как_отмена_с_журналом()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _runOverride = async (executable, args, directory, log, timeout, token, environment) =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                return new(DotNetCliOutcome.Cancelled, null, "partial inspection", log, false, null);
            }

            throw new InvalidOperationException("Expected cancellation");
        };

        var vm = Create();
        var inspection = vm.InspectCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.CancelCommand!.Execute(null);
        await inspection;
        Assert.Multiple(() =>
        {
            Assert.That(vm.StatusText, Is.EqualTo("Проверка отменена."));
            Assert.That(vm.LogPath, Does.EndWith("inspect-version.log"));
            Assert.That(vm.ResultText, Is.EqualTo("partial inspection"));
            Assert.That(vm.CleanCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task Родительская_страница_маршрутизирует_проверку_и_занятость()
    {
        var dialogs = new PausedDialogs();
        var workload = new WorkloadCleanupViewModel(
            CreateService(),
            dialogs,
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            NullLogger<WorkloadCleanupViewModel>.Instance);

        var projects = new ProjectCleanupViewModel(
            new(),
            new NoopDialogs(),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            new MemorySettings(),
            NullLogger<ProjectCleanupViewModel>.Instance);

        var parent = new DeveloperCleanupViewModel(
            new(Path.Combine(_root, "logs")),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            projects,
            workload,
            new(
                new(_root),
                new NoopDialogs(),
                new FakeFilePicker(),
                new FakeShellLauncher(),
                new FakeUiDispatcher(),
                NullLogger<DotNetCleanViewModel>.Instance));

        parent.SelectedAreaIndex = 2;
        await parent.RefreshCommand.ExecuteAsync(null);
        var cleaning = workload.CleanCommand.ExecuteAsync(null);
        try
        {
            Assert.That(parent.IsBusy, Is.True);
            Assert.That(parent.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(parent.CleanCommand.CanExecute(null), Is.False);
            Assert.That(projects.FindCommand.CanExecute(null), Is.False);
            Assert.That(parent.CancelCommand?.CanExecute(null), Is.True);

            parent.CancelCommand!.Execute(null);
            dialogs.Confirmation.SetResult(true);
            await cleaning;
            Assert.That(_calls.Any(args => args.SequenceEqual(["workload", "clean"])), Is.False);
        }
        finally
        {
            dialogs.Confirmation.TrySetResult(false);
            await cleaning;
        }

        Assert.That(parent.IsBusy, Is.False);
    }

    [Test]
    public async Task Успешный_запуск_показывает_исход_и_журнал_без_оценки_освобождённого_места()
    {
        var vm = Create(new NoopDialogs(showResult: true));
        await vm.InspectCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.ResultText, Does.Contain("Завершено"));
            Assert.That(vm.ResultText, Does.Not.Contain("данных:"));
            Assert.That(vm.LogPath, Does.EndWith("workload-clean.log"));
            Assert.That(
                _calls,
                Does.Contain(
                    new[]
                    {
                        "workload", "clean",
                    }));
        });
    }

    private WorkloadCleanupService CreateService()
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

                var output = args.SequenceEqual(
                        new[]
                        {
                            "--version",
                        }) ? "10.0.303"
                    : args.SequenceEqual(
                        new[]
                        {
                            "workload", "list",
                        }) ? _installed : "cleaned";

                return Task.FromResult(new DotNetCliResult(DotNetCliOutcome.Succeeded, 0, output, logPath, false, null));
            },
            () => _root,
            name => Path.Combine(_root, name + ".log"),
            () => DateTime.UtcNow);
    }

    private WorkloadCleanupViewModel Create(IDialogService? dialogs = null)
    {
        return new(
            CreateService(),
            dialogs ?? new NoopDialogs(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            NullLogger<WorkloadCleanupViewModel>.Instance);
    }

    private sealed class PausedDialogs : IDialogService
    {
        internal TaskCompletionSource<bool> Confirmation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            return Confirmation.Task;
        }

        public Task<bool> ReplaceAsync(IDialogViewModel viewModel)
        {
            throw new NotSupportedException();
        }

        public bool Confirm(string title, string message, bool defaultYes = false)
        {
            throw new NotSupportedException();
        }

        public bool ConfirmWarning(string title, string message, bool defaultYes = false)
        {
            throw new NotSupportedException();
        }

        public void Info(string title, string message)
        {
            throw new NotSupportedException();
        }

        public void Warning(string title, string message)
        {
            throw new NotSupportedException();
        }

        public void Error(string title, string message)
        {
            throw new NotSupportedException();
        }
    }
}
