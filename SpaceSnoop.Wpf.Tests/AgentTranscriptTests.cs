using KeepShell.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceSnoop.Wpf.Agent;
using SpaceSnoop.Wpf.Bootstrap;
using SpaceSnoop.Wpf.Bootstrap.Storage;
using SpaceSnoop.Wpf.ViewModels.Settings;
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

        Assert.That(store.Begin(AgentBackendKind.Codex, null), Is.Null);
        Assert.That(Directory.Exists(store.DirectoryPath), Is.False);
    }

    [Test]
    public void Наблюдатель_пакета_пишет_вид_события_в_формате_приложения()
    {
        var store = Store(enabled: true);
        using (var transcript = store.Begin(AgentBackendKind.Antigravity, "секретный-токен"))
        {
            KeepShell.Agents.IAgentTranscript observer = transcript!;
            observer.Write(KeepShell.Agents.AgentTranscriptKind.Launch, "Antigravity: секретный-токен");
            observer.Write(KeepShell.Agents.AgentTranscriptKind.Exit, "exit 0");
        }

        var lines = File.ReadAllLines(SingleFile(store));
        Assert.Multiple(() =>
        {
            Assert.That(JsonDocument.Parse(lines[0]).RootElement.GetProperty("kind").GetString(), Is.EqualTo("launch"));
            Assert.That(JsonDocument.Parse(lines[1]).RootElement.GetProperty("kind").GetString(), Is.EqualTo("exit"));
            Assert.That(lines[0], Does.Contain("<токен>"));
            Assert.That(lines[0], Does.Not.Contain("секретный-токен"));
        });
    }

    [Test]
    public void Новый_транскрипт_вытесняет_только_самый_старый()
    {
        var store = Store(enabled: true);
        Directory.CreateDirectory(store.DirectoryPath);
        var existing = Enumerable.Range(0, AppDefaults.AgentTranscriptLimit)
            .Select(index => Path.Combine(store.DirectoryPath, $"turn-20000101-000000-{index:D3}-codex.jsonl"))
            .ToArray();
        foreach (var path in existing.Reverse())
        {
            File.WriteAllText(path, "предыдущий ход");
        }

        var notes = Path.Combine(store.DirectoryPath, "notes.txt");
        File.WriteAllText(notes, "заметка");

        string current;
        using (var transcript = store.Begin(AgentBackendKind.Codex, null))
        {
            Assert.That(transcript, Is.Not.Null);
            current = transcript!.Path;
        }

        Assert.That(Directory.GetFiles(store.DirectoryPath), Is.EquivalentTo(existing.Skip(1).Append(current).Append(notes)));
    }

    [Test]
    public void Строка_транскрипта_несёт_вид_и_текст()
    {
        var store = Store(enabled: true);

        using (var transcript = store.Begin(AgentBackendKind.Codex, null))
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

    private AgentTranscriptStore Store(bool enabled)
    {
        var preferences = new AgentPreferences(new MemorySettings())
        {
            Transcript = enabled,
        };

        return new(preferences, NullLogger<AgentTranscriptStore>.Instance, Path.Combine(_directory.FullName, "chat"));
    }

    private static string SingleFile(AgentTranscriptStore store)
    {
        var files = Directory.GetFiles(store.DirectoryPath);

        Assert.That(files, Has.Length.EqualTo(1));

        return files[0];
    }
}
