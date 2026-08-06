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

    private ArchiveProgressDialogViewModel Dialog(bool deleteOriginal)
    {
        ArchiveRequest request = new(
            _source,
            _target,
            2,
            10,
            deleteOriginal,
            CompressionLevel.Optimal,
            Interactive: false);

        return new(request, new(), NullLogger<ArchiveProgressDialogViewModel>.Instance);
    }
}
