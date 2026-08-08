using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Settings;
using System.IO;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentTranscriptTests
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Directory.CreateTempSubdirectory("spacesnoop-transcript");
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Delete(true);
    }

    [Test]
    public void Без_настройки_транскрипт_не_заводится()
    {
        var store = Store(enabled: false);

        Assert.That(store.Begin(AgentBackendKind.Codex, token: null), Is.Null);
        Assert.That(Directory.Exists(store.DirectoryPath), Is.False);
    }

    [Test]
    public void Строка_транскрипта_несёт_вид_и_текст()
    {
        var store = Store(enabled: true);

        using (var transcript = store.Begin(AgentBackendKind.Codex, token: null))
        {
            Assert.That(transcript, Is.Not.Null);
            transcript.Write(AgentTranscriptKind.Stdout, """{"type":"turn.completed"}""");
        }

        var line = JsonDocument.Parse(File.ReadAllLines(SingleFile(store))[0]).RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(line.GetProperty("kind").GetString(), Is.EqualTo("stdout"));
            Assert.That(line.GetProperty("text").GetString(), Is.EqualTo("""{"type":"turn.completed"}"""));
            Assert.That(line.GetProperty("at").GetDateTimeOffset(), Is.Not.EqualTo(default(DateTimeOffset)));
        });
    }

    [Test]
    public void Токен_доступа_в_транскрипт_не_попадает()
    {
        var store = Store(enabled: true);

        using (var transcript = store.Begin(AgentBackendKind.Claude, "секретный-токен"))
        {
            transcript!.Write(AgentTranscriptKind.Config, """{"headers":{"Authorization":"Bearer секретный-токен"}}""");
        }

        var text = File.ReadAllText(SingleFile(store));

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Not.Contain("секретный-токен"));
            Assert.That(text, Does.Contain("<токен>"));
        });
    }

    [TestCase(AgentBackendKind.Claude, "claude")]
    [TestCase(AgentBackendKind.Codex, "codex")]
    [TestCase(AgentBackendKind.OpenCode, "opencode")]
    public void Имя_файла_называет_время_и_бэкенд(AgentBackendKind backend, string expected)
    {
        var name = AgentTranscriptStore.FileName(backend, new DateTimeOffset(2026, 7, 27, 16, 38, 54, 123, TimeSpan.Zero));

        Assert.That(name, Is.EqualTo($"turn-20260727-163854-123-{expected}.jsonl"));
    }

    [Test]
    public void Лишними_считаются_самые_старые_файлы()
    {
        string[] files =
        [
            @"C:\logs\chat\turn-20260727-163854-123-codex.jsonl",
            @"C:\logs\chat\turn-20260725-090000-000-claude.jsonl",
            @"C:\logs\chat\turn-20260726-120000-000-codex.jsonl",
        ];

        Assert.That(AgentTranscriptStore.Obsolete(files, keep: 1), Is.EqualTo(new[]
        {
            @"C:\logs\chat\turn-20260726-120000-000-codex.jsonl",
            @"C:\logs\chat\turn-20260725-090000-000-claude.jsonl",
        }));
    }

    [Test]
    public void Запуск_описывается_без_секретов_но_с_аргументами()
    {
        var request = new AgentRequest
        {
            Prompt = "куда делось место",
            Model = "gpt-5.6-luna",
            Mcp = new("spacesnoop", "http://127.0.0.1:7654/mcp", "секретный-токен", ["scan_directory"], []),
        };

        var launch = new AgentLaunch
        {
            Arguments = ["exec", "-", "--json"],
            Environment = new Dictionary<string, string> { ["SPACESNOOP_MCP_TOKEN"] = "секретный-токен" },
        };

        var text = AgentBackendBase.DescribeLaunch("Codex", new(@"C:\codex.exe", "0.145.0"), request, launch);

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("модель: gpt-5.6-luna"));
            Assert.That(text, Does.Contain("продолжение сессии: –"));
            Assert.That(text, Does.Contain("инструменты: scan_directory"));
            Assert.That(text, Does.Contain("аргументы: exec - --json"));
            Assert.That(text, Does.Not.Contain("секретный-токен"));
        });
    }

    private AgentTranscriptStore Store(bool enabled)
    {
        var preferences = new AgentPreferences(new MemorySettings()) { Transcript = enabled };

        return new(preferences, NullLogger<AgentTranscriptStore>.Instance, Path.Combine(_directory.FullName, "chat"));
    }

    private static string SingleFile(AgentTranscriptStore store)
    {
        var files = Directory.GetFiles(store.DirectoryPath);

        Assert.That(files, Has.Length.EqualTo(1));

        return files[0];
    }
}
