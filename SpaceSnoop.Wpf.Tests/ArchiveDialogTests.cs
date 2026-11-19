using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.Diagnostics;
using System.IO.Compression;

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

    private ArchiveProgressDialogViewModel Dialog(bool deleteOriginal)
    {
        ArchiveRequest request = new(
            _source,
            _target,
            2,
            10,
            deleteOriginal,
            CompressionLevel.Optimal,
            false);

        return new(request, new(), new FakeUiDispatcher(), NullLogger<ArchiveProgressDialogViewModel>.Instance);
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
