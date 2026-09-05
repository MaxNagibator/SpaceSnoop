using SpaceSnoop.Wpf.Agent;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentBackendChoiceTests
{
    private static AgentBackendProbe Found(AgentBackendKind kind, string version = "1.0.0")
    {
        return new(kind, new($@"C:\cli\{kind}.exe", version));
    }

    private static AgentBackendProbe Missing(AgentBackendKind kind)
    {
        return new(kind, null);
    }

    [Test]
    public void Ни_одного_CLI_не_найдено_и_предлагать_нечего()
    {
        var choice = AgentBackendChoice.From(
            AgentBackendKind.Claude,
            [Missing(AgentBackendKind.Claude), Missing(AgentBackendKind.Codex), Missing(AgentBackendKind.OpenCode)]);

        Assert.Multiple(() =>
        {
            Assert.That(choice.NothingFound, Is.True);
            Assert.That(choice.Suggested, Is.Null);
            Assert.That(choice.Alternatives, Is.Empty);
        });
    }

    [Test]
    public void Выбранного_CLI_нет_и_единственный_найденный_становится_предложением()
    {
        var choice = AgentBackendChoice.From(
            AgentBackendKind.Claude,
            [Missing(AgentBackendKind.Claude), Found(AgentBackendKind.Codex), Missing(AgentBackendKind.OpenCode)]);

        Assert.Multiple(() =>
        {
            Assert.That(choice.CurrentFound, Is.False);
            Assert.That(choice.NothingFound, Is.False);
            Assert.That(choice.Suggested, Is.EqualTo(AgentBackendKind.Codex));
        });
    }

    [Test]
    public void Найденные_возвращаются_в_каноническом_порядке_независимо_от_порядка_проб()
    {
        var choice = AgentBackendChoice.From(
            AgentBackendKind.Codex,
            [Found(AgentBackendKind.OpenCode), Missing(AgentBackendKind.Codex), Found(AgentBackendKind.Claude)]);

        Assert.Multiple(() =>
        {
            Assert.That(choice.Alternatives, Is.EqualTo(new[] { AgentBackendKind.Claude, AgentBackendKind.OpenCode }));
            Assert.That(choice.Suggested, Is.EqualTo(AgentBackendKind.Claude));
        });
    }

    [Test]
    public void Найденный_выбранный_CLI_в_альтернативы_не_попадает()
    {
        var choice = AgentBackendChoice.From(
            AgentBackendKind.Codex,
            [Found(AgentBackendKind.Claude), Found(AgentBackendKind.Codex)]);

        Assert.Multiple(() =>
        {
            Assert.That(choice.CurrentFound, Is.True);
            Assert.That(choice.Alternatives, Is.EqualTo(new[] { AgentBackendKind.Claude }));
        });
    }

    [Test]
    public void Пустая_версия_считается_найденным_CLI()
    {
        var choice = AgentBackendChoice.From(
            AgentBackendKind.Claude,
            [Found(AgentBackendKind.Claude, string.Empty)]);

        Assert.Multiple(() =>
        {
            Assert.That(choice.CurrentFound, Is.True);
            Assert.That(choice.NothingFound, Is.False);
        });
    }
}
