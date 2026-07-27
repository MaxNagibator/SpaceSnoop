using SpaceSnoop.Wpf.Agent;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class CodexAgentBackendTests
{
    private static AgentMcpConfig Mcp(params string[] denied)
    {
        return new("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory", "list_profiles"], denied);
    }

    [Test]
    public void Минимальный_запрос_держит_подкоманду_и_чтение_промпта_из_stdin()
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "привет" });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args[0], Is.EqualTo("exec"));
            Assert.That(args[1], Is.EqualTo("-"));
            Assert.That(args, Does.Contain("--json"));
            Assert.That(args, Does.Contain("--ignore-user-config"));
            Assert.That(args, Does.Contain("--strict-config"));
            Assert.That(args, Does.Contain("sandbox_mode=\"danger-full-access\""));
            Assert.That(args, Does.Contain("approval_policy=\"never\""));
            Assert.That(args, Does.Contain("project_doc_max_bytes=0"));
            Assert.That(args, Does.Contain("tools.web_search=false"));
        }
    }

    [Test]
    public void Продолжение_сессии_идёт_подкомандой_resume_перед_промптом()
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", ResumeSessionId = "019f9f78-2608-7ab2-9ff0-485e4fe26a52" });

        Assert.That(args.Take(4), Is.EqualTo(new[] { "exec", "resume", "019f9f78-2608-7ab2-9ff0-485e4fe26a52", "-" }));
    }

    [Test]
    public void Mcp_описывается_адресом_переменной_токена_и_белым_списком()
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", Mcp = Mcp() });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("mcp_servers.spacesnoop.url=\"http://127.0.0.1:7654/mcp\""));
            Assert.That(args, Does.Contain($"mcp_servers.spacesnoop.bearer_token_env_var=\"{CodexAgentBackend.TokenVariable}\""));
            Assert.That(args, Does.Contain("mcp_servers.spacesnoop.enabled_tools=[\"scan_directory\",\"list_profiles\"]"));
            Assert.That(args.Any(argument => argument.StartsWith("mcp_servers.spacesnoop.disabled_tools", StringComparison.Ordinal)), Is.False);
        }
    }

    [Test]
    public void Токен_уходит_переменной_окружения_а_не_аргументом()
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", Mcp = Mcp() });

        Assert.That(args.Any(argument => argument.Contains("secret-token", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public void Запрещённые_инструменты_попадают_в_disabled_tools()
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", Mcp = Mcp("sync_current") });

        Assert.That(args, Does.Contain("mcp_servers.spacesnoop.disabled_tools=[\"sync_current\"]"));
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("gpt-5.6", ExpectedResult = true)]
    public bool Модель_добавляется_только_когда_задана(string model)
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", Model = model });

        return args.Contains("--model") && args.Contains(model);
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("high", ExpectedResult = true)]
    public bool Глубина_рассуждений_уходит_ключом_конфигурации(string effort)
    {
        var args = CodexAgentBackend.BuildArguments(new() { Prompt = "тест", Effort = effort });

        return args.Contains($"model_reasoning_effort=\"{effort}\"");
    }

    [Test]
    public void Системный_промпт_уезжает_преамбулой_к_первому_сообщению()
    {
        var stdin = CodexAgentBackend.BuildStdin(new() { Prompt = "куда делось место", SystemPrompt = "Ты помощник." });

        Assert.That(stdin, Is.EqualTo("Ты помощник.\n\nкуда делось место"));
    }

    [Test]
    public void При_продолжении_сессии_промпт_не_повторяется()
    {
        var stdin = CodexAgentBackend.BuildStdin(new() { Prompt = "а теперь удали", SystemPrompt = "Ты помощник.", ResumeSessionId = "abc" });

        Assert.That(stdin, Is.EqualTo("а теперь удали"));
    }

    [Test]
    public void Начало_треда_даёт_событие_Started()
    {
        const string line = """
                            {"type":"thread.started","thread_id":"019f9f78-2608-7ab2-9ff0-485e4fe26a52"}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Begin("019f9f78-2608-7ab2-9ff0-485e4fe26a52")));
    }

    [Test]
    public void Сообщение_агента_даёт_событие_Text()
    {
        const string line = """
                            {"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"Проверяю текущее состояние приложения."}}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Chunk("Проверяю текущее состояние приложения.")));
    }

    [Test]
    public void Второе_сообщение_хода_отделяется_пустой_строкой()
    {
        var parser = new CodexAgentBackend.StreamParser();

        parser.Parse("""
                     {"type":"item.completed","item":{"id":"item_0","type":"agent_message","text":"Проверяю."}}
                     """);

        var second = parser.Parse("""
                                  {"type":"item.completed","item":{"id":"item_2","type":"agent_message","text":"SpaceSnoop 2.8.38 запущен."}}
                                  """);

        Assert.That(second, Is.EqualTo(AgentEvent.Chunk("\n\nSpaceSnoop 2.8.38 запущен.")));
    }

    [Test]
    public void Начало_вызова_инструмента_даёт_событие_ToolCall()
    {
        const string line = """
                            {"type":"item.started","item":{"id":"item_1","type":"mcp_tool_call","server":"spacesnoop","tool":"get_app_state","arguments":{},"result":null,"error":null,"status":"in_progress"}}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Tool("get_app_state")));
    }

    [Test]
    public void Завершение_вызова_инструмента_не_даёт_второго_бейджа()
    {
        const string line = """
                            {"type":"item.completed","item":{"id":"item_1","type":"mcp_tool_call","server":"spacesnoop","tool":"get_app_state","arguments":{},"result":{"content":[{"type":"text","text":"{}"}],"structured_content":null},"error":null,"status":"completed"}}
                            """;

        Assert.That(Parse(line), Is.Null);
    }

    [Test]
    public void Запуск_команды_оболочки_виден_отдельным_вызовом()
    {
        const string line = """
                            {"type":"item.started","item":{"id":"item_1","type":"command_execution","command":"bash -lc ls","status":"in_progress"}}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Tool(AgentPrompt.ShellTool)));
    }

    [Test]
    public void Завершение_хода_несёт_токены_вместо_стоимости()
    {
        const string line = """
                            {"type":"turn.completed","usage":{"input_tokens":51437,"cached_input_tokens":47360,"cache_write_input_tokens":0,"output_tokens":106,"reasoning_output_tokens":15}}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Done(sessionId: null, costUsd: 0, tokens: 51543)));
    }

    [Test]
    public void Провал_хода_даёт_событие_Failed()
    {
        const string line = """
                            {"type":"turn.failed","error":{"message":"The 'нет-такой-модели' model is not supported when using Codex with a ChatGPT account."}}
                            """;

        Assert.That(Parse(line), Is.EqualTo(AgentEvent.Fail("The 'нет-такой-модели' model is not supported when using Codex with a ChatGPT account.")));
    }

    [TestCase("""{"type":"turn.started"}""")]
    [TestCase("""{"type":"error","message":"HTTP 400"}""")]
    [TestCase("""{"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata not found."}}""")]
    [TestCase("""{"type":"item.updated","item":{"id":"item_1","type":"command_execution","status":"in_progress"}}""")]
    public void Служебные_и_повторяющие_строки_игнорируются(string line)
    {
        Assert.That(Parse(line), Is.Null);
    }

    [TestCase("2026-07-26T17:14:19.590075Z ERROR codex_models_manager: failed")]
    [TestCase("не json вообще")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("[]")]
    public void Испорченная_или_чужая_строка_не_бросает_исключение(string line)
    {
        Assert.That(Parse(line), Is.Null);
    }

    [Test]
    public void Причина_обрыва_без_turn_failed_берётся_из_строки_error()
    {
        var parser = new CodexAgentBackend.StreamParser();

        parser.Parse("""
                     {"type":"error","message":"HTTP 401: требуется токен доступа"}
                     """);

        Assert.That(parser.FailureHint, Is.EqualTo("HTTP 401: требуется токен доступа"));
    }

    [Test]
    public void Причина_обрыва_берётся_и_из_элемента_error()
    {
        var parser = new CodexAgentBackend.StreamParser();

        parser.Parse("""
                     {"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata not found."}}
                     """);

        Assert.That(parser.FailureHint, Is.EqualTo("Model metadata not found."));
    }

    [Test]
    public void Успешный_ход_подсказки_об_обрыве_не_оставляет()
    {
        var parser = new CodexAgentBackend.StreamParser();

        parser.Parse("""
                     {"type":"error","message":"HTTP 429: попробую ещё раз"}
                     """);

        parser.Parse("""
                     {"type":"turn.completed","usage":{"input_tokens":10,"output_tokens":2}}
                     """);

        Assert.That(parser.FailureHint, Is.Null);
    }

    [Test]
    public void Ошибка_потока_вынимается_для_журнала()
    {
        const string line = """
                            {"type":"error","message":"HTTP 401: требуется токен доступа"}
                            """;

        Assert.That(CodexAgentBackend.ParseStreamError(line), Is.EqualTo("HTTP 401: требуется токен доступа"));
    }

    [TestCase("""{"type":"turn.completed","usage":{}}""")]
    [TestCase("не json")]
    public void Строка_без_ошибки_не_даёт_записи_в_журнал(string line)
    {
        Assert.That(CodexAgentBackend.ParseStreamError(line), Is.Null);
    }

    private static AgentEvent? Parse(string line)
    {
        return new CodexAgentBackend.StreamParser().Parse(line);
    }
}
