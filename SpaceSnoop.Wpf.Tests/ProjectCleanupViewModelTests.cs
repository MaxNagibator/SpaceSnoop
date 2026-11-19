using KeepShell.Bootstrap;
using KeepShell.Services;
using KeepShell.Services.Modal;
using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core.Cleanup;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class ProjectCleanupViewModelTests
{
    private string _root = null!;
    private List<string> _recycled = null!;

    [SetUp]
    public void SetUp()
    {
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository is not null && !Directory.Exists(Path.Combine(repository.FullName, ".git")))
        {
            repository = repository.Parent;
        }

        Assert.That(repository, Is.Not.Null);
        _root = Path.Combine(repository!.FullName, ".kc", "scratch", "project-cleanup-vm-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "app.csproj"), "<Project />");
        foreach (var name in new[]
        {
            "bin", "obj", "TestResults", "BenchmarkDotNet.Artifacts",
        })
        {
            Directory.CreateDirectory(Path.Combine(_root, name));
            File.WriteAllText(Path.Combine(_root, name, "data.txt"), "data");
        }

        _recycled = [];
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public async Task Все_артефакты_включая_bin_obj_требуют_ручного_выбора()
    {
        var vm = Create();
        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(vm.Artifacts.Count, Is.EqualTo(4));
            Assert.That(vm.Artifacts.Any(x => x.IsSelected), Is.False);
            Assert.That(vm.Artifacts.All(x => x.Contents.Count > 0), Is.True);
            Assert.That(vm.CanClean, Is.False);
        });

        vm.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).IsSelected = true;
        Assert.That(vm.CanClean, Is.True);
        Assert.That(vm.SelectionText, Is.EqualTo("Выбрано строк: 1 из 4"));
        vm.ClearSelectionCommand.Execute(null);
        Assert.That(vm.CanClean, Is.False);
    }

    [Test]
    public async Task Выбор_каталога_запускает_поиск_а_отмена_диалога_сохраняет_состояние()
    {
        var picker = new FakeFilePicker
        {
            Folder = _root,
        };

        var vm = Create(picker: picker);
        await vm.BrowseCommand.ExecuteAsync(null);
        var rows = vm.Artifacts.ToArray();
        picker.Folder = null;
        await vm.BrowseCommand.ExecuteAsync(null);
        Assert.That(vm.RootPath, Is.EqualTo(_root));
        Assert.That(vm.Artifacts, Is.EqualTo(rows));
    }

    [Test]
    public void Демонстрационный_сброс_не_перезаписывает_сохранённые_пути()
    {
        var settings = new MemorySettings();
        var roots = JsonSerializer.Serialize(
            new[]
            {
                _root, Path.Combine(_root, "second"),
            });

        var outputs = JsonSerializer.Serialize(
            new[]
            {
                Path.Combine(_root, "custom"),
            });

        settings.SetValue(SettingsKeys.DeveloperProjectRoots, roots);
        settings.SetValue(SettingsKeys.DeveloperCustomOutputs, outputs);
        var vm = Create(settings: settings);
        vm.ShowForAutomation();
        vm.ResetForAutomation();
        Assert.Multiple(() =>
        {
            Assert.That(settings.GetStringValue(SettingsKeys.DeveloperProjectRoots), Is.EqualTo(roots));
            Assert.That(settings.GetStringValue(SettingsKeys.DeveloperCustomOutputs), Is.EqualTo(outputs));
        });
    }

    [Test]
    public async Task Изменение_списка_путей_во_время_подтверждения_отменяет_очистку()
    {
        var dialogs = new PausedDialogs();
        var vm = Create(dialogs);
        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).IsSelected = true;
        var cleanup = vm.CleanCommand.ExecuteAsync(null);
        vm.CustomOutputs.Add(Path.Combine(_root, "bin"));
        dialogs.Confirmation.SetResult(true);
        await cleanup;
        Assert.Multiple(() =>
        {
            Assert.That(_recycled, Is.Empty);
            Assert.That(vm.StatusText, Does.Contain("изменились"));
        });
    }

    [Test]
    public async Task Отказ_от_подтверждения_сохраняет_просмотренный_план()
    {
        ConfirmDialogViewModel? confirmation = null;
        var vm = Create(
            new NoopDialogs(
                show: model =>
                {
                    confirmation = model as ConfirmDialogViewModel;
                    return false;
                }));

        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts[0].IsSelected = true;
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(confirmation, Is.Not.Null);
            Assert.That(confirmation!.Warning, Does.Contain("Корзина"));
            Assert.That(vm.CanClean, Is.True);
            Assert.That(vm.ResultText, Is.Null);
            Assert.That(_recycled, Is.Empty);
        });
    }

    [Test]
    public async Task Отмена_во_время_подтверждения_не_начинает_очистку()
    {
        var dialogs = new PausedDialogs();
        var vm = Create(dialogs);
        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).IsSelected = true;
        var cleanup = vm.CleanCommand.ExecuteAsync(null);
        vm.CancelCommand.Execute(null);
        dialogs.Confirmation.SetResult(true);
        await cleanup;
        Assert.Multiple(() =>
        {
            Assert.That(_recycled, Is.Empty);
            Assert.That(vm.IsBusy, Is.False);
            Assert.That(vm.CanClean, Is.True);
        });
    }

    [Test]
    public async Task Очистка_выбранного_bin_сохраняет_результаты_тестов_и_бенчмарков()
    {
        var vm = Create(new NoopDialogs(showResult: true));
        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin).IsSelected = true;
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.Multiple(() =>
        {
            Assert.That(_recycled, Has.Count.EqualTo(1));
            Assert.That(File.Exists(Path.Combine(_root, "bin", "data.txt")), Is.False);
            Assert.That(File.Exists(Path.Combine(_root, "TestResults", "data.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(_root, "BenchmarkDotNet.Artifacts", "data.txt")), Is.True);
            Assert.That(vm.ResultText, Does.Contain("В корзине файлов: 1"));
            Assert.That(vm.Artifacts.Any(x => x.IsSelected), Is.False);
        });
    }

    [Test]
    public void Повреждённые_списки_настроек_считаются_пустыми()
    {
        var settings = new MemorySettings();
        settings.SetValue(SettingsKeys.DeveloperProjectRoots, "{broken");
        settings.SetValue(SettingsKeys.DeveloperCustomOutputs, "not-json");
        var vm = Create(settings: settings);
        Assert.Multiple(() =>
        {
            Assert.That(vm.RootPath, Is.Empty);
            Assert.That(vm.AdditionalRoots, Is.Empty);
            Assert.That(vm.CustomOutputs, Is.Empty);
            Assert.That(vm.FindCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public async Task Подтверждение_блокирует_повторный_поиск_NuGet_и_сброс_выбора()
    {
        var dialogs = new PausedDialogs();
        var vm = Create(dialogs);
        var parent = new DeveloperCleanupViewModel(
            new(Path.Combine(_root, "logs")),
            new NoopDialogs(),
            new FakeUiDispatcher(),
            NullLogger<DeveloperCleanupViewModel>.Instance,
            vm,
            new(
                new(_root),
                new NoopDialogs(),
                new FakeShellLauncher(),
                new FakeUiDispatcher(),
                NullLogger<WorkloadCleanupViewModel>.Instance),
            new(
                new(_root),
                new NoopDialogs(),
                new FakeFilePicker(),
                new FakeShellLauncher(),
                new FakeUiDispatcher(),
                NullLogger<DotNetCleanViewModel>.Instance));

        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts[0].IsSelected = true;
        var cleanup = vm.CleanCommand.ExecuteAsync(null);
        try
        {
            Assert.That(parent.IsBusy, Is.True);
            Assert.That(parent.RefreshCommand.CanExecute(null), Is.False);
            Assert.That(vm.BrowseCommand.CanExecute(null), Is.False);
            Assert.That(vm.FindCommand.CanExecute(null), Is.False);
            vm.ClearSelectionCommand.Execute(null);
            Assert.That(vm.Artifacts[0].IsSelected, Is.True);
            vm.RootPath = Path.Combine(_root, "other");
        }
        finally
        {
            dialogs.Confirmation.SetResult(true);
            await cleanup;
        }

        Assert.That(_recycled, Is.Empty);
        Assert.That(vm.StatusText, Does.Contain("изменились"));
        Assert.That(parent.IsBusy, Is.False);
    }

    [Test]
    public async Task Поиск_фильтрует_строки_но_оставляет_общие_итоги_и_скрытый_выбор()
    {
        ConfirmDialogViewModel? confirmation = null;
        var vm = Create(
            new NoopDialogs(
                show: model =>
                {
                    confirmation = model as ConfirmDialogViewModel;
                    return false;
                }));

        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        Assert.That(
            vm.Artifacts.Count,
            Is.EqualTo(4),
            $"{vm.StatusText}{Environment.NewLine}{string.Join(Environment.NewLine, vm.Artifacts.Select(x => $"{x.Kind}: {x.Path}"))}");

        var bin = vm.Artifacts.Single(x => x.Kind == ProjectArtifactKind.Bin);
        bin.IsSelected = true;
        var hiddenChanged = false;
        vm.PropertyChanged += (_, args) => hiddenChanged |= args.PropertyName == nameof(vm.HiddenSelectionText);
        vm.SearchText = "нет совпадений";
        Assert.Multiple(() =>
        {
            Assert.That(vm.RowsView.Cast<ProjectArtifactViewModel>(), Is.Empty);
            Assert.That(vm.TotalText, Is.Not.Empty);
            Assert.That(vm.SelectedText, Is.Not.Empty);
            Assert.That(vm.HasHiddenSelection, Is.True);
            Assert.That(hiddenChanged, Is.True);
        });

        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(confirmation!.Lines.OfType<ConfirmTextLine>().Any(line => line.Body == bin.Path), Is.True);
    }

    [Test]
    public void Пути_восстанавливаются_и_сохраняются_без_деструктивного_выбора()
    {
        var settings = new MemorySettings();
        settings.SetValue(
            SettingsKeys.DeveloperProjectRoots,
            JsonSerializer.Serialize(
                new[]
                {
                    _root, Path.Combine(_root, "second"),
                }));

        settings.SetValue(
            SettingsKeys.DeveloperCustomOutputs,
            JsonSerializer.Serialize(
                new[]
                {
                    Path.Combine(_root, "custom"),
                }));

        var vm = Create(settings: settings);
        Assert.Multiple(() =>
        {
            Assert.That(vm.RootPath, Is.EqualTo(_root));
            Assert.That(
                vm.AdditionalRoots,
                Is.EqualTo(
                    new[]
                    {
                        Path.Combine(_root, "second"),
                    }));

            Assert.That(
                vm.CustomOutputs,
                Is.EqualTo(
                    new[]
                    {
                        Path.Combine(_root, "custom"),
                    }));

            Assert.That(vm.Artifacts, Is.Empty);
        });

        vm.RootPath = string.Empty;
        Assert.That(
            JsonSerializer.Deserialize<string[]>(settings.GetStringValue(SettingsKeys.DeveloperProjectRoots)!),
            Is.EqualTo(
                new[]
                {
                    Path.Combine(_root, "second"),
                }));
    }

    [Test]
    public async Task Смена_корня_сбрасывает_план_и_выбор()
    {
        var vm = Create();
        vm.RootPath = _root;
        await vm.FindCommand.ExecuteAsync(null);
        vm.Artifacts[0].IsSelected = true;
        vm.RootPath = Path.Combine(_root, "other");
        Assert.That(vm.Artifacts, Is.Empty);
        Assert.That(vm.CanClean, Is.False);
    }

    [Test]
    public async Task Только_нестандартный_output_можно_искать_и_очищать_по_Id()
    {
        var output = Path.Combine(_root, "bin");
        var vm = Create(new NoopDialogs(showResult: true));
        vm.CustomOutputs.Add(output);
        await vm.FindCommand.ExecuteAsync(null);
        Assert.That(vm.Artifacts.Single().Kind, Is.EqualTo(ProjectArtifactKind.CustomOutput));
        vm.Artifacts[0].IsSelected = true;
        await vm.CleanCommand.ExecuteAsync(null);
        Assert.That(_recycled, Has.Count.EqualTo(1));
    }

    [Test]
    public void Явная_очистка_списков_сохраняет_пустые_значения()
    {
        var settings = new MemorySettings();
        var vm = Create(settings: settings);
        vm.RootPath = _root;
        vm.AdditionalRoots.Add(Path.Combine(_root, "second"));
        vm.CustomOutputs.Add(Path.Combine(_root, "custom"));
        vm.ClearRootsCommand.Execute(null);
        vm.ClearCustomOutputsCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(JsonSerializer.Deserialize<string[]>(settings.GetStringValue(SettingsKeys.DeveloperProjectRoots)!), Is.Empty);
            Assert.That(JsonSerializer.Deserialize<string[]>(settings.GetStringValue(SettingsKeys.DeveloperCustomOutputs)!), Is.Empty);
        });
    }

    private ProjectCleanupViewModel Create(IDialogService? dialogs = null, FakeFilePicker? picker = null, ISettingsStore? settings = null)
    {
        return new(
            new(path =>
            {
                Directory.Move(path, path + "-recycled");
                _recycled.Add(path + "-recycled");
            }),
            dialogs ?? new NoopDialogs(),
            picker ?? new FakeFilePicker(),
            new FakeShellLauncher(),
            new FakeUiDispatcher(),
            settings ?? new MemorySettings(),
            NullLogger<ProjectCleanupViewModel>.Instance);
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
