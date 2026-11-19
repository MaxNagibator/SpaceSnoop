using KeepShell.Services.Platform;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Docker;
using SpaceSnoop.Wpf.ViewModels.Docker;

namespace SpaceSnoop.Wpf.Tests;

public sealed class DockerViewModelTests
{
    private const string VhdxPath = @"C:\Docker\wsl\disk\docker_data.vhdx";
    private const string BasePath = @"C:\Docker\wsl";

    [Test]
    public async Task Повторный_вход_дожидается_уже_запущенного_опроса()
    {
        var runner = new PausedRunner();
        var vm = new DockerViewModel(
            IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        var first = vm.EnsureLoadedAsync();
        var second = Task.CompletedTask;
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = vm.EnsureLoadedAsync();
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(runner.Calls, Is.EqualTo(1));
        }
        finally
        {
            runner.Pause = false;
            runner.Completion.TrySetResult(
                Success("""{"Type":"Images","TotalCount":"1","Active":"0","Size":"1GB","Reclaimable":"1GB (100%)"}"""));
            await Task.WhenAll(first, second);
        }

        Assert.That(vm.Buckets.Select(bucket => bucket.Title), Does.Contain("Образы"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Остановка_опроса_передаёт_отмену_и_повторный_вход_загружает_первый_снимок(bool firstLoad)
    {
        var runner = new PausedRunner { Pause = firstLoad };
        var vm = new DockerViewModel(
            IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        if (!firstLoad)
        {
            await vm.EnsureLoadedAsync();
            runner.Pause = true;
        }
        var run = firstLoad ? vm.EnsureLoadedAsync() : vm.RefreshCommand.ExecuteAsync(null);
        try
        {
            await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(vm.CancelCommand, Is.Not.Null);
            vm.CancelCommand!.Execute(null);
            await run;
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsBusy, Is.False);
                Assert.That(vm.IsAvailable, Is.EqualTo(!firstLoad));
                Assert.That(vm.CanRun, Is.EqualTo(!firstLoad));
                Assert.That(vm.CancelCommand, Is.Null);
                Assert.That(vm.StatusText, Does.Contain("остановлен"));
                Assert.That(vm.ShowUnavailable, Is.False);
            });
            runner.Pause = false;
            await vm.EnsureLoadedAsync();
            Assert.That(vm.CanRun, Is.True);
            Assert.That(vm.Buckets, Is.Not.Empty);
        }
        finally
        {
            runner.Completion.TrySetResult(new(0, string.Empty, string.Empty));
            await run;
        }
    }

    internal static DockerService IsolatedService(IDockerProcessRunner runner)
    {
        return new(runner, new FakeFileSystem([]), new StaticSettingsProvider(new(BasePath, false, null)));
    }

    [Test]
    public async Task Забытый_снимок_блокирует_действия_и_повторяется_после_отмены()
    {
        var runner = new PausedRunner { Pause = false };
        var vm = new DockerViewModel(
            IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        await vm.EnsureLoadedAsync();
        vm.ForgetSnapshot();
        Assert.Multiple(() =>
        {
            Assert.That(vm.CanRun, Is.False);
            Assert.That(vm.IsAvailable, Is.False);
            Assert.That(vm.ShowUnavailable, Is.False);
            Assert.That(vm.Buckets, Is.Empty);
        });
        runner.Pause = true;
        var run = vm.EnsureLoadedAsync();
        await runner.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.CancelCommand!.Execute(null);
        await run;
        runner.Pause = false;
        await vm.EnsureLoadedAsync();
        Assert.That(vm.CanRun, Is.True);
        Assert.That(vm.Buckets, Is.Not.Empty);
    }

    [Test]
    public async Task Вход_во_время_другой_операции_не_помечает_снимок_загруженным()
    {
        var runner = new PausedRunner { Pause = false };
        var vm = new DockerViewModel(
            IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        vm.IsBusy = true;
        await vm.EnsureLoadedAsync();
        Assert.That(runner.Calls, Is.Zero);
        vm.IsBusy = false;
        await vm.EnsureLoadedAsync();
        Assert.That(vm.CanRun, Is.True);
        Assert.That(vm.Buckets, Is.Not.Empty);
    }

    [Test]
    public async Task Недоступность_показывается_только_после_неудачного_опроса()
    {
        var runner = new ScriptedProcessRunner((_, _) => new(-1, string.Empty, "Docker offline"));
        var vm = new DockerViewModel(
            IsolatedService(runner), new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        Assert.That(vm.ShowUnavailable, Is.False);
        await vm.EnsureLoadedAsync();
        Assert.Multiple(() =>
        {
            Assert.That(vm.ShowUnavailable, Is.True);
            Assert.That(vm.UnavailableReason, Is.Not.Null.And.Not.Empty);
            Assert.That(vm.CanRun, Is.False);
        });
    }

    [Test]
    public async Task Отмена_во_время_замера_VHDX_не_запускает_процессы_Docker()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var files = new FakeFileSystem([new(100, 80)])
        {
            BeforeMetrics = () =>
            {
                started.TrySetResult();
                Assert.That(release.Wait(TimeSpan.FromSeconds(10)), Is.True);
            },
        };
        var runner = new PausedRunner { Pause = false };
        var vm = new DockerViewModel(
            new(runner, files, new StaticSettingsProvider(new(BasePath, true, null))),
            new NoopDialogs(), new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance);
        var run = vm.EnsureLoadedAsync();
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            vm.CancelCommand!.Execute(null);
            release.Set();
            await run;
            Assert.That(runner.Calls, Is.Zero);
            Assert.That(vm.StatusText, Does.Contain("остановлен"));
        }
        finally
        {
            release.Set();
            await run;
        }
    }

    [Test]
    public async Task Запоздавший_доклад_о_стадии_не_перетирает_итог()
    {
        var dispatcher = new DeferringUiDispatcher();
        var vm = CreateViewModel(Success("DiskPart successfully compacted the virtual disk file."), dispatcher: dispatcher);

        await vm.Compact.RunCommand.ExecuteAsync(null);

        dispatcher.RunPending();

        Assert.That(vm.StatusText, Is.EqualTo("Готово. Запустите Docker заново."));
    }

    [Test]
    public async Task Исчерпанный_таймаут_не_отменяет_первую_проверку_состояния()
    {
        var vm = CreateViewModel(Success("DiskPart successfully compacted the virtual disk file."), TimeSpan.Zero);

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("Готово. Запустите Docker заново."));
    }

    [Test]
    public async Task Провал_diskpart_оставляет_статус_об_остановленном_Docker()
    {
        var vm = CreateViewModel(diskpart: new(1, string.Empty, "DiskPart error: compact failed"));

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Does.Contain("Docker остановлен"));
    }

