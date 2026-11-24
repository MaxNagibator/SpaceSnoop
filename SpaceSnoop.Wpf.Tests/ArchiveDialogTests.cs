using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Core.Domain;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using SpaceSnoop.Wpf.ViewModels.Scan;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Windows.Threading;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class ArchiveDialogTests
{
    private string _root = string.Empty;
    private string _source = string.Empty;
    private string _target = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-archive-dialog-" + Guid.NewGuid().ToString("N"));
        _source = Path.Combine(_root, "source");
        _target = Path.Combine(_root, "source.zip");

        Directory.CreateDirectory(Path.Combine(_source, "nested"));
        File.WriteAllText(Path.Combine(_source, "a.txt"), "alpha");
        File.WriteAllText(Path.Combine(_source, "nested", "b.txt"), "bravo");
    }

    [TearDown]
    public void TearDown()
    {
        RemoveTree(_root);
    }

    [Test]
    public async Task Альтернативный_поток_NTFS_даёт_отказ_покрытия_и_сохраняет_оригинал()
    {
        var file = Path.Combine(_source, "a.txt");
        await File.WriteAllTextAsync($"{file}:secret", "hidden");

        var dialog = Dialog(deleteOriginal: true);

        await dialog.StartCommand.ExecuteAsync(null);

        var secretText = await File.ReadAllTextAsync($"{file}:secret");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.OriginalDeleted, Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
            Assert.That(secretText, Is.EqualTo("hidden"));
            Assert.That(File.Exists(_target), Is.True);
            Assert.That(dialog.HasErrors, Is.True);
            Assert.That(dialog.StatusText, Does.Contain("покрыт не полностью").And.Contain("альтернативный поток NTFS"));
        }
    }

    [Test]
    public async Task Выключенное_удаление_не_трогает_ни_оригинал_ни_признак_отказа()
    {
        var dialog = Dialog(deleteOriginal: false);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.OriginalDeleted, Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
            Assert.That(dialog.HasErrors, Is.False);
            Assert.That(dialog.CreatedArchivePath, Is.EqualTo(_target));
            Assert.That(File.Exists(_target), Is.True);
        }
    }

    [Test]
    public async Task Провал_покрытия_оставляет_оригинал_на_месте()
    {
        File.SetLastWriteTime(Path.Combine(_source, "a.txt"), new(2200, 1, 1, 0, 0, 0, DateTimeKind.Local));

        var dialog = Dialog(deleteOriginal: true);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.OriginalDeleted, Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
            Assert.That(File.Exists(_target), Is.True);
            Assert.That(dialog.HasErrors, Is.True);
            Assert.That(dialog.StatusText, Does.Contain("покрыт не полностью"));
            Assert.That(dialog.CreatedArchivePath, Is.EqualTo(_target));
        }
    }

    [Test]
    public async Task Ссылка_внутри_каталога_даёт_отказ_покрытия_и_сохраняет_оригинал()
    {
        var outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "ext.txt"), "external");

        var junction = Path.Combine(_source, "link");
        Assert.That(CreateJunction(junction, outside), Is.True);

        var dialog = Dialog(deleteOriginal: true);

        await dialog.StartCommand.ExecuteAsync(null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.OriginalDeleted, Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
            Assert.That(Directory.Exists(junction), Is.True);
            Assert.That(File.Exists(_target), Is.True);
            Assert.That(dialog.HasErrors, Is.True);
            Assert.That(dialog.StatusText, Does.Contain("покрыт не полностью").And.Contain("точка повторного разбора"));
        }
    }

    [Test]
    public async Task Упаковка_объединяет_обновления_прогресса_и_показывает_полный_счёт()
    {
        const int added = 30;

        for (var index = 0; index < added; index++)
        {
            await File.WriteAllTextAsync(Path.Combine(_source, $"file{index}.txt"), "data");
        }

        var dialog = Dialog(deleteOriginal: false);
        var updates = 0;
        dialog.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(dialog.CountText))
            {
                updates++;
            }
        };

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

        try
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updates, Is.Positive.And.LessThan(added / 2));
            Assert.That(dialog.CountText, Is.EqualTo("32 / 32"));
            Assert.That(dialog.CountLabel, Is.EqualTo("Упаковано"));
        }
    }

    [Test]
    public async Task Сверка_покрытия_обновляет_счётчик_и_долю_при_включённом_удалении()
    {
        var largeFile = Path.Combine(_source, "a.txt");
        await File.WriteAllBytesAsync(largeFile, new byte[160 * 1024]);
        var totalBytes = new FileInfo(largeFile).Length + new FileInfo(Path.Combine(_source, "nested", "b.txt")).Length;

        var dialog = Dialog(deleteOriginal: true);
        var sawDeterminateCoverage = false;
        var injectedLateFile = false;

        dialog.PropertyChanged += (_, _) =>
        {
            if (dialog.StatusText == "Сверка архива с каталогом…" && !dialog.IsIndeterminate)
            {
                sawDeterminateCoverage = true;

                if (!injectedLateFile)
                {
                    injectedLateFile = true;
                    File.WriteAllText(Path.Combine(_source, "late.txt"), "late");
                }
            }
        };

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

        try
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var expectedTotalText = SizeFormatter.Format(totalBytes);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sawDeterminateCoverage, Is.True);
            Assert.That(dialog.IsIndeterminate, Is.False);
            Assert.That(dialog.CountLabel, Is.EqualTo("Сверено"));
            Assert.That(dialog.CountText, Is.EqualTo($"2 / 2 · {expectedTotalText} / {expectedTotalText}"));
            Assert.That(dialog.ProgressValue, Is.EqualTo(1d));
            Assert.That(dialog.OriginalDeleted, Is.False);
            Assert.That(Directory.Exists(_source), Is.True);
        }
    }

    [Test]
    public async Task Сверка_покрытия_отражает_частичный_прогресс_при_отказе_на_следующем_файле()
    {
        var firstBytes = 160 * 1024;
        await File.WriteAllBytesAsync(Path.Combine(_source, "a.txt"), new byte[firstBytes]);
        var secondFile = Path.Combine(_source, "nested", "b.txt");
        var totalBytes = firstBytes + new FileInfo(secondFile).Length;

        var dialog = Dialog(deleteOriginal: true);
        FileStream? lockStream = null;

        dialog.PropertyChanged += (_, _) =>
        {
            if (lockStream is null && dialog.StatusText == "Сверка архива с каталогом…" && !dialog.IsIndeterminate)
            {
                lockStream = new(secondFile, FileMode.Open, FileAccess.Read, FileShare.None);
            }
        };

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

        try
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }
        finally
        {
            if (lockStream is not null)
            {
                await lockStream.DisposeAsync();
            }

            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.IsIndeterminate, Is.False);
            Assert.That(dialog.CountLabel, Is.EqualTo("Сверено"));
            Assert.That(dialog.CountText, Is.EqualTo($"1 / 2 · {SizeFormatter.Format(firstBytes)} / {SizeFormatter.Format(totalBytes)}"));
            Assert.That(dialog.ProgressValue, Is.EqualTo((double)firstBytes / totalBytes).Within(1e-6));
            Assert.That(dialog.HasErrors, Is.True);
            Assert.That(dialog.OriginalDeleted, Is.False);
        }
    }

    [Test]
    public async Task Успешная_сверка_и_удаление_переключают_индикатор_в_неопределённый_режим_на_время_корзины()
    {
        var deleteCalls = 0;
        var indeterminateDuringDelete = false;
        var statusDuringDelete = string.Empty;
        var currentDuringDelete = "dirty";
        var countDuringDelete = "dirty";
        var progressDuringDelete = -1d;

        ArchiveProgressDialogViewModel? dialog = null;
        dialog = Dialog(
            deleteOriginal: true,
            deleteDirectory: (_, _) =>
            {
                deleteCalls++;
                indeterminateDuringDelete = dialog!.IsIndeterminate;
                statusDuringDelete = dialog.StatusText;
                currentDuringDelete = dialog.CurrentPath;
                countDuringDelete = dialog.CountText;
                progressDuringDelete = dialog.ProgressValue;
            });

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

        try
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var expectedBytes = SizeFormatter.Format("alpha".Length + "bravo".Length);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(deleteCalls, Is.EqualTo(1));
            Assert.That(indeterminateDuringDelete, Is.True);
            Assert.That(statusDuringDelete, Is.EqualTo("Перемещение в корзину…"));
            Assert.That(currentDuringDelete, Is.Empty);
            Assert.That(countDuringDelete, Is.Empty);
            Assert.That(progressDuringDelete, Is.EqualTo(0d));
            Assert.That(dialog.OriginalDeleted, Is.True);
            Assert.That(dialog.IsIndeterminate, Is.False);
            Assert.That(dialog.ProgressValue, Is.EqualTo(1d));
            Assert.That(dialog.CountText, Is.EqualTo($"2 / 2 · {expectedBytes} / {expectedBytes}"));
            Assert.That(dialog.StatusText, Does.Contain("Оригинал → в корзину"));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Сверка_покрытия_пустого_каталога_и_нулевых_файлов_не_делит_на_ноль(bool emptyDirectory)
    {
        File.Delete(Path.Combine(_source, "a.txt"));
        File.Delete(Path.Combine(_source, "nested", "b.txt"));

        if (!emptyDirectory)
        {
            await File.WriteAllBytesAsync(Path.Combine(_source, "zero1.bin"), []);
            await File.WriteAllBytesAsync(Path.Combine(_source, "nested", "zero2.bin"), []);
        }

        var dialog = Dialog(deleteOriginal: true, deleteDirectory: (_, _) => { });

        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

        try
        {
            await dialog.StartCommand.ExecuteAsync(null);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dialog.OriginalDeleted, Is.True);
            Assert.That(dialog.HasErrors, Is.False);
            Assert.That(dialog.IsIndeterminate, Is.False);
            Assert.That(dialog.ProgressValue, Is.EqualTo(1d));
            Assert.That(dialog.CountText, Is.EqualTo(emptyDirectory ? "0 / 0" : "2 / 2"));
        }
    }

    [Test]
    [Apartment(ApartmentState.STA)]
    public void Метаданные_архива_читаются_в_фоне_и_не_блокируют_диспетчер_WPF()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var uiThreadId = Environment.CurrentManagedThreadId;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));

        try
        {
            using var readerEntered = new ManualResetEventSlim(false);
            using var allowRead = new ManualResetEventSlim(false);
            var readerThreadId = 0;

            var dialog = Dialog(
                deleteOriginal: false,
                readArchiveMetadata: path =>
                {
                    readerThreadId = Environment.CurrentManagedThreadId;
                    readerEntered.Set();

                    if (!allowRead.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Таймаут ожидания чтения метаданных архива.");
                    }

                    return ArchiveProgressDialogViewModel.ReadArchiveMetadata(path);
                });

            var run = dialog.StartCommand.ExecuteAsync(null);
            Assert.That(readerEntered.Wait(TimeSpan.FromSeconds(10)), Is.True);

            var dispatcherResponded = false;
            var probeFrame = new DispatcherFrame();
            _ = dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                () =>
                {
                    dispatcherResponded = true;
                    probeFrame.Continue = false;
                });
            Dispatcher.PushFrame(probeFrame);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(dispatcherResponded, Is.True);
                Assert.That(run.IsCompleted, Is.False);
                Assert.That(readerThreadId, Is.Not.EqualTo(uiThreadId));
            }

            allowRead.Set();

            var completionFrame = new DispatcherFrame();
            _ = run.ContinueWith(
                _ => dispatcher.BeginInvoke(() => completionFrame.Continue = false),
                TaskScheduler.Default);

            if (!run.IsCompleted)
            {
                Dispatcher.PushFrame(completionFrame);
            }

            run.GetAwaiter().GetResult();

            Assert.That(dialog.CreatedArchive, Is.Not.Null);
            var expectedMetadata = dialog.CreatedArchive!.Value;
            var expectedName = Path.GetFileName(_target);

            File.Delete(_target);

            var settings = new MemorySettings();
            var nodeFactory = new ScanNodeFactory(new ScanPreferences(settings), new AgentPreferences(settings), new FakeShellLauncher());
            var rootSpace = new DirectorySpace(_root, null, DateTime.Now, DateTime.Now);
            var sourceSpace = new DirectorySpace("source", rootSpace, DateTime.Now, DateTime.Now);
            rootSpace.Add(sourceSpace);
            ObservableCollection<ScanNodeViewModel> roots = [nodeFactory.CreateRoot(rootSpace, new(), null)];

            var added = ScanTreeEditor.AddArchiveToTree(roots, sourceSpace, expectedMetadata);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(added, Is.True);
                var addedFile = rootSpace.Files.Single(f => f.Name == expectedName);
                Assert.That(addedFile.TotalSize, Is.EqualTo(expectedMetadata.Length));
                Assert.That(addedFile.CreationDate, Is.EqualTo(expectedMetadata.CreationTime).Within(TimeSpan.FromMilliseconds(1)));
                Assert.That(addedFile.LastAccessTime, Is.EqualTo(expectedMetadata.LastAccessTime).Within(TimeSpan.FromMilliseconds(1)));
                Assert.That(roots[0].Children.Select(static c => c.Name), Does.Contain(expectedName));
            }
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private ArchiveProgressDialogViewModel Dialog(
        bool deleteOriginal,
        Action<string, bool>? deleteDirectory = null,
        Func<string, ArchiveFileMetadata>? readArchiveMetadata = null)
    {
        ArchiveRequest request = new(
            _source,
            _target,
            2,
            10,
            deleteOriginal,
            CompressionLevel.Optimal,
            false);

        var service = new ArchiveService();
        return new(
            request,
            service,
            new FakeUiDispatcher(),
            NullLogger<ArchiveProgressDialogViewModel>.Instance,
            deleteDirectory ?? service.DeleteDirectoryToRecycleBin,
            readArchiveMetadata: readArchiveMetadata);
    }

    private static bool CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(start);

        if (process is null)
        {
            return false;
        }

        process.WaitForExit();

        return process.ExitCode == 0;
    }

    private static void RemoveTree(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var directory = new DirectoryInfo(path);

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            directory.Delete();
            return;
        }

        foreach (var sub in directory.GetDirectories())
        {
            RemoveTree(sub.FullName);
        }

        directory.Delete(true);
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }
    }
}
