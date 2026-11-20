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
            Assert.That(recycleDialog.StartButtonText, Is.EqualTo("Удалить безвозвратно"));
            Assert.That(recycleDialog.ActionVerb, Is.EqualTo("удалены безвозвратно"));
        }

        recycleDialog.ExcludeCommand.Execute(recycleDialog.Items[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(recycleDialog.ForcedByNetwork, Is.False);
            Assert.That(recycleDialog.StartButtonText, Is.EqualTo("Удалить"));
            Assert.That(recycleDialog.ActionVerb, Is.EqualTo("перемещены в корзину"));
            Assert.That(recycleDialog.IntroText, Does.Contain("перемещены в корзину"));
            Assert.That(recycleDialog.CancelHint, Does.Contain("пачки"));
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

        permanentDialog.ExcludeCommand.Execute(permanentDialog.Items[0]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(permanentDialog.ForcedByNetwork, Is.False);
            Assert.That(permanentDialog.StartButtonText, Is.EqualTo("Удалить безвозвратно"));
            Assert.That(permanentDialog.ActionVerb, Is.EqualTo("удалены безвозвратно"));
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
