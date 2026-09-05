using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Core;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.ViewModels.Dialogs;
using System.IO;
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
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
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
    public async Task Экран_упаковки_обновляется_тиком_таймера_а_не_каждой_записью()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = Dialog(deleteOriginal: false, dispatcher);
        var updates = 0;

        dialog.PropertyChanged += (_, arguments) =>
        {
            if (arguments.PropertyName == nameof(dialog.CountText))
            {
                updates++;
            }
        };

        await RunInlineAsync(dialog);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(updates, Is.EqualTo(1), $"Экран обновился {updates} раз на двух записях – прогресс снова пушится на каждый объект.");
            Assert.That(dialog.CountText, Is.EqualTo("2 / 2"), "Последний срез прогресса до экрана не доехал.");
            Assert.That(dialog.CountLabel, Is.EqualTo("Упаковано"));
        }
    }

    [Test]
    public async Task Таймер_опроса_заводится_один_и_останавливается_после_упаковки()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialog = Dialog(deleteOriginal: false, dispatcher);

        await RunInlineAsync(dialog);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(dispatcher.Timers, Has.Count.EqualTo(1));
            Assert.That(dispatcher.Timers[0].Interval, Is.EqualTo(ArchiveProgressDialogViewModel.ProgressPollInterval));
            Assert.That(dispatcher.Timers[0].IsRunning, Is.False, "Таймер опроса пережил операцию.");
        }
    }

    private static async Task RunInlineAsync(ArchiveProgressDialogViewModel dialog)
    {
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
    }

    private ArchiveProgressDialogViewModel Dialog(bool deleteOriginal, FakeUiDispatcher? dispatcher = null)
    {
        ArchiveRequest request = new(
            _source,
            _target,
            2,
            10,
            deleteOriginal,
            CompressionLevel.Optimal,
            Interactive: false);

        return new(request, new(), dispatcher ?? new FakeUiDispatcher(), NullLogger<ArchiveProgressDialogViewModel>.Instance);
    }

    private sealed class InlineSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            callback(state);
        }
    }
}
