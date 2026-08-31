using SpaceSnoop.Wpf.Mcp;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class McpConnectSnippetsTests
{
    private const string Url = "http://127.0.0.1:7777/mcp";

    private const string Token = "тестовый-токен-42";

    [TestCase(0, "\"mcpServers\"")]
    [TestCase(1, "claude mcp add")]
    [TestCase(2, "[mcp_servers.spacesnoop]")]
    [TestCase(3, "\"type\": \"remote\"")]
    [TestCase(4, "Транспорт: Streamable HTTP")]
    public void Каждый_формат_даёт_свою_форму(int formatIndex, string marker)
    {
        var snippet = McpConnectSnippets.For(formatIndex, Url, Token);

        Assert.That(snippet, Does.Contain(marker));
    }

    [TestCase(-1)]
    [TestCase(5)]
    [TestCase(int.MaxValue)]
    public void Индекс_вне_диапазона_даёт_JSON(int formatIndex)
    {
        var snippet = McpConnectSnippets.For(formatIndex, Url, Token);

        Assert.That(snippet, Is.EqualTo(McpConnectSnippets.For(0, Url, Token)));
    }

    [Test]
    public void Ни_один_формат_не_теряет_адрес_и_токен()
    {
        Assert.Multiple(() =>
        {
            for (var formatIndex = 0; formatIndex <= 4; formatIndex++)
            {
                var snippet = McpConnectSnippets.For(formatIndex, Url, Token);

                Assert.That(snippet, Does.Contain(Url), $"формат {formatIndex} без адреса");
                Assert.That(snippet, Does.Contain(Token), $"формат {formatIndex} без токена");
            }
        });
    }
}
