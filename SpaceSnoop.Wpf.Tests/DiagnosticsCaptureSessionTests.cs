using KeepShell.Bootstrap;
using KeepShell.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Diagnostics;
using System.IO.Compression;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public sealed class DiagnosticsCaptureSessionTests
{
    private string _root = string.Empty;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "spacesnoop-diagnostics-capture-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_root, true);
    }

    [Test]
    public void Заменённый_и_неизвестный_идентификаторы_не_сохраняют_пакет()
    {
        var captures = Create(new());
        var first = captures.Prepare(false);
        var second = captures.Prepare(false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(() => captures.Save(first.CaptureId), Throws.InvalidOperationException);
            Assert.That(() => captures.Save("unknown"), Throws.InvalidOperationException);
            Assert.That(Directory.Exists(Path.Combine(_root, "diagnostics")), Is.False);
        }

        Assert.That(File.Exists(captures.Save(second.CaptureId).Path), Is.True);
    }

    [Test]
    public void Неудачная_запись_оставляет_проверенный_снимок_для_повтора()
    {
        var captures = Create(new());
        var prepared = captures.Prepare(false);
        var directory = Path.Combine(_root, "diagnostics");
        File.WriteAllText(directory, "занято файлом");

        Assert.That(() => captures.Save(prepared.CaptureId), Throws.InstanceOf<IOException>());
        File.Delete(directory);

        Assert.That(File.Exists(captures.Save(prepared.CaptureId).Path), Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Сохранение_пишет_проверенный_снимок_и_вырезает_секреты(bool rawPaths)
    {
        var source = new MutableSource();
        var captures = Create(source);
        var prepared = captures.Prepare(rawPaths);

        Assert.That(Directory.Exists(Path.Combine(_root, "diagnostics")), Is.False);
        Assert.That(prepared.Contents, Does.Contain("sample.txt"));
        source.Text = "изменено после подготовки";

        var saved = captures.Save(prepared.CaptureId);
        using var zip = ZipFile.OpenRead(saved.Path);
        using var reader = new StreamReader(zip.GetEntry("sample.txt")!.Open());
        var text = reader.ReadToEnd();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(saved.CapturedAtUtc, Is.EqualTo(prepared.CapturedAtUtc));
            Assert.That(text, Does.Contain("исходный снимок"));
            Assert.That(text, Does.Not.Contain(source.Text));
            Assert.That(text, Does.Not.Contain("live-secret-value"));
            Assert.That(text.Contains("private-segment", StringComparison.Ordinal), Is.EqualTo(rawPaths));
            Assert.That(Path.GetDirectoryName(saved.Path), Is.EqualTo(Path.Combine(_root, "diagnostics")));
            Assert.That(() => captures.Save(prepared.CaptureId), Throws.InvalidOperationException);
        }
    }

    private DiagnosticsCaptureSession Create(MutableSource source)
    {
        var options = new DiagnosticsOptions
        {
            DataDirectory = _root,
            AppName = "SpaceSnoop",
            AppVersion = "test",
        };

        var logging = new KeepShellLoggingOptions
        {
            LogsDirectory = Path.Combine(_root, "logs"),
        };

        var collector = new DiagnosticsCollector(TestDiagnostics.Monitor(), new(options), options, logging, [source], [], []);
        return new(collector, NullLogger<DiagnosticsCaptureSession>.Instance);
    }

    private sealed class MutableSource : IDiagnosticsBundleSource
    {
        public string Text { get; set; } = "исходный снимок: C:\\private-segment\\sample.txt password=live-secret-value";

        public IEnumerable<DiagnosticsEntry> Collect()
        {
            yield return new("sample.txt", Text);
        }
    }
}
