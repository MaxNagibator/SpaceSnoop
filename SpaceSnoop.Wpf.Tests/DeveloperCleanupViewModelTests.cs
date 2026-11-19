using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Windows.Data;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class DeveloperCleanupViewModelTests
{
    private string _root = null!;
    private Dictionary<NuGetCacheKind, string> _paths = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "developer-cleanup-vm-tests", Guid.NewGuid().ToString("N"));
        _paths = Enum.GetValues<NuGetCacheKind>()
            .Where(x => x != NuGetCacheKind.None)
            .ToDictionary(x => x, x => Path.Combine(_root, x.ToString()));

        foreach (var (kind, path) in _paths)
        {
            Directory.CreateDirectory(path);
            if (kind != NuGetCacheKind.GlobalPackages)
            {
                File.WriteAllText(Path.Combine(path, "cache.dat"), "cache");
            }
        }

        var package = Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "package", "1.0.0");
        Directory.CreateDirectory(package);
        foreach (var name in new[]
        {
            ".nupkg.metadata", "package.nuspec", "package.1.0.0.nupkg",
        })
        {
            File.WriteAllText(Path.Combine(package, name), "package");
        }
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task Замер_NuGet_сохраняет_статус_и_остановку_после_смены_подвкладки()
    {
        var discovery = new TaskCompletionSource<NuGetCacheLocations>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new DeveloperCleanupViewModel(
            new(token => discovery.Task.WaitAsync(token)),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(), CreateWorkloads(), CreateDotNetClean());
        var run = vm.RefreshCommand.ExecuteAsync(null);
        try
        {
            vm.SelectedAreaIndex = 1;
            var captionChanges = 0;
            vm.PropertyChanged += (_, args) => captionChanges += args.PropertyName == nameof(vm.StatusCaption) ? 1 : 0;
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsProjectsActive, Is.True);
                Assert.That(vm.IsBusy, Is.True);
                Assert.That(vm.StatusCaption, Does.Contain("NuGet"));
                Assert.That(vm.StatusCaption, Does.Contain("измеряю"));
                Assert.That(vm.CancelCommand, Is.Not.Null);
                Assert.That(vm.Projects.FindCommand.CanExecute(null), Is.False);
            });
            vm.CancelCommand!.Execute(null);
            await run;
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(vm.StatusText, Does.Contain("отменён"));
            Assert.That(captionChanges, Is.GreaterThan(0));
        }
        finally
        {
            discovery.TrySetResult(new("test-user", _paths));
            await run;
        }
    }

    [Test]
    public async Task Обновление_из_шапки_не_заменяет_план_во_время_подтверждения()
    {
        var dialogs = new PausedDialogs();
        var vm = new DeveloperCleanupViewModel(
            new(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            dialogs,
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        await vm.RefreshCommand.ExecuteAsync(null);
        var rows = vm.Caches.ToArray();
        foreach (var row in rows)
        {
            row.IsSelected = true;
        }

        var cleanup = vm.CleanCommand.ExecuteAsync(null);
        try
        {
            await vm.RefreshCommand.ExecuteAsync(null);
            vm.ClearSelectionCommand.Execute(null);
            vm.SelectRecommendedCommand.Execute(null);
            Assert.Multiple(() =>
            {
                Assert.That(vm.IsBusy, Is.True);
                Assert.That(vm.CancelCommand, Is.Null, "Подтверждение ещё не завершилось.");
                Assert.That(vm.Caches, Is.EqualTo(rows));
                Assert.That(vm.Caches.All(x => x.IsSelected), Is.True);
                Assert.That(vm.ClearSelectionCommand.CanExecute(null), Is.False);
                Assert.That(vm.SelectRecommendedCommand.CanExecute(null), Is.False);
            });
        }
        finally
        {
            dialogs.Confirmation.SetResult(false);
            await cleanup;
        }
    }

    [Test]
    public async Task Ошибка_определения_путей_не_оставляет_исполнимый_план()
    {
        var vm = new DeveloperCleanupViewModel(
            new(_ => throw new InvalidOperationException("SDK недоступен")),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.That(vm.CanClean, Is.False);
        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.StatusText, Does.Contain("SDK недоступен"));
    }

    [Test]
    public async Task Ошибка_повторного_замера_не_теряет_результат_удаления()
    {
        var calls = 0;
        var service = new NuGetCleanupService(_ => ++calls == 3
            ? throw new InvalidOperationException("повторный замер недоступен")
            : Task.FromResult(new NuGetCacheLocations("user", _paths)));

        var vm = new DeveloperCleanupViewModel(
            service,
            new NoopDialogs(showResult: true),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(vm.ResultText, Does.Contain("Удалено файлов: 2"));
        Assert.That(vm.StatusText, Does.Contain("повторный замер недоступен"));
        Assert.That(vm.CanClean, Is.False);
    }

    [Test]
    public async Task Подтверждение_предупреждает_о_безвозвратности_а_отказ_сохраняет_файлы_и_план()
    {
        ConfirmDialogViewModel? confirmation = null;
        var dialogs = new NoopDialogs(
            show: model =>
            {
                confirmation = model as ConfirmDialogViewModel;
                return false;
            });

        var vm = new DeveloperCleanupViewModel(
            new(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            dialogs,
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(confirmation?.Warning, Does.Contain("безвозвратно"));
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.HttpCache], "cache.dat")), Is.True);
            Assert.That(vm.CanClean, Is.True);
            Assert.That(vm.ResultText, Is.Null);
        }
    }

    [Test]
    public async Task Подтверждённая_очистка_не_удаляет_невыбранные_категории()
    {
        var vm = Create(confirm: true);
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.HttpCache], "cache.dat")), Is.False);
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.PluginsCache], "cache.dat")), Is.False);
            Assert.That(File.Exists(Path.Combine(_paths[NuGetCacheKind.Temp], "cache.dat")), Is.True);
            Assert.That(Directory.GetFiles(_paths[NuGetCacheKind.GlobalPackages], "*", SearchOption.AllDirectories).Length, Is.EqualTo(3));
            Assert.That(vm.ResultText, Does.Contain("Удалено файлов: 2"));
            Assert.That(vm.IsBusy, Is.False);
        });
    }

    [Test]
    public async Task После_замера_выбраны_только_HTTP_и_plugins()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.That(
            vm.Caches.Where(x => x.IsSelected).Select(x => x.Kind),
            Is.EquivalentTo(
                new[]
                {
                    NuGetCacheKind.HttpCache, NuGetCacheKind.PluginsCache,
                }));

        Assert.That(vm.CanClean, Is.True);
    }

    [Test]
    public async Task Снятие_и_рекомендуемый_выбор_обновляют_итог_без_выбора_пакетов()
    {
        var vm = Create();
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages).IsSelected = true;
        vm.ClearSelectionCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.HasSelectedBytes, Is.False);
            Assert.That(vm.CanClean, Is.False);
            Assert.That(vm.SelectionText, Is.EqualTo("Категорий: 0 из 4"));
        });

        vm.SelectRecommendedCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(
                vm.Caches.Where(x => x.IsSelected).Select(x => x.Kind),
                Is.EquivalentTo(
                    new[]
                    {
                        NuGetCacheKind.HttpCache, NuGetCacheKind.PluginsCache,
                    }));

            Assert.That(vm.HasSelectedBytes, Is.True);
            Assert.That(vm.SelectionText, Is.EqualTo("Категорий: 2 из 4"));
            Assert.That(vm.CanClean, Is.True);
        });
    }

    [Test]
    public void Статус_проекта_остаётся_в_родительской_строке_после_завершения_поиска()
    {
        var vm = Create();
        vm.Projects.StatusText = "Поиск завершён. Проверьте пути и выберите каталоги.";
        vm.SelectedAreaIndex = 1;
        Assert.That(vm.StatusCaption, Is.EqualTo(vm.Projects.StatusText));
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Замер_из_фона_наполняет_привязанный_список_кэшей()
    {
        var dispatcher = new DispatcherUiDispatcher(Dispatcher.CurrentDispatcher);
        var vm = new DeveloperCleanupViewModel(
            new(_ => Task.FromResult(new NuGetCacheLocations("user", _paths))),
            new NoopDialogs(),
            dispatcher,
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        var caches = CollectionViewSource.GetDefaultView(vm.Caches);
        VisualTestHost.Run(() => Task.Run(() => vm.RefreshCommand.ExecuteAsync(null)));
        VisualTestHost.Pump();

        Assert.That(caches.Cast<NuGetCacheViewModel>().Select(cache => cache.Kind), Is.EquivalentTo(_paths.Keys));
    }

    [Test]
    public async Task Исключённый_пакет_виден_и_не_мешает_очистке_остальных()
    {
        var bad = Path.Combine(_paths[NuGetCacheKind.GlobalPackages], "bad", "1.0.0", ".git");
        Directory.CreateDirectory(bad);
        var sentinel = Path.Combine(bad, "config");
        await File.WriteAllTextAsync(sentinel, "repository");
        var vm = Create(confirm: true);
        await vm.RefreshCommand.ExecuteAsync(null);
        var row = vm.Caches.Single(x => x.Kind == NuGetCacheKind.GlobalPackages);
        Assert.Multiple(() =>
        {
            Assert.That(row.CanSelect, Is.True);
            Assert.That(row.ErrorText, Does.Contain("исключён"));
            Assert.That(row.Files, Is.EqualTo(3));
            Assert.That(vm.StatusText, Does.Contain("Исключено пакетов: 1"));
        });
        vm.ClearSelectionCommand.Execute(null);
        row.IsSelected = true;
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(vm.ResultText, Does.Contain("Удалено файлов: 3"));
        Assert.That(File.Exists(sentinel), Is.True);
    }

    [Test]
    public async Task Отменённая_первичная_загрузка_NuGet_повторяется_при_входе_а_успешная_кэшируется()
    {
        var pause = true;
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<NuGetCacheLocations>(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new DeveloperCleanupViewModel(
            new(async token =>
            {
                Interlocked.Increment(ref calls);
                if (pause)
                {
                    started.TrySetResult();
                    return await blocked.Task.WaitAsync(token);
                }

                return new("test-user", _paths);
            }),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());

        var first = vm.EnsureLoadedAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.CancelCommand!.Execute(null);
        await first;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Caches, Is.Empty);
            Assert.That(calls, Is.EqualTo(1));
        }

        pause = false;
        await vm.EnsureLoadedAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Caches, Has.Count.EqualTo(4));
            Assert.That(calls, Is.EqualTo(2));
        }

        await vm.EnsureLoadedAsync();
        Assert.That(calls, Is.EqualTo(2));
    }

    private DeveloperCleanupViewModel Create(bool confirm = false)
    {
        return new(
            new(_ => Task.FromResult(new NuGetCacheLocations("test-user", _paths))),
            new NoopDialogs(showResult: confirm),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            CreateProjects(),
            CreateWorkloads(),
            CreateDotNetClean());
    }

    private DotNetCleanViewModel CreateDotNetClean()
    {
        return new(
            new(_root),
            new NoopDialogs(),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            NullLogger<DotNetCleanViewModel>.Instance);
    }

    private static ProjectCleanupViewModel CreateProjects()
    {
        return new(
            new(),
            new NoopDialogs(),
            new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            new MemorySettings(),
            NullLogger<ProjectCleanupViewModel>.Instance);
    }

    private WorkloadCleanupViewModel CreateWorkloads()
    {
        return new(
            new(_root),
            new NoopDialogs(),
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
