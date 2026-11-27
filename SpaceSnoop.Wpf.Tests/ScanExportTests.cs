using KeepShell.Bootstrap;
using KeepShell.Testing;
using KeepShell.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels;
using SpaceSnoop.Wpf.ViewModels.Cleanup;
using SpaceSnoop.Wpf.ViewModels.Scan;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
[NonParallelizable]
public class ScanExportTests
{
    private string _root = null!;
    private KeepShellLogging _logging = null!;
    private ServiceProvider _services = null!;
    private FakeFilePicker _picker = null!;
    private NoopDialogs _dialogs = null!;
    private ScanViewModel _scan = null!;
    private ShellViewModel _shell = null!;

    [SetUp]
    public void SetUp()
    {
        _ = TestApplication.Ensure(AppResources.Sources);

        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-scan-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "target"));

        ISettingsStore settings = new MemorySettings();
        settings.SetBool(SettingsKeys.UpdateCheckOnStartup, false);

        _logging = KeepShellLogging.Bootstrap(
            new()
            {
                LogsDirectory = Path.Combine(_root, "logs"),
                FileNamePrefix = AppInfo.LogFilePrefix,
            });

        _services = App.ConfigureServices(settings, _logging);
        _picker = new();
        _dialogs = new();
        _scan = ActivatorUtilities.CreateInstance<ScanViewModel>(_services, _dialogs, _picker);
        _shell = ActivatorUtilities.CreateInstance<ShellViewModel>(_services, _scan);
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
    public async Task Отказ_в_выборе_файла_не_включает_состояние_экспорта()
    {
        ArrangeTree();
        var seenExportingDuringPicker = false;
        var seenExportingChange = false;

        _picker.SaveHandler = _ =>
        {
            seenExportingDuringPicker = _scan.IsExporting || _scan.IsBusy;
            return null;
        };

        _scan.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ScanViewModel.IsExporting))
            {
                seenExportingChange = true;
            }
        };

        await _scan.ExportFullScanCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(seenExportingDuringPicker, Is.False);
            Assert.That(seenExportingChange, Is.False);
            Assert.That(_scan.IsExporting, Is.False);
            Assert.That(_scan.IsBusy, Is.False);
            Assert.That(_scan.StatusCaption, Is.Null);
            Assert.That(((IPageStatus)_scan).CancelCommand?.CanExecute(null), Is.False);
            Assert.That(_scan.ExportFullScanCommand.CanExecute(null), Is.True);
        }
    }

    [TestCase(true, TestName = "Отмена через команду статус-бара останавливает экспорт и удерживает навигацию")]
    [TestCase(false, TestName = "Отмена через кнопку оверлея останавливает экспорт и удерживает навигацию")]
    public async Task Отмена_экспорта_защищает_навигацию_целевой_файл_и_убирает_временный(bool viaPageStatus)
    {
        ArrangeTree();
        var targetFile = Path.Combine(_root, "export.json");
        await File.WriteAllTextAsync(targetFile, "existing-content");
        _picker.SavePath = targetFile;

        var changedProperties = new List<string?>();
        var navBlockedDuringExport = false;
        var settingsBlockedDuringExport = false;
        var selectBlockedDuringExport = false;
        var dockerBlockedDuringExport = false;
        var statusDuringExport = string.Empty;

        _scan.PropertyChanged += (_, args) =>
        {
            changedProperties.Add(args.PropertyName);

            if (args.PropertyName == nameof(ScanViewModel.IsExporting) && _scan.IsExporting)
            {
                statusDuringExport = _scan.StatusCaption ?? string.Empty;
                navBlockedDuringExport = !_shell.TryNavigate(SectionKey.Logs) && _shell.Selected?.Key == SectionKey.Scan;
                dockerBlockedDuringExport = !_shell.TryNavigate(SectionKey.Docker);

                _shell.OpenSettingsCommand.Execute(null);
                settingsBlockedDuringExport = _shell.Selected?.Key == SectionKey.Scan;

                var logsItem = _shell.Sections.First(static item => item.Key == SectionKey.Logs);
                _shell.SelectCommand.Execute(logsItem);
                selectBlockedDuringExport = _shell.Selected?.Key == SectionKey.Scan;

                if (viaPageStatus)
                {
                    ((IPageStatus)_scan).CancelCommand!.Execute(null);
                }
                else
                {
                    _scan.CancelFullScanExportCommand.Execute(null);
                }
            }
        };

        await _scan.ExportFullScanCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(statusDuringExport, Does.StartWith("Сохранение полного скана:"));
            Assert.That(navBlockedDuringExport, Is.True);
            Assert.That(dockerBlockedDuringExport, Is.True);
            Assert.That(_services.GetRequiredService<CleanupPageViewModel>().IsDockerActive, Is.False);
            Assert.That(settingsBlockedDuringExport, Is.True);
            Assert.That(selectBlockedDuringExport, Is.True);
            Assert.That(changedProperties, Does.Contain(nameof(ScanViewModel.CancelCommand)));
            Assert.That(changedProperties, Does.Contain(nameof(ScanViewModel.IsBusy)));
            Assert.That(changedProperties, Does.Contain(nameof(ScanViewModel.StatusCaption)));
            Assert.That(await File.ReadAllTextAsync(targetFile), Is.EqualTo("existing-content"));
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
            Assert.That(_scan.IsExporting, Is.False);
            Assert.That(_scan.IsBusy, Is.False);
            Assert.That(_scan.StatusCaption, Is.Null);
            Assert.That(((IPageStatus)_scan).CancelCommand?.CanExecute(null), Is.False);
            Assert.That(_scan.CancelFullScanExportCommand.CanExecute(null), Is.False);
            Assert.That(_shell.IsNavigationLocked, Is.False);
            Assert.That(_shell.TryNavigate(SectionKey.Logs), Is.True);
        }
    }

    [Test]
    public async Task Ошибка_записи_и_успешный_экспорт_восстанавливают_состояние_и_очищают_временные_файлы()
    {
        ArrangeTree();
        var targetFile = Path.Combine(_root, "export.json");
        _picker.SavePath = targetFile;

        await using (var lockStream = new FileStream(targetFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            await _scan.ExportFullScanCommand.ExecuteAsync(null);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(_dialogs.LastErrorMessage, Is.Not.Null.And.Not.Empty);
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
            Assert.That(_scan.IsExporting, Is.False);
            Assert.That(_scan.IsBusy, Is.False);
            Assert.That(_scan.StatusCaption, Is.Null);
            Assert.That(_shell.IsNavigationLocked, Is.False);
        }

        await _scan.ExportFullScanCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(targetFile), Is.True);
            Assert.That(new FileInfo(targetFile).Length, Is.GreaterThan(0));
            Assert.That(Directory.GetFiles(_root, "*.tmp"), Is.Empty);
            Assert.That(_scan.IsExporting, Is.False);
            Assert.That(_scan.IsBusy, Is.False);
            Assert.That(_scan.StatusCaption, Is.Null);
            Assert.That(((IPageStatus)_scan).CancelCommand?.CanExecute(null), Is.False);
        }
    }

    private void ArrangeTree()
    {
        var path = Path.Combine(_root, "target");
        var space = new DirectorySpace(path, null, DateTime.Now, DateTime.Now);
        var filePath = Path.Combine(path, "payload.bin");
        File.WriteAllBytes(filePath, new byte[128]);
        space.AddFile(new(filePath));

        _scan.ApplyScanResult(space, TimeSpan.FromSeconds(1), null, ScanNotes.ForTraversal(1));
    }
}
