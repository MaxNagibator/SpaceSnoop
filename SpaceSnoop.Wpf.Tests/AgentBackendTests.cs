using SpaceSnoop.Wpf.Agent;
using System.Text.Json;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class AgentBackendTests
{
    private static readonly IReadOnlyList<string> ClaudeNames = AgentCli.ExecutableNames("claude");

    [Test]
    public void Явный_путь_используется_если_файл_существует()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            @"D:\custom\claude.exe",
            [@"C:\A"],
            candidate => candidate == @"D:\custom\claude.exe" || candidate == @"C:\A\claude.exe");

        Assert.That(path, Is.EqualTo(@"D:\custom\claude.exe"));
    }

    [Test]
    public void Несуществующий_явный_путь_не_блокирует_поиск_по_каталогам()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            @"D:\custom\claude.exe",
            [@"C:\A"],
            candidate => candidate == @"C:\A\claude.exe");

        Assert.That(path, Is.EqualTo(@"C:\A\claude.exe"));
    }

    [Test]
    public void Поиск_идёт_по_каталогам_по_порядку()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            null,
            [@"C:\First", @"C:\Second"],
            candidate => candidate == @"C:\Second\claude.cmd");

        Assert.That(path, Is.EqualTo(@"C:\Second\claude.cmd"));
    }

    [Test]
    public void Исполняемый_exe_предпочтительнее_cmd_и_bat()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            null,
            [@"C:\A"],
            candidate => candidate is @"C:\A\claude.exe" or @"C:\A\claude.cmd" or @"C:\A\claude.bat");

        Assert.That(path, Is.EqualTo(@"C:\A\claude.exe"));
    }

    [Test]
    public void Настоящий_exe_из_дальнего_каталога_выигрывает_у_ближнего_шима()
    {
        var path = AgentCli.ResolveExecutable(
            AgentCli.ExecutableNames("opencode"),
            null,
            [@"C:\npm", @"C:\npm\node_modules\opencode-ai\bin"],
            candidate => candidate is @"C:\npm\opencode.cmd" or @"C:\npm\node_modules\opencode-ai\bin\opencode.exe");

        Assert.That(path, Is.EqualTo(@"C:\npm\node_modules\opencode-ai\bin\opencode.exe"));
    }

    [Test]
    public void Ничего_не_найдено_возвращает_null()
    {
        var path = AgentCli.ResolveExecutable(ClaudeNames, null, [@"C:\A", @"C:\B"], _ => false);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void Явный_путь_с_чужим_именем_файла_игнорируется()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            @"D:\custom\evil.exe",
            [@"C:\A"],
            candidate => candidate == @"D:\custom\evil.exe" || candidate == @"C:\A\claude.exe");

        Assert.That(path, Is.EqualTo(@"C:\A\claude.exe"));
    }

    [Test]
    public void Явный_путь_сравнивает_имя_файла_без_учёта_регистра()
    {
        var path = AgentCli.ResolveExecutable(
            ClaudeNames,
            @"D:\custom\Claude.EXE",
            [@"C:\A"],
            candidate => candidate == @"D:\custom\Claude.EXE");

        Assert.That(path, Is.EqualTo(@"D:\custom\Claude.EXE"));
    }

    [Test]
    public void Имя_бэкенда_не_пускает_чужой_CLI_по_явному_пути()
    {
        var path = AgentCli.ResolveExecutable(
            AgentCli.ExecutableNames("codex"),
            @"D:\custom\claude.exe",
            [@"C:\A"],
            candidate => candidate is @"D:\custom\claude.exe" or @"C:\A\codex.exe");

        Assert.That(path, Is.EqualTo(@"C:\A\codex.exe"));
    }

    [Test]
    public void Настоящий_exe_запускается_напрямую()
    {
        var info = AgentCli.CreateStartInfo(@"C:\A\claude.exe", ["--model", "sonnet"]);

        Assert.Multiple(() =>
        {
            Assert.That(info!.FileName, Is.EqualTo(@"C:\A\claude.exe"));
            Assert.That(info.ArgumentList, Is.EqualTo(new[] { "--model", "sonnet" }));
            Assert.That(info.Arguments, Is.Empty);
        });
    }

    [TestCase(@"C:\A\claude.cmd")]
    [TestCase(@"C:\A\claude.BAT")]
    public void Шим_запускается_через_командный_процессор(string path)
    {
        var info = AgentCli.CreateStartInfo(path, ["--model", "sonnet"]);

        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(info!.FileName), Is.EqualTo("cmd.exe").IgnoreCase);
            Assert.That(info.ArgumentList, Is.Empty);
            Assert.That(info.Arguments, Is.EqualTo($"/s /c \"{path} --model sonnet\""));
        });
    }

    [TestCase(@"C:\Program Files\claude.cmd", ExpectedResult = "/s /c \"\"C:\\Program Files\\claude.cmd\" --model sonnet\"")]
    [TestCase(@"C:\A\claude.cmd", ExpectedResult = "/s /c \"C:\\A\\claude.cmd --model sonnet\"")]
    public string Пробел_в_пути_шима_уходит_в_кавычках(string path)
    {
        return AgentCli.BuildScriptArguments(path, ["--model", "sonnet"]);
    }

    [TestCase("модель&calc", ExpectedResult = "/s /c \"c.cmd \"модель&calc\"\"")]
    [TestCase("a|b", ExpectedResult = "/s /c \"c.cmd \"a|b\"\"")]
    [TestCase("с пробелом", ExpectedResult = "/s /c \"c.cmd \"с пробелом\"\"")]
    [TestCase("--model", ExpectedResult = "/s /c \"c.cmd --model\"")]
    public string Метасимволы_командного_процессора_не_разрывают_команду(string argument)
    {
        return AgentCli.BuildScriptArguments("c.cmd", [argument]);
    }

    [TestCase("gpt-%USERNAME%-sol")]
    [TestCase("x%\" & echo INJECTED & rem \"")]
    [TestCase("обычная\"кавычка")]
    public void Шим_не_запускается_с_аргументом_который_командный_процессор_раскроет(string argument)
    {
        Assert.That(AgentCli.CreateStartInfo(@"C:\A\claude.cmd", [argument]), Is.Null);
    }

    [Test]
    public void Процент_в_пути_шима_тоже_отменяет_запуск()
    {
        Assert.That(AgentCli.CreateStartInfo(@"C:\Tools\%TEMP%\claude.cmd", ["--model", "sonnet"]), Is.Null);
    }

    [Test]
    public void Тот_же_аргумент_у_настоящего_exe_запуску_не_мешает()
    {
        var info = AgentCli.CreateStartInfo(@"C:\A\claude.exe", ["gpt-%USERNAME%-sol"]);

        Assert.That(info?.ArgumentList, Is.EqualTo(new[] { "gpt-%USERNAME%-sol" }));
    }

    [TestCase("2.1.220 (Claude Code)", ExpectedResult = "2.1.220")]
    [TestCase("codex-cli 0.145.0", ExpectedResult = "0.145.0")]
    [TestCase("2.1.220", ExpectedResult = "2.1.220")]
    [TestCase("1.0.0\r\n", ExpectedResult = "1.0.0")]
    [TestCase("без цифр вообще", ExpectedResult = "без")]
    [TestCase("", ExpectedResult = "")]
    [TestCase("   ", ExpectedResult = "")]
    public string Версия_разбирается_из_первого_слова_с_цифры(string rawOutput)
    {
        return AgentCli.ParseVersion(rawOutput);
    }

    [Test]
    public void Минимальный_запрос_даёт_базовый_набор_флагов()
    {
        var request = new AgentRequest { Prompt = "привет" };

        var args = ClaudeAgentBackend.BuildArguments(request, mcpConfigPath: null);

        Assert.That(args, Is.EqualTo(new[]
        {
            "--print",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
            "--permission-mode", "dontAsk",
            "--tools", "",
            "--setting-sources", "",
            "--disable-slash-commands",
            "--system-prompt", AgentPrompt.System,
        }));
    }

    [Test]
    public void Mcp_добавляет_путь_к_конфигу_и_allowed_tools()
    {
        var mcp = new AgentMcpConfig("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory", "list_profiles"], []);
        var request = new AgentRequest { Prompt = "куда делось место", Mcp = mcp };

        var args = ClaudeAgentBackend.BuildArguments(request, @"C:\temp\mcp.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("--mcp-config"));
            Assert.That(args, Does.Contain(@"C:\temp\mcp.json"));
            Assert.That(args, Does.Contain("--strict-mcp-config"));
            Assert.That(args, Does.Contain("--allowed-tools"));
            Assert.That(args, Does.Contain("mcp__spacesnoop__scan_directory,mcp__spacesnoop__list_profiles"));
            Assert.That(args, Does.Not.Contain("--disallowed-tools"));
        }
    }

    [Test]
    public void Mcp_добавляет_disallowed_tools_для_запрещённых_инструментов()
    {
        var mcp = new AgentMcpConfig("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory"], ["sync_current", "open_sync"]);
        var request = new AgentRequest { Prompt = "куда делось место", Mcp = mcp };

        var args = ClaudeAgentBackend.BuildArguments(request, @"C:\temp\mcp.json");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("--disallowed-tools"));
            Assert.That(args, Does.Contain("mcp__spacesnoop__sync_current,mcp__spacesnoop__open_sync"));
        }
    }

    [Test]
    public void Пустой_deny_список_не_добавляет_disallowed_tools()
    {
        var mcp = new AgentMcpConfig("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory"], []);
        var request = new AgentRequest { Prompt = "тест", Mcp = mcp };

        var args = ClaudeAgentBackend.BuildArguments(request, @"C:\temp\mcp.json");

        Assert.That(args, Does.Not.Contain("--disallowed-tools"));
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("sonnet", ExpectedResult = true)]
    public bool Модель_добавляется_только_когда_задана(string model)
    {
        var request = new AgentRequest { Prompt = "тест", Model = model };
        var args = ClaudeAgentBackend.BuildArguments(request, mcpConfigPath: null);

        return args.Contains("--model") && args.Contains(model);
    }

    [TestCase("", ExpectedResult = false)]
    [TestCase("xhigh", ExpectedResult = true)]
    public bool Глубина_рассуждений_добавляется_только_когда_задана(string effort)
    {
        var request = new AgentRequest { Prompt = "тест", Effort = effort };
        var args = ClaudeAgentBackend.BuildArguments(request, mcpConfigPath: null);

        return args.Contains("--effort") && args.Contains(effort);
    }

    [Test]
    public void Продолжение_сессии_добавляется_через_resume()
    {
        var request = new AgentRequest { Prompt = "тест", ResumeSessionId = "session-42" };

        var args = ClaudeAgentBackend.BuildArguments(request, mcpConfigPath: null);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(args, Does.Contain("--resume"));
            Assert.That(args, Does.Contain("session-42"));
        }
    }

    [Test]
    public void Свой_системный_промпт_заменяет_дефолтный()
    {
        var request = new AgentRequest { Prompt = "тест", SystemPrompt = "Кастомный промпт" };

        var args = ClaudeAgentBackend.BuildArguments(request, mcpConfigPath: null);

        Assert.That(args, Does.Contain("Кастомный промпт"));
        Assert.That(args, Does.Not.Contain(AgentPrompt.System));
    }

    [Test]
    public void Инициализация_сессии_даёт_событие_Started()
    {
        const string line = """
                             {"type":"system","subtype":"init","session_id":"fd09a8e8-96f8-4c9c-9def-6924fbb8f82d","mcp_servers":[{"name":"spacesnoop","status":"failed"}]}
                             """;

        var result = ClaudeAgentBackend.ParseLine(line);

        Assert.That(result, Is.EqualTo(AgentEvent.Begin("fd09a8e8-96f8-4c9c-9def-6924fbb8f82d")));
    }

    [TestCase("""{"type":"system","subtype":"status","status":"requesting"}""")]
    [TestCase("""{"type":"rate_limit_event","rate_limit_info":{"status":"allowed_warning"}}""")]
    public void Статус_и_rate_limit_игнорируются(string line)
    {
        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [Test]
    public void Текстовая_дельта_даёт_событие_Text()
    {
        const string line = """
                             {"type":"stream_event","event":{"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"Красный, синий, зелёный."}}}
                             """;

        var result = ClaudeAgentBackend.ParseLine(line);

        Assert.That(result, Is.EqualTo(AgentEvent.Chunk("Красный, синий, зелёный.")));
    }

    [TestCase("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"..."}}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"abc"}}}""")]
    public void Thinking_и_signature_дельты_игнорируются(string line)
    {
        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [TestCase("""{"type":"stream_event","event":{"type":"message_start","message":{}}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"content_block_start","index":0,"content_block":{}}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"content_block_stop","index":0}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"message_delta","delta":{"stop_reason":"end_turn"}}}""")]
    [TestCase("""{"type":"stream_event","event":{"type":"message_stop"}}""")]
    public void Служебные_подтипы_stream_event_игнорируются(string line)
    {
        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [Test]
    public void Assistant_с_вызовом_инструмента_даёт_событие_ToolCall()
    {
        const string line = """
                             {"type":"assistant","message":{"content":[{"type":"tool_use","id":"toolu_1","name":"mcp__spacesnoop__scan_directory","input":{}}]}}
                             """;

        var result = ClaudeAgentBackend.ParseLine(line);

        Assert.That(result, Is.EqualTo(AgentEvent.Tool("mcp__spacesnoop__scan_directory", "{}")));
    }

    [Test]
    public void Assistant_с_только_текстом_не_дублирует_ответ()
    {
        const string line = """
                             {"type":"assistant","message":{"content":[{"type":"text","text":"Красный, синий, зелёный."}]}}
                             """;

        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [Test]
    public void Результат_инструмента_от_user_игнорируется()
    {
        const string line = """
                             {"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"toolu_1","content":"ok"}]}}
                             """;

        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [Test]
    public void Успешный_результат_даёт_событие_Completed()
    {
        const string line = """
                             {"is_error":false,"session_id":"fd09a8e8-96f8-4c9c-9def-6924fbb8f82d","total_cost_usd":0.00244900000000000002,"type":"result"}
                             """;

        var result = ClaudeAgentBackend.ParseLine(line);

        Assert.That(result, Is.EqualTo(AgentEvent.Done("fd09a8e8-96f8-4c9c-9def-6924fbb8f82d", 0.00244900000000000002)));
    }

    [Test]
    public void Результат_с_ошибкой_даёт_событие_Failed()
    {
        const string line = """
                             {"is_error":true,"session_id":"abc","result":"Ошибка авторизации","type":"result"}
                             """;

        var result = ClaudeAgentBackend.ParseLine(line);

        Assert.That(result, Is.EqualTo(AgentEvent.Fail("Ошибка авторизации")));
    }

    [TestCase("не json вообще")]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("[]")]
    [TestCase("true")]
    public void Испорченная_или_чужая_строка_не_бросает_исключение(string line)
    {
        Assert.That(ClaudeAgentBackend.ParseLine(line), Is.Null);
    }

    [Test]
    public void Неподключённые_mcp_серверы_находятся_по_статусу()
    {
        const string line = """
                             {"type":"system","subtype":"init","mcp_servers":[{"name":"spacesnoop","status":"failed"},{"name":"другой","status":"connected"}]}
                             """;

        var disconnected = ClaudeAgentBackend.ParseDisconnectedMcpServers(line);

        Assert.That(disconnected, Is.EqualTo(new[] { ("spacesnoop", "failed") }));
    }

    [Test]
    public void Все_серверы_подключены_список_пуст()
    {
        const string line = """
                             {"type":"system","subtype":"init","mcp_servers":[{"name":"spacesnoop","status":"connected"}]}
                             """;

        Assert.That(ClaudeAgentBackend.ParseDisconnectedMcpServers(line), Is.Empty);
    }

    [TestCase("""{"type":"system","subtype":"init"}""")]
    [TestCase("не json")]
    public void Строка_без_mcp_серверов_даёт_пустой_список(string line)
    {
        Assert.That(ClaudeAgentBackend.ParseDisconnectedMcpServers(line), Is.Empty);
    }

    [Test]
    public void Конфиг_MCP_содержит_адрес_и_токен()
    {
        var mcp = new AgentMcpConfig("spacesnoop", "http://127.0.0.1:7654/mcp", "secret-token", ["scan_directory"], []);

        var json = ClaudeAgentBackend.BuildMcpConfigJson(mcp);

        using var document = JsonDocument.Parse(json);
        var server = document.RootElement.GetProperty("mcpServers").GetProperty("spacesnoop");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(server.GetProperty("type").GetString(), Is.EqualTo("http"));
            Assert.That(server.GetProperty("url").GetString(), Is.EqualTo("http://127.0.0.1:7654/mcp"));
            Assert.That(server.GetProperty("headers").GetProperty("Authorization").GetString(), Is.EqualTo("Bearer secret-token"));
        }
    }

    [TestCase("Ошибка: заголовок Authorization: Bearer secret-token отклонён", "secret-token", ExpectedResult = "Ошибка: заголовок Authorization: Bearer <токен> отклонён")]
    [TestCase("обычная ошибка без токена", "secret-token", ExpectedResult = "обычная ошибка без токена")]
    [TestCase("secret-token secret-token", "secret-token", ExpectedResult = "<токен> <токен>")]
    [TestCase("текст как есть", "", ExpectedResult = "текст как есть")]
    [TestCase("текст как есть", null, ExpectedResult = "текст как есть")]
    public string Токен_вырезается_из_текста(string text, string? token)
    {
        return ClaudeAgentBackend.RedactToken(text, token);
    }
}
