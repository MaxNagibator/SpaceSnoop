using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using KeepShell.Services;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using SpaceSnoop.Wpf.ViewModels.Sync;
using SpaceSnoop.Wpf.Views;
using SpaceSnoop.Wpf.Views.Dialogs;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanMarksNetworkDeleteTests
{
    private string _root = null!;
    private ServiceProvider _services = null!;
    private KeepShellLogging _logging = null!;
    private VisualTestHost _host = null!;
    private MainWindow _window = null!;
    private ModalHostViewModel _modals = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-marks-unc", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        AppThemes.Register();
        ThemeManager.Apply(AppThemes.LightKey);
        ViewLocator.InstallIntoApplication();

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _window = _services.GetRequiredService<MainWindow>();
        _modals = _services.GetRequiredService<ModalHostViewModel>();
        _host = VisualTestHost.Show(_window, 1100, 720);
        _host.Settle();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _host?.Dispose();
        _services?.Dispose();
        _logging?.Dispose();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Test]
    public async Task Пометка_на_сети_отменяет_автозапуск_диалога_и_показывает_предупреждение_без_корзины()
    {
        using var sink = BindingErrorSink.Attach();
        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.DeleteConfirm, false);
        settings.SetEnum(SettingsKeys.DeleteMode, DeleteMode.RecycleBin);

        var operations = new OperationPreferences(settings);
        var scanPreferences = new ScanPreferences(settings);
        var agent = new AgentPreferences(settings);
        var factory = new ScanNodeFactory(scanPreferences, agent, new FakeShellLauncher());
        var rootSpace = new DirectorySpace(@"\\nas\media", null, DateTime.Now, DateTime.Now);
        var child = new DirectorySpace("season1", rootSpace, DateTime.Now, DateTime.Now);
        child.Delete();
        rootSpace.Add(child);

        var rootNode = factory.CreateRoot(rootSpace, new(), null);
        ObservableCollection<ScanNodeViewModel> roots = [rootNode];
        var treemap = new ScanTreemapViewModel(roots);
        var summary = new ScanSummaryViewModel(false, static () => Task.FromResult(false));

        DeleteProgressDialogViewModel? captured = null;
        var dialogs = new NoopDialogs(show: modal =>
        {
            captured = modal as DeleteProgressDialogViewModel;
            return false;
        });

        var marks = new ScanMarksViewModel(
            dialogs,
            _services.GetRequiredService<DeleteProgressDialogFactory>(),
            operations,
            NullLogger.Instance,
            factory,
            roots,
            summary,
            treemap,
            static () => null,
            static _ => { },
            static () => { },
            static () => false,
            static () => { });

        marks.RecountMarked();
        await marks.DeleteMarkedCommand.ExecuteAsync(null);

        Assert.That(captured, Is.Not.Null);

        _ = _modals.ShowAsync(captured!);
        _host.Settle();

        try
        {
            var view = Descendant<DeleteProgressDialogView>(_window) ?? throw new InvalidOperationException("DeleteProgressDialogView не найден.");
            var warning = Descendants<TextBlock>(view)
                .FirstOrDefault(block => block.IsVisible && block.Text == "На сетевом хранилище нет корзины Windows – удаление необратимо");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(captured!.IsIdle, Is.True, "При ConfirmBeforeDelete=false сетевое удаление не должно стартовать автоматически.");
                Assert.That(captured.IsRunning, Is.False);
                Assert.That(captured.ForcedByNetwork, Is.True);
                Assert.That(captured.ActionVerb, Is.EqualTo("удалены безвозвратно"));
                Assert.That(captured.StartButtonText, Is.EqualTo("Удалить безвозвратно"));
                Assert.That(warning, Is.Not.Null);
                Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
            }
        }
        finally
        {
            _modals.RequestCancel();
            _host.Settle();
        }
    }

    [Test]
    public async Task Ошибка_корзины_для_симлинка_фиксируется_в_ошибке_строки_без_потери_данных()
    {
        var previousChunk = DeleteOperationDiagnostics.RecycleChunk;
        var previousSingle = DeleteOperationDiagnostics.RecycleSingle;

        var testFile = Path.Combine(_root, "keep-me.txt");
        await File.WriteAllTextAsync(testFile, "intact");

        DeleteOperationDiagnostics.RecycleChunk = static paths => throw new RecycleBinUnavailableException(paths[0]);
        DeleteOperationDiagnostics.RecycleSingle = static path => throw new RecycleBinUnavailableException(path);

        try
        {
            var rootSpace = new DirectorySpace(_root, null, DateTime.Now, DateTime.Now);
            var fileSpace = rootSpace.AddFile(new FileInfo(testFile));

            using var monitor = TestDiagnostics.Monitor();
            var dialog = new DeleteProgressDialogViewModel(
                [fileSpace],
                permanent: false,
                monitor,
                new(monitor),
                new(new MemorySettings()),
                new FakeUiDispatcher(),
                NullLogger.Instance);

            await dialog.StartCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(File.Exists(testFile), Is.True);
                Assert.That(dialog.DeletedItems, Is.Empty);
                Assert.That(dialog.HasErrors, Is.True);
                Assert.That(dialog.Items[0].State, Is.EqualTo(DeleteRowState.Failed));
                Assert.That(dialog.Items[0].Error, Does.Contain("не поддерживает корзину Windows"));
            }
        }
        finally
        {
            DeleteOperationDiagnostics.RecycleChunk = previousChunk;
            DeleteOperationDiagnostics.RecycleSingle = previousSingle;
        }
    }

    [Test]
    public void Исключение_последнего_сетевого_элемента_возвращает_режим_корзины_при_настройке_корзины()
    {
        var netRoot = new DirectorySpace(@"\\nas\media", null, DateTime.Now, DateTime.Now);
        var netItem = new DirectorySpace("clips", netRoot, DateTime.Now, DateTime.Now);
        netRoot.Add(netItem);

        var localRoot = new DirectorySpace(_root, null, DateTime.Now, DateTime.Now);
        var localItem = new DirectorySpace("notes", localRoot, DateTime.Now, DateTime.Now);
        localRoot.Add(localItem);

        HashSet<SpaceBase> withoutRecycleBin = [netItem];

        using var monitor = TestDiagnostics.Monitor();
        var recycleDialog = new DeleteProgressDialogViewModel(
            [netItem, localItem],
            permanent: false,
            monitor,
            new(monitor),
            new(new MemorySettings()),
            new FakeUiDispatcher(),
            NullLogger.Instance,
            withoutRecycleBin: withoutRecycleBin);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recycleDialog.ForcedByNetwork, Is.True);
            Assert.That(recycleDialog.IsMixed, Is.True);
            Assert.That(recycleDialog.StartButtonText, Is.EqualTo("Удалить (часть безвозвратно)"));
            Assert.That(recycleDialog.IntroText, Does.Contain("В корзину: 1 объект").And.Contain("безвозвратно: 1 объект"));
            Assert.That(recycleDialog.NetworkWarningText, Does.Contain("сетевые объекты будут удалены безвозвратно").And.Contain("локальные – в корзину"));
            Assert.That(recycleDialog.Items[0].IsPermanent, Is.True);
            Assert.That(recycleDialog.Items[0].ModeBadgeText, Is.EqualTo("Безвозвратно"));
            Assert.That(recycleDialog.Items[1].IsPermanent, Is.False);
            Assert.That(recycleDialog.Items[1].ModeBadgeText, Is.EqualTo("В корзину"));
        }

        recycleDialog.ExcludeCommand.Execute(recycleDialog.Items[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recycleDialog.ForcedByNetwork, Is.False);
            Assert.That(recycleDialog.IsMixed, Is.False);
            Assert.That(recycleDialog.StartButtonText, Is.EqualTo("Удалить"));
            Assert.That(recycleDialog.ActionVerb, Is.EqualTo("перемещены в корзину"));
            Assert.That(recycleDialog.IntroText, Does.Contain("перемещены в корзину"));
            Assert.That(recycleDialog.CancelHint, Does.Contain("пачки"));
        }

        var mixedExcludeLocalDialog = new DeleteProgressDialogViewModel(
            [netItem, localItem],
            permanent: false,
            monitor,
            new(monitor),
            new(new MemorySettings()),
            new FakeUiDispatcher(),
            NullLogger.Instance,
            withoutRecycleBin: withoutRecycleBin);

        mixedExcludeLocalDialog.ExcludeCommand.Execute(mixedExcludeLocalDialog.Items[1]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(mixedExcludeLocalDialog.ForcedByNetwork, Is.True);
            Assert.That(mixedExcludeLocalDialog.IsMixed, Is.False);
            Assert.That(mixedExcludeLocalDialog.StartButtonText, Is.EqualTo("Удалить безвозвратно"));
            Assert.That(mixedExcludeLocalDialog.ActionVerb, Is.EqualTo("удалены безвозвратно"));
            Assert.That(mixedExcludeLocalDialog.NetworkWarningText, Is.EqualTo("На сетевом хранилище нет корзины Windows – удаление необратимо"));
            Assert.That(mixedExcludeLocalDialog.CancelHint, Does.Contain("после текущего объекта"));
        }

        var permanentDialog = new DeleteProgressDialogViewModel(
            [netItem, localItem],
            permanent: true,
            monitor,
            new(monitor),
            new(new MemorySettings()),
            new FakeUiDispatcher(),
            NullLogger.Instance,
            withoutRecycleBin: withoutRecycleBin);

        Assert.That(permanentDialog.IsMixed, Is.False);
        Assert.That(permanentDialog.Items.Select(static row => row.IsPermanent), Is.All.True);

        permanentDialog.ExcludeCommand.Execute(permanentDialog.Items[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(permanentDialog.ForcedByNetwork, Is.False);
            Assert.That(permanentDialog.IsMixed, Is.False);
            Assert.That(permanentDialog.StartButtonText, Is.EqualTo("Удалить безвозвратно"));
            Assert.That(permanentDialog.ActionVerb, Is.EqualTo("удалены безвозвратно"));
        }
    }

    [Test]
    public async Task Смешанная_пометка_не_автозапускает_диалог_показывает_метки_и_удаляет_локальное_в_корзину()
    {
        using var sink = BindingErrorSink.Attach();
        var previousProbe = ScanMarksViewModel.SupportsRecycleBin;
        var previousChunk = DeleteOperationDiagnostics.RecycleChunk;
        var previousSingle = DeleteOperationDiagnostics.RecycleSingle;

        var localDir = Path.Combine(_root, "mix-local");
        var netDir = Path.Combine(_root, "mix-net");
        Directory.CreateDirectory(localDir);
        Directory.CreateDirectory(netDir);
        var local1 = Path.Combine(localDir, "a.txt");
        var local2 = Path.Combine(localDir, "b.txt");
        var net1 = Path.Combine(netDir, "n1.txt");
        var net2 = Path.Combine(netDir, "n2.txt");
        await File.WriteAllTextAsync(local1, "local1");
        await File.WriteAllTextAsync(local2, "local2");
        await File.WriteAllTextAsync(net1, "net1");
        await File.WriteAllTextAsync(net2, "net2");

        var recycledChunks = new List<IReadOnlyList<string>>();
        var recycledSingles = new List<string>();

        ScanMarksViewModel.SupportsRecycleBin = path => !path.StartsWith(netDir, StringComparison.OrdinalIgnoreCase);
        DeleteOperationDiagnostics.RecycleChunk = paths =>
        {
            recycledChunks.Add([.. paths]);
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        };
        DeleteOperationDiagnostics.RecycleSingle = path =>
        {
            recycledSingles.Add(path);
            File.Delete(path);
        };

        try
        {
            ISettingsStore settings = new MemorySettings();
            settings.SetBool(SettingsKeys.DeleteConfirm, false);
            settings.SetEnum(SettingsKeys.DeleteMode, DeleteMode.RecycleBin);

            var operations = new OperationPreferences(settings);
            var scanPreferences = new ScanPreferences(settings);
            var agent = new AgentPreferences(settings);
            var factory = new ScanNodeFactory(scanPreferences, agent, new FakeShellLauncher());

            var rootSpace = new DirectorySpace(_root, null, DateTime.Now, DateTime.Now);
            var localSpace = new DirectorySpace("mix-local", rootSpace, DateTime.Now, DateTime.Now);
            var netSpace = new DirectorySpace("mix-net", rootSpace, DateTime.Now, DateTime.Now);
            var localFile1 = localSpace.AddFile(new FileInfo(local1));
            var localFile2 = localSpace.AddFile(new FileInfo(local2));
            var netFile1 = netSpace.AddFile(new FileInfo(net1));
            var netFile2 = netSpace.AddFile(new FileInfo(net2));
            localFile1.Delete();
            localFile2.Delete();
            netFile1.Delete();
            netFile2.Delete();
            rootSpace.Add(localSpace);
            rootSpace.Add(netSpace);

            var rootNode = factory.CreateRoot(rootSpace, new(), null);
            ObservableCollection<ScanNodeViewModel> roots = [rootNode];
            var treemap = new ScanTreemapViewModel(roots);
            var summary = new ScanSummaryViewModel(false, static () => Task.FromResult(false));

            DeleteProgressDialogViewModel? captured = null;
            var dialogs = new NoopDialogs(show: modal =>
            {
                captured = modal as DeleteProgressDialogViewModel;
                return false;
            });

            var marks = new ScanMarksViewModel(
                dialogs,
                _services.GetRequiredService<DeleteProgressDialogFactory>(),
                operations,
                NullLogger.Instance,
                factory,
                roots,
                summary,
                treemap,
                static () => null,
                static _ => { },
                static () => { },
                static () => false,
                static () => { });

            marks.RecountMarked();
            await marks.DeleteMarkedCommand.ExecuteAsync(null);

            Assert.That(captured, Is.Not.Null);

            _ = _modals.ShowAsync(captured!);
            _host.Settle();

            try
            {
                var view = Descendant<DeleteProgressDialogView>(_window)!;
                var visibleTexts = Descendants<TextBlock>(view)
                    .Where(static block => block.IsVisible)
                    .Select(static block => block.Text)
                    .ToList();

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(captured!.IsIdle, Is.True, "При наличии сетевого элемента в смешанном списке автозапуск должен быть заблокирован.");
                    Assert.That(captured.IsMixed, Is.True);
                    Assert.That(captured.ForcedByNetwork, Is.True);
                    Assert.That(visibleTexts, Has.Some.EqualTo(captured.NetworkWarningText));
                    Assert.That(visibleTexts, Has.Some.EqualTo("В корзину"));
                    Assert.That(visibleTexts, Has.Some.EqualTo("Безвозвратно"));
                    Assert.That(sink.Errors, Is.Empty, () => string.Join(Environment.NewLine, sink.Errors));
                }
            }
            finally
            {
                _modals.RequestCancel();
                _host.Settle();
            }

            var journal = Path.Combine(AppStorage.DataDirectory, AppInfo.DeletionLogFileName);
            var journalOffset = File.Exists(journal) ? new FileInfo(journal).Length : 0;

            await captured.StartCommand.ExecuteAsync(null);

            string journalTail;
            using (var stream = new FileStream(journal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                stream.Seek(journalOffset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                journalTail = await reader.ReadToEndAsync();
            }

            using (Assert.EnterMultipleScope())
            {
                Assert.That(recycledChunks, Has.Count.EqualTo(1));
                Assert.That(recycledChunks[0], Is.EquivalentTo(new[] { local1, local2 }));
                Assert.That(recycledSingles, Is.Empty);
                Assert.That(File.Exists(local1), Is.False);
                Assert.That(File.Exists(local2), Is.False);
                Assert.That(File.Exists(net1), Is.False);
                Assert.That(File.Exists(net2), Is.False);
                Assert.That(captured.DeletedItems, Has.Count.EqualTo(4));
                Assert.That(captured.HasErrors, Is.False);
                Assert.That(captured.StatusText, Does.StartWith("Готово. В корзину: 2 из 2, безвозвратно: 2 из 2."));
                Assert.That(journalTail, Does.Contain("(в корзину: 2, безвозвратно: 2)"));
                Assert.That(journalTail, Does.Contain($"В КОРЗИНУ {local1}"));
                Assert.That(journalTail, Does.Contain($"БЕЗВОЗВРАТНО {net1}"));
            }
        }
        finally
        {
            ScanMarksViewModel.SupportsRecycleBin = previousProbe;
            DeleteOperationDiagnostics.RecycleChunk = previousChunk;
            DeleteOperationDiagnostics.RecycleSingle = previousSingle;
        }
    }

    [Test]
    public async Task Явный_безвозвратный_режим_и_чисто_корзинный_режим_сохраняют_своё_поведение()
    {
        var previousChunk = DeleteOperationDiagnostics.RecycleChunk;
        var previousSingle = DeleteOperationDiagnostics.RecycleSingle;

        var modeDir = Path.Combine(_root, "modes");
        Directory.CreateDirectory(modeDir);
        var recycleFile = Path.Combine(modeDir, "to-bin.txt");
        var permLocalFile = Path.Combine(modeDir, "perm-local.txt");
        var permNetFile = Path.Combine(modeDir, "perm-net.txt");
        await File.WriteAllTextAsync(recycleFile, "bin");
        await File.WriteAllTextAsync(permLocalFile, "perm-local");
        await File.WriteAllTextAsync(permNetFile, "perm-net");

        var recycled = new List<string>();
        DeleteOperationDiagnostics.RecycleChunk = paths =>
        {
            recycled.AddRange(paths);
            foreach (var path in paths)
            {
                File.Delete(path);
            }
        };
        DeleteOperationDiagnostics.RecycleSingle = path =>
        {
            recycled.Add(path);
            File.Delete(path);
        };

        try
        {
            var rootSpace = new DirectorySpace(modeDir, null, DateTime.Now, DateTime.Now);
            var recycleSpace = rootSpace.AddFile(new FileInfo(recycleFile));
            var permLocalSpace = rootSpace.AddFile(new FileInfo(permLocalFile));
            var permNetSpace = rootSpace.AddFile(new FileInfo(permNetFile));

            using var monitor = TestDiagnostics.Monitor();
            var recycleDialog = new DeleteProgressDialogViewModel(
                [recycleSpace],
                permanent: false,
                monitor,
                new(monitor),
                new(new MemorySettings()),
                new FakeUiDispatcher(),
                NullLogger.Instance);

            Assert.That(recycleDialog.IsMixed, Is.False);
            Assert.That(recycleDialog.ForcedByNetwork, Is.False);
            await recycleDialog.StartCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(recycled, Is.EqualTo(new[] { recycleFile }));
                Assert.That(File.Exists(recycleFile), Is.False);
                Assert.That(recycleDialog.StatusText, Does.StartWith("Готово. Перемещено в корзину: 1 из 1."));
            }

            recycled.Clear();

            HashSet<SpaceBase> withoutRecycleBin = [permNetSpace];
            var permanentDialog = new DeleteProgressDialogViewModel(
                [permLocalSpace, permNetSpace],
                permanent: true,
                monitor,
                new(monitor),
                new(new MemorySettings()),
                new FakeUiDispatcher(),
                NullLogger.Instance,
                withoutRecycleBin: withoutRecycleBin);

            Assert.That(permanentDialog.IsMixed, Is.False);
            await permanentDialog.StartCommand.ExecuteAsync(null);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(recycled, Is.Empty, "При явном режиме «Безвозвратно» корзина вызываться не должна.");
                Assert.That(File.Exists(permLocalFile), Is.False);
                Assert.That(File.Exists(permNetFile), Is.False);
                Assert.That(permanentDialog.StatusText, Does.StartWith("Готово. Удалено безвозвратно: 2 из 2."));
            }
        }
        finally
        {
            DeleteOperationDiagnostics.RecycleChunk = previousChunk;
            DeleteOperationDiagnostics.RecycleSingle = previousSingle;
        }
    }

    private static T? Descendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        return Descendants<T>(root).FirstOrDefault();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