    [Test]
    public async Task Успешное_сжатие_оставляет_статус_о_необходимости_запустить_Docker()
    {
        var vm = CreateViewModel(diskpart: Success("DiskPart successfully compacted the virtual disk file."));

        await vm.Compact.RunCommand.ExecuteAsync(null);

        Assert.That(vm.StatusText, Is.EqualTo("Готово. Запустите Docker заново."));
        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.CancelCommand, Is.Null);
    }

    [Test]
    public void Подсказка_сжатия_называет_причину_блокировки_без_ложного_требования_прав()
    {
        var unelevated = new DockerViewModel(
            new(new PausedRunner(), new FakeFileSystem([]), new StaticSettingsProvider(new(BasePath, false, null)), administratorCheck: static () => false),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DockerViewModel>.Instance);
        var elevated = new DockerViewModel(
            new(new PausedRunner(), new FakeFileSystem([]), new StaticSettingsProvider(new(BasePath, false, null)), administratorCheck: static () => true),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DockerViewModel>.Instance);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unelevated.Compact.IsElevated, Is.False);
            Assert.That(unelevated.CompactHint, Is.EqualTo("Для сжатия диска Docker требуются права администратора"));

            Assert.That(elevated.Compact.IsElevated, Is.True);
            Assert.That(elevated.CanRun, Is.False);
            Assert.That(elevated.CompactHint, Does.Not.Contain("администратор"));
            Assert.That(elevated.CompactHint, Is.EqualTo("Опрос Docker не выполнен – нажмите «Обновить»"));
        }

        elevated.IsBusy = true;
        Assert.That(elevated.CompactHint, Is.EqualTo("Дождитесь завершения текущей операции Docker"));

        elevated.IsBusy = false;
        elevated.UnavailableReason = "Docker offline";
        Assert.That(elevated.CompactHint, Is.EqualTo("Docker недоступен"));

        elevated.UnavailableReason = null;
        elevated.IsAvailable = true;
        Assert.That(elevated.CanRun, Is.True);
        Assert.That(elevated.CompactHint, Is.EqualTo("Остановить Docker Desktop и сжать образ VHDX через diskpart"));
    }

    private static DockerViewModel CreateViewModel(
        DockerProcessResult diskpart,
        TimeSpan? stopTimeout = null,
        IUiDispatcher? dispatcher = null)
    {
        var runner = new ScriptedProcessRunner((fileName, arguments) => (fileName, arguments) switch
        {
            ("wsl", "--list --quiet") => Success("docker-desktop\0"),
            ("docker", "desktop stop") => Success(),
            ("docker", "desktop status") => Success("Status: stopped"),
            ("diskpart", "") => diskpart,
            _ => throw new AssertionException($"Неожиданная команда: {fileName} {arguments}"),
        });

        var fileSystem = new FakeFileSystem([new(1000, 800), new(1000, 600)]);
        var service = new DockerService(
            runner,
            fileSystem,
            new StaticSettingsProvider(new(BasePath, true, null)),
            new()
            {
                ProcessTimeout = TimeSpan.FromSeconds(1),
                StopTimeout = stopTimeout ?? TimeSpan.FromSeconds(1),
                StatusPollInterval = TimeSpan.Zero,
            },
            administratorCheck: () => true);

        return new(service, new NoopDialogs(showResult: true), dispatcher ?? new FakeUiDispatcher(), NullLogger<DockerViewModel>.Instance)
        {
            IsAvailable = true,
        };
    }

    private static DockerProcessResult Success(string output = "")
    {
        return new(0, output, string.Empty);
    }

    private sealed class DeferringUiDispatcher : IUiDispatcher
    {
        private readonly List<Action> _pending = [];

        public bool HasAccess => false;

        public void Invoke(Action action)
        {
            _pending.Add(action);
        }

        public IUiTimer CreateTimer(TimeSpan interval, Action tick)
        {
            return new FakeUiTimer(interval, tick);
        }

        public void RunPending()
        {
            var pending = _pending.ToArray();
            _pending.Clear();

            foreach (var action in pending)
            {
                action();
            }
        }
    }

    private sealed class StaticSettingsProvider(DockerDesktopSettings settings) : IDockerDesktopSettingsProvider
    {
        public DockerDesktopSettings? Read()
        {
            return settings;
        }
    }

    private sealed class FakeFileSystem(IReadOnlyList<DockerFileMetrics> measurements) : IDockerFileSystem
    {
        private readonly Queue<DockerFileMetrics> _measurements = new(measurements);

        internal Action? BeforeMetrics { get; init; }

        public bool FileExists(string path)
        {
            return string.Equals(path, VhdxPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsAvailable(string path)
        {
            return FileExists(path);
        }

        public DockerFileMetrics GetMetrics(string path)
        {
            BeforeMetrics?.Invoke();
            Assert.That(path, Is.EqualTo(VhdxPath));
            return _measurements.Dequeue();
        }
    }

    internal sealed class PausedRunner : IDockerProcessRunner
    {
        private int _calls;

        internal int Calls => Volatile.Read(ref _calls);
        internal bool Pause { get; set; } = true;
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<DockerProcessResult> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<DockerProcessResult> RunAsync(string fileName, string arguments, DockerProcessOptions options, CancellationToken cancel = default)
        {
            Interlocked.Increment(ref _calls);
            if (!Pause)
            {
                return Success(arguments.Contains("-v", StringComparison.Ordinal)
                    ? "{}"
                    : """{"Type":"Images","TotalCount":"1","Active":"0","Size":"1GB","Reclaimable":"1GB (100%)"}""");
            }
            Started.TrySetResult();
            try
            {
                return await Completion.Task.WaitAsync(cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                return new(-1, string.Empty, string.Empty, Canceled: true);
            }
        }
    }

    private sealed class ScriptedProcessRunner(
        Func<string, string, DockerProcessResult> responder) : IDockerProcessRunner
    {
        public Task<DockerProcessResult> RunAsync(
            string fileName,
            string arguments,
            DockerProcessOptions options,
            CancellationToken cancel = default)
        {
            return Task.FromResult(responder(fileName, arguments));
        }
    }
}
