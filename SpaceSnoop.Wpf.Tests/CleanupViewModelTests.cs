using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.Diagnostics;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Globalization;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class CleanupViewModelTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "cleanup-vm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task Сбой_замера_отличается_от_отсутствующего_каталога_и_сохраняет_текст_ошибки()
    {
        var service = new CleanupService();
        using var monitor = TestDiagnostics.Monitor();
        var missingPath = Path.Combine(_root, "absent");
        var failingPath = Path.Combine(_root, "broken");
        Directory.CreateDirectory(failingPath);

        var vm = CreateViewModel(
            service,
            monitor,
            minAge =>
            [
                Target("Missing", "Отсутствующий", missingPath, minAge),
                Target("Broken", "Сбойный", failingPath, minAge),
            ],
            (target, token) => target.Id == "Broken"
                ? Task.FromException<CleanupMeasurement>(new IOException("Устройство не готово"))
                : service.MeasureAsync(target, token));

        await vm.RefreshCommand.ExecuteAsync(null);

        var missing = vm.Targets.Single(x => x.Model.Id == "Missing");
        var broken = vm.Targets.Single(x => x.Model.Id == "Broken");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missing.Availability, Is.EqualTo(CleanupAvailability.Missing));
            Assert.That(missing.AvailabilityText, Is.EqualTo("каталога нет"));
            Assert.That(missing.IsAvailabilityError, Is.False);

            Assert.That(broken.Availability, Is.EqualTo(CleanupAvailability.Failed));
            Assert.That(broken.IsAvailabilityError, Is.True);
            Assert.That(broken.AvailabilityText, Is.EqualTo("не удалось опросить: Устройство не готово"));
        }
    }

    [Test]
    public async Task Смена_порога_возраста_во_время_замера_пересчитывает_цели_и_статус_для_нового_порога()
    {
        var directory = Path.Combine(_root, "age-threshold");
        Directory.CreateDirectory(directory);
        var oldFile = Path.Combine(directory, "old.tmp");
        var recentFile = Path.Combine(directory, "recent.tmp");
        await File.WriteAllBytesAsync(oldFile, new byte[1024]);
        await File.WriteAllBytesAsync(recentFile, new byte[2048]);
        var oldStamp = DateTime.UtcNow.AddHours(-48);
        var recentStamp = DateTime.UtcNow.AddHours(-4);
        File.SetCreationTimeUtc(oldFile, oldStamp);
        File.SetLastWriteTimeUtc(oldFile, oldStamp);
        File.SetCreationTimeUtc(recentFile, recentStamp);
        File.SetLastWriteTimeUtc(recentFile, recentStamp);

        var service = new CleanupService();
        using var monitor = TestDiagnostics.Monitor();
        var firstPassStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pauseFirstPass = true;

        var vm = CreateViewModel(
            service,
            monitor,
            minAge => [Target("AgeTarget", "Временные файлы", directory, minAge)],
            async (target, token) =>
            {
                if (pauseFirstPass)
                {
                    pauseFirstPass = false;
                    firstPassStarted.TrySetResult();
                    await releaseFirstPass.Task.WaitAsync(token);
                }

                return await service.MeasureAsync(target, token);
            },
            initialMinAgeHours: 1);

        var run = vm.RefreshCommand.ExecuteAsync(null);
        await firstPassStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.MinAgeHours = 24;
        releaseFirstPass.TrySetResult();
        await run;

        var row = vm.Targets.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(row.IsMeasured, Is.True);
            Assert.That(row.SizeBytes, Is.EqualTo(1024));
            Assert.That(vm.TotalBytes, Is.EqualTo(1024));
            Assert.That(vm.StatusText, Is.EqualTo($"Найдено {SizeFormatter.Format(1024)}."));
        }

        vm.MinAgeHours = 72;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.StatusText, Is.Null);
            Assert.That(vm.Targets.Single().IsMeasured, Is.False);
            Assert.That(vm.TotalBytes, Is.Zero);
        }
    }

    [Test]
    public async Task Повторный_замер_после_очистки_показывает_фазу_и_отменяется_без_потери_отчёта()
    {
        var directory = Path.Combine(_root, "post-clean");
        Directory.CreateDirectory(directory);
        var item = Path.Combine(directory, "stale.tmp");
        await File.WriteAllBytesAsync(item, new byte[512]);
        var stamp = DateTime.UtcNow.AddHours(-48);
        File.SetCreationTimeUtc(item, stamp);
        File.SetLastWriteTimeUtc(item, stamp);

        var service = new CleanupService();
        using var monitor = TestDiagnostics.Monitor();
        var dialogs = new AutoRunCleanupDialogs();
        var remeasureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockRemeasure = new TaskCompletionSource<CleanupMeasurement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockNextMeasure = false;

        var vm = CreateViewModel(
            service,
            monitor,
            minAge => [Target("PostClean", "Кэш приложения", directory, minAge)],
            async (target, token) =>
            {
                if (blockNextMeasure)
                {
                    remeasureStarted.TrySetResult();
                    return await blockRemeasure.Task.WaitAsync(token);
                }

                return await service.MeasureAsync(target, token);
            },
            dialogs: dialogs);

        await vm.RefreshCommand.ExecuteAsync(null);
        vm.Targets.Single().IsSelected = true;
        blockNextMeasure = true;

        var cleanup = vm.CleanSelectedCommand.ExecuteAsync(null);
        await remeasureStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(item), Is.False);
            Assert.That(vm.IsBusy, Is.True);
            Assert.That(vm.IsIndeterminate, Is.False);
            Assert.That(vm.ProgressMax, Is.EqualTo(1));
            Assert.That(vm.ProgressValue, Is.Zero);
            Assert.That(vm.CancelCommand, Is.Not.Null);
            Assert.That(vm.StatusText, Does.Contain("Завершено:").And.Contain("Повторный замер 1 из 1: Кэш приложения"));
        }

        vm.CancelCommand!.Execute(null);
        await cleanup;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(vm.CancelCommand, Is.Null);
            Assert.That(vm.StatusText, Does.StartWith("Завершено: удалено 1").And.EndWith("Повторный замер остановлен."));
        }
    }

    [Test]
    public async Task Отменённая_первичная_загрузка_Windows_повторяется_при_входе_а_успешная_кэшируется()
    {
        var directory = Path.Combine(_root, "ensure-loaded");
        Directory.CreateDirectory(directory);
        var item = Path.Combine(directory, "cached.tmp");
        await File.WriteAllBytesAsync(item, new byte[256]);
        var stamp = DateTime.UtcNow.AddHours(-48);
        File.SetCreationTimeUtc(item, stamp);
        File.SetLastWriteTimeUtc(item, stamp);

        var service = new CleanupService();
        using var monitor = TestDiagnostics.Monitor();
        var pause = true;
        var calls = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<CleanupMeasurement>(TaskCreationOptions.RunContinuationsAsynchronously);

        var vm = CreateViewModel(
            service,
            monitor,
            minAge => [Target("InitialLoad", "Кэш Windows", directory, minAge)],
            async (target, token) =>
            {
                Interlocked.Increment(ref calls);
                if (pause)
                {
                    started.TrySetResult();
                    return await blocked.Task.WaitAsync(token);
                }

                return await service.MeasureAsync(target, token);
            });

        var first = vm.EnsureLoadedAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.CancelCommand!.Execute(null);
        await first;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Targets.Single().IsMeasured, Is.False);
            Assert.That(calls, Is.EqualTo(1));
        }

        pause = false;
        await vm.EnsureLoadedAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(vm.Targets.Single().IsMeasured, Is.True);
            Assert.That(vm.TotalBytes, Is.EqualTo(256));
            Assert.That(calls, Is.EqualTo(2));
        }

        await vm.EnsureLoadedAsync();
        Assert.That(calls, Is.EqualTo(2));
    }

    private static CleanupViewModel CreateViewModel(
        CleanupService service,
        PerformanceMonitor monitor,
        Func<TimeSpan, IReadOnlyList<CleanupTarget>> catalog,
        Func<CleanupTarget, CancellationToken, Task<CleanupMeasurement>> measure,
        int initialMinAgeHours = 24,
        IDialogService? dialogs = null)
    {
        var dispatcher = new FakeUiDispatcher();
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.CleanupMinAgeHours, initialMinAgeHours.ToString(CultureInfo.InvariantCulture));
        var operations = new PerformanceOperations(monitor);
        var factory = new CleanupProgressDialogFactory(
            service,
            dispatcher,
            operations,
            NullLogger<CleanupProgressDialogViewModel>.Instance);
        var notifier = new ToastNotifier(new ToastHostViewModel(), new ShellPreferences(settings));

        return new(
            factory,
            dialogs ?? new NoopDialogs(),
            new ModalHostViewModel(),
            new FakeShellLauncher(),
            settings,
            dispatcher,
            notifier,
            NullLogger<CleanupViewModel>.Instance,
            catalog,
            measure);
    }

    private static CleanupTarget Target(string id, string name, string path, TimeSpan minAge)
    {
        return new()
        {
            Id = id,
            Name = name,
            Description = "Тестовая цель",
            Kind = CleanupTargetKind.Directory,
            Path = path,
            MinimumAge = minAge,
        };
    }

    private sealed class AutoRunCleanupDialogs : IDialogService
    {
        public async Task<bool> ShowAsync(IDialogViewModel viewModel)
        {
            if (viewModel is ConfirmDialogViewModel)
            {
                return true;
            }

            if (viewModel is CleanupProgressDialogViewModel progress)
            {
                await progress.StartCommand.ExecuteAsync(null);
                return true;
            }

            return false;
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
