using SpaceSnoop.Wpf.Agent;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class OpenCodeAgentBackendTests
{
    private const string SessionId = "ses_05d0a2d73ffeVQZNzi0cV68epp";

    private static readonly string StepStart = Event("step_start", "part", """{"id":"prt_0","type":"step-start"}""");

    [Test]
    public void Новый_разговор_запускается_подкомандой_run_своим_агентом_и_фиксированным_заголовком()
    {
        var args = OpenCodeAgentBackend.BuildArguments(new() { Prompt = "привет" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args.Take(6), Is.EqualTo(new[] { "run", "--format", "json", "--pure", "--agent", OpenCodeAgentBackend.AgentName }));
            Assert.That(args, Does.Contain("--title"));
            Assert.That(args, Does.Not.Contain("--session"));
        }
    }

    [Test]
    public void Продолжение_сессии_идёт_ключом_session_вместо_заголовка()
    {
        var args = OpenCodeAgentBackend.BuildArguments(new() { Prompt = "тест", ResumeSessionId = SessionId });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("--session"));
            Assert.That(args, Does.Contain(SessionId));
            Assert.That(args, Does.Not.Contain("--title"));
        }
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("opencode/big-pickle", ExpectedResult = true)]
    public bool Модель_добавляется_только_когда_задана(string model)
    {
        var args = OpenCodeAgentBackend.BuildArguments(new() { Prompt = "тест", Model = model });

        return args.Contains("--model") && args.Contains(model);
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("high", ExpectedResult = true)]
    public bool Глубина_рассуждений_уходит_вариантом_модели(string effort)
    {
        var args = OpenCodeAgentBackend.BuildArguments(new() { Prompt = "тест", Effort = effort });

        return args.Contains("--variant") && args.Contains(effort);
    }

    [Test]
    public void Встроенные_инструменты_выключены_и_запрещены()
    {
        var agent = Agent(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест", Mcp = Mcp() }));

        using (Assert.EnterMultipleScope())
        {
            foreach (var tool in OpenCodeAgentBackend.BuiltInTools)
            {
                Assert.That(agent.GetProperty("tools").GetProperty(tool).GetBoolean(), Is.False, tool);
                Assert.That(agent.GetProperty("permission").GetProperty(tool).GetString(), Is.EqualTo("deny"), tool);
            }

            Assert.That(agent.GetProperty("permission").GetProperty("external_directory").GetString(), Is.EqualTo("deny"));
            Assert.That(agent.GetProperty("permission").GetProperty("doom_loop").GetString(), Is.EqualTo("deny"));
        }
    }

    [Test]
    public void Оболочка_системы_запрещена_даже_без_настроенного_Mcp()
    {
        var agent = Agent(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест" }));

        Assert.That(agent.GetProperty("tools").GetProperty("bash").GetBoolean(), Is.False);
    }

    [Test]
    public void Инструменты_приложения_разрешены_под_именем_с_префиксом_сервера()
    {
        var agent = Agent(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест", Mcp = Mcp("sync_current") }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(agent.GetProperty("tools").GetProperty("spacesnoop_scan_directory").GetBoolean(), Is.True);
            Assert.That(agent.GetProperty("permission").GetProperty("spacesnoop_scan_directory").GetString(), Is.EqualTo("allow"));
            Assert.That(agent.GetProperty("tools").GetProperty("spacesnoop_sync_current").GetBoolean(), Is.False);
            Assert.That(agent.GetProperty("permission").GetProperty("spacesnoop_sync_current").GetString(), Is.EqualTo("deny"));
        }
    }

    [Test]
    public void Разрешённые_мутации_открывают_sync_current()
    {
        var mcp = new AgentMcpConfig("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory", "sync_current"], []);
        var agent = Agent(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест", Mcp = mcp }));

        Assert.That(agent.GetProperty("tools").GetProperty("spacesnoop_sync_current").GetBoolean(), Is.True);
    }

    [Test]
    public void Сервер_описывается_удалённым_адресом_а_токен_подстановкой_переменной()
    {
        using var document = JsonDocument.Parse(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест", Mcp = Mcp() }));
        var server = document.RootElement.GetProperty("mcp").GetProperty("spacesnoop");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.GetProperty("type").GetString(), Is.EqualTo("remote"));
            Assert.That(server.GetProperty("url").GetString(), Is.EqualTo("http://127.0.0.1:7654/mcp"));
            Assert.That(
                server.GetProperty("headers").GetProperty("Authorization").GetString(),
                Is.EqualTo($"Bearer {{env:{OpenCodeAgentBackend.TokenVariable}}}"));
        }
    }

    [Test]
    public void Токен_во_временный_файл_не_записывается()
    {
        var json = OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест", Mcp = Mcp() });

        Assert.That(json, Does.Not.Contain("secret-token"));
    }

    [Test]
    public void Пользовательские_инструкции_и_публикация_сессии_отключены()
    {
        using var document = JsonDocument.Parse(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "тест" }));
        var root = document.RootElement;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(root.GetProperty("share").GetString(), Is.EqualTo("disabled"));
            Assert.That(root.GetProperty("autoupdate").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("snapshot").GetBoolean(), Is.False);
            Assert.That(root.GetProperty("instructions").GetArrayLength(), Is.Zero);
        }
    }

    [Test]
    public void Системный_промпт_уезжает_промптом_агента()
    {
        var agent = Agent(OpenCodeAgentBackend.BuildConfigJson(new() { Prompt = "куда делось место", SystemPrompt = "Ты помощник." }));

        Assert.That(agent.GetProperty("prompt").GetString(), Is.EqualTo("Ты помощник."));
    }

    [Test]
    public void Начало_шага_даёт_событие_Started_с_идентификатором_сессии()
    {
        Assert.That(new OpenCodeAgentBackend.StreamParser().Parse(StepStart), Is.EqualTo(AgentEvent.Begin(SessionId)));
    }

    [Test]
    public void Второе_начало_шага_второго_Started_не_даёт()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();
        parser.Parse(StepStart);

        Assert.That(parser.Parse(Event("step_start", "part", """{"id":"prt_5","type":"step-start"}""")), Is.Null);
    }

    [Test]
    public void Текстовая_часть_даёт_событие_Text()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();

        Assert.That(parser.Parse(TextPart("prt_1", "Нашёл главного пожирателя.")), Is.EqualTo(AgentEvent.Chunk("Нашёл главного пожирателя.")));
    }

    [Test]
    public void Вторая_текстовая_часть_отделяется_пустой_строкой()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();
        parser.Parse(TextPart("prt_1", "Проверяю."));

        Assert.That(parser.Parse(TextPart("prt_2", "340 ГБ в node_modules.")), Is.EqualTo(AgentEvent.Chunk("\n\n340 ГБ в node_modules.")));
    }

    [Test]
    public void Повторно_присланная_часть_второго_события_не_даёт()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();
        parser.Parse(TextPart("prt_1", "Проверяю."));

        Assert.That(parser.Parse(TextPart("prt_1", "Проверяю.")), Is.Null);
    }

    [Test]
    public void Вызов_инструмента_даёт_событие_ToolCall_с_полным_именем()
    {
        var line = Event("tool_use", "part", """{"id":"prt_3","type":"tool","tool":"spacesnoop_scan_directory","state":{"status":"completed"}}""");

        Assert.That(new OpenCodeAgentBackend.StreamParser().Parse(line), Is.EqualTo(AgentEvent.Tool("spacesnoop_scan_directory")));
    }

    [Test]
    public void Аргументы_вызова_берутся_из_состояния_части()
    {
        var line = Event("tool_use", "part", """{"id":"prt_3","type":"tool","tool":"spacesnoop_scan_directory","state":{"status":"completed","input":{"path":"C:\\Data"}}}""");

        Assert.That(new OpenCodeAgentBackend.StreamParser().Parse(line), Is.EqualTo(AgentEvent.Tool("spacesnoop_scan_directory", """{"path":"C:\\Data"}""")));
    }

    [Test]
    public void Итог_хода_собирается_после_конца_потока_из_всех_шагов()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();
        parser.Parse(StepStart);
        parser.Parse(StepFinish("prt_10", input: 24986, output: 14, reasoning: 27, cost: 0.25));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parser.Parse(StepFinish("prt_11", input: 31200, output: 120, reasoning: 0, cost: 0.5)), Is.Null);
            Assert.That(parser.Complete(), Is.EqualTo(AgentEvent.Done(SessionId, costUsd: 0.75, tokens: 31200 + 14 + 27 + 120)));
        }
    }

    [Test]
    public void Не_начавшийся_ход_итога_не_даёт()
    {
        Assert.That(new OpenCodeAgentBackend.StreamParser().Complete(), Is.Null);
    }

    [Test]
    public void Ошибка_даёт_событие_Failed_и_запоминается_подсказкой()
    {
        var parser = new OpenCodeAgentBackend.StreamParser();
        var line = Event("error", "error", """{"name":"APIError","data":{"message":"No provider available","statusCode":401}}""");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(parser.Parse(line), Is.EqualTo(AgentEvent.Fail("No provider available")));
            Assert.That(parser.FailureHint, Is.EqualTo("No provider available"));
        }
    }

    [Test]
    public void Ошибка_без_текста_называется_хотя_бы_своим_типом()
    {
        var line = Event("error", "error", """{"name":"UnknownError"}""");

        Assert.That(new OpenCodeAgentBackend.StreamParser().Parse(line), Is.EqualTo(AgentEvent.Fail("UnknownError")));
    }

    [TestCase("""{"type":"reasoning","sessionID":"ses_1","part":{"id":"prt_9","type":"reasoning","text":"…"}}""")]
    [TestCase("""{"type":"step_finish","sessionID":"ses_1"}""")]
    [TestCase("не json вообще")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("[]")]
    public void Служебная_или_испорченная_строка_не_даёт_события(string line)
    {
        Assert.That(new OpenCodeAgentBackend.StreamParser().Parse(line), Is.Null);
    }

    private static AgentMcpConfig Mcp(params string[] denied)
    {
        return new("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory", "list_profiles"], denied);
    }

    private static string TextPart(string partId, string text)
    {
        return Event("text", "part", JsonSerializer.Serialize(new { id = partId, type = "text", text, time = new { start = 1, end = 2 } }));
    }

    private static string StepFinish(string partId, long input, long output, long reasoning, double cost)
    {
        var part = new
        {
            id = partId,
            type = "step-finish",
            reason = "stop",
            tokens = new { total = input + output + reasoning, input, output, reasoning },
            cost,
        };

        return Event("step_finish", "part", JsonSerializer.Serialize(part));
    }

    private static string Event(string type, string property, string value)
    {
        return $$"""{"sessionID":"{{SessionId}}","timestamp":1785145456055,"{{property}}":{{value}},"type":"{{type}}"}""";
    }

    private static JsonElement Agent(string json)
    {
        using var document = JsonDocument.Parse(json);

        return document.RootElement.GetProperty("agent").GetProperty(OpenCodeAgentBackend.AgentName).Clone();
    }
}
