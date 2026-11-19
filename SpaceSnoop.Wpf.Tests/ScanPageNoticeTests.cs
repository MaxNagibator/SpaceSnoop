using KeepShell.Bootstrap;
using KeepShell.Testing;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanPageNoticeTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private ScanViewModel _scan = null!;
    private NoopDialogs _dialogs = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-notice", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "target"));
        Directory.CreateDirectory(Path.Combine(_root, "other"));

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _dialogs = new();
        _scan = ActivatorUtilities.CreateInstance<ScanViewModel>(_services, _dialogs);
    }

    [TearDown]
    public void TearDown()
    {
        _services?.Dispose();
        _logging?.Dispose();

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }
        catch (IOException exception)
        {
            TestContext.Out.WriteLine($"Не удалось удалить каталог стенда {_root}: {exception.Message}");
        }
    }

    [Test]
    public void Итог_прежнего_скана_прячется_при_выборе_новой_цели_и_переезжает_в_кнопку_возврата()
    {
        var previous = Arrange("other");

        Assert.That(_scan.ShowSummary, Is.True);

        _scan.OpenPickerCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ShowTargetPicker, Is.True);
            Assert.That(_scan.ShowSummary, Is.False);
            Assert.That(_scan.Summary.ReturnCaption, Is.EqualTo($"К результатам: other · {previous.TotalSizeText}"));
        }
    }

    [Test]
    public async Task Остановленный_первый_скан_о_прежнем_результате_молчит()
    {
        await CancelledScanAsync(Path.Combine(_root, "target"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.True);
            Assert.That(_scan.CancelledReleasedPrevious, Is.False);
        }
    }

    [TestCase(true, TestName = "Остановленный повторный скан называет папку и говорит, что прежний результат убран")]
    [TestCase(false, TestName = "Остановленный повторный скан без снятия прежнего результата о его потере молчит")]
    public async Task Остановленный_повторный_скан(bool release)
    {
        var target = Arrange("target");
        _scan.Preferences.ReleaseBeforeRescan = release;

        await CancelledScanAsync(target.AbsolutePath);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.True);
            Assert.That(_scan.CancelledCaption, Is.EqualTo($"Сканирование {target.AbsolutePath} остановлено"));
            Assert.That(_scan.CancelledReleasedPrevious, Is.EqualTo(release));
            Assert.That(_scan.RescanCancelledCommand.CanExecute(null), Is.True);
        }
    }

    [Test]
    public async Task Повторный_скан_и_Сканировать_снова_снимают_только_корень_своей_папки()
    {
        var other = Arrange("other");
        var target = Arrange("target");
        _scan.Preferences.ReleaseBeforeRescan = true;
        _scan.Preferences.ConfirmRescan = false;

        await CancelledScanAsync(target.AbsolutePath);
        var afterCancel = _scan.Roots.Select(static root => root.AbsolutePath).ToList();

        await _scan.RescanCancelledCommand.ExecuteAsync(null);
        await SettleDriveLabelsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                afterCancel,
                Is.EqualTo(
                    new[]
                    {
                        other.AbsolutePath,
                    }));

            Assert.That(
                _scan.Roots.Select(static root => root.AbsolutePath),
                Is.EqualTo(
                    new[]
                    {
                        target.AbsolutePath, other.AbsolutePath,
                    }));
        }
    }

    [Test]
    public async Task Повторный_скан_сетевого_пути_проверяет_каталог_вне_потока_интерфейса()
    {
        const string uncPath = @"\\hung-server\share\folder";
        var space = new DirectorySpace(uncPath, null, DateTime.Now, DateTime.Now);
        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
        _scan.SelectedDrive = uncPath;
        _scan.Preferences.ReleaseBeforeRescan = true;
        _scan.Preferences.ConfirmRescan = true;

        var previousExists = ScanTargetProbe.DirectoryExists;
        var uiThread = Environment.CurrentManagedThreadId;
        var probeThread = 0;

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        ScanTargetProbe.DirectoryExists = _ =>
        {
            Interlocked.Exchange(ref probeThread, Environment.CurrentManagedThreadId);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            return false;
        };

        try
        {
            var run = _scan.StartCommand.ExecuteAsync(null);

            Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(Volatile.Read(ref probeThread), Is.Not.EqualTo(uiThread));

            release.Set();
            await run.WaitAsync(TimeSpan.FromSeconds(5));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(_dialogs.LastWarningMessage, Does.Contain(uncPath));
                Assert.That(_scan.Roots, Has.Count.EqualTo(1));
            }
        }
        finally
        {
            release.Set();
            ScanTargetProbe.DirectoryExists = previousExists;
        }
    }

    [TestCase("target", true, TestName = "Скан новой цели накрывает дерево прежней")]
    [TestCase("other", false, TestName = "Скан той же цели дерево не накрывает")]
    public void Прежнее_дерево_во_время_скана(string scanning, bool stale)
    {
        var previous = Arrange("other");
        var target = Path.Combine(_root, scanning);

        _scan.IsScanning = true;
        _scan.ScanTargetPath = target;

        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(_scan.ShowStaleResult, Is.EqualTo(stale));
                Assert.That(_scan.StaleResultCaption, Is.EqualTo($"Прежний результат: {previous.AbsolutePath} – новый появится после обхода {target}"));
            }
        }
        finally
        {
            _scan.IsScanning = false;
        }
    }

    [Test]
    public void Прежний_результат_из_нескольких_корней_называет_первый_и_число_остальных()
    {
        Arrange("target");
        var previous = Arrange("other");
        var target = Path.Combine(_root, "third");
        var changed = new List<string?>();
        _scan.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        _scan.IsScanning = true;
        _scan.ScanTargetPath = target;

        try
        {
            Assert.That(_scan.StaleResultCaption, Is.EqualTo($"Прежний результат: {previous.AbsolutePath} и ещё 1 – новый появится после обхода {target}"));

            changed.Clear();
            _scan.Roots.RemoveAt(1);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(changed, Does.Contain(nameof(ScanViewModel.StaleResultCaption)));
                Assert.That(_scan.StaleResultCaption, Does.Not.Contain("и ещё"));
            }
        }
        finally
        {
            _scan.IsScanning = false;
        }
    }

    [Test]
    public async Task Сканировать_снова_для_исчезнувшей_папки_объясняет_причину_и_убирает_сообщение()
    {
        var target = Path.Combine(_root, "target");
        await CancelledScanAsync(target);
        Directory.Delete(target);

        await _scan.RescanCancelledCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.False);
            Assert.That(_scan.HasResult, Is.False);
            Assert.That(_dialogs.LastWarningMessage, Does.Contain(target));
            Assert.That(_scan.RescanCancelledCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public async Task Сканировать_снова_повторяет_остановленный_скан_той_же_папки()
    {
        var target = Path.Combine(_root, "target");
        await File.WriteAllTextAsync(Path.Combine(target, "file.bin"), "data");
        await CancelledScanAsync(target);

        await _scan.RescanCancelledCommand.ExecuteAsync(null);
        await SettleDriveLabelsAsync();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.False);
            Assert.That(_scan.HasResult, Is.True);
            Assert.That(_scan.Roots[0].AbsolutePath, Is.EqualTo(target));
            Assert.That(_scan.SelectedDrive, Is.EqualTo(target));
        }
    }

    [Test]
    public async Task Скрыть_убирает_сообщение_об_остановке()
    {
        await CancelledScanAsync(Path.Combine(_root, "target"));

        _scan.DismissCancelledCommand.Execute(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_scan.ScanWasCancelled, Is.False);
            Assert.That(_scan.RescanCancelledCommand.CanExecute(null), Is.False);
        }
    }

    [Test]
    public void Числа_строки_дерева_подписаны_подсказками()
    {
        Arrange("other");
        var root = _scan.Roots[0];
        root.ReloadChildren();
        var child = root.Children.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.FileCountHint, Is.EqualTo($"Файлов: {root.FileCountText} – вместе с вложенными папками"));
            Assert.That(root.SizeHint, Is.EqualTo($"Размер: {root.SizeText} – вместе с вложенными папками"));
            Assert.That(child.ShareHint, Is.EqualTo($"Доля от родителя: {child.ShareText}"));
        }
    }

    private async Task CancelledScanAsync(string path)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await _scan.ScanFromAutomationAsync(path, cancelled.Token);
        await SettleDriveLabelsAsync();
    }

    private async Task SettleDriveLabelsAsync()
    {
        await Task.WhenAll(_scan.Drives.Items.Select(static drive => drive.LoadAsync()));
    }

    private DirectorySpace Arrange(string name)
    {
        var path = Path.Combine(_root, name);
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        space.Add(new("branch", space, DateTime.Now, DateTime.Now));

        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));

        return space;
    }
}
