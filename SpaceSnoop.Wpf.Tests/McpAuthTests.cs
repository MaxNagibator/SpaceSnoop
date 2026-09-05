using SpaceSnoop.Wpf.Mcp;

namespace SpaceSnoop.Wpf.Tests;

[TestFixture]
public class McpAuthTests
{
    private const string Token = "e7605ca560754c5c87756c415c02283b";

    [TestCase("Bearer e7605ca560754c5c87756c415c02283b")]
    [TestCase("bearer e7605ca560754c5c87756c415c02283b")]
    [TestCase("Bearer  e7605ca560754c5c87756c415c02283b ")]
    public void Верный_токен_в_заголовке_Authorization_пропускается(string authorization)
    {
        Assert.That(McpAuth.IsAuthorized(authorization, null, Token), Is.True);
    }

    [TestCase(null, null)]
    [TestCase("", "")]
    [TestCase("Bearer другой", null)]
    [TestCase("e7605ca560754c5c87756c415c02283b", null)]
    [TestCase(null, "другой")]
    public void Без_верного_токена_запрос_отклоняется(string? authorization, string? tokenHeader)
    {
        Assert.That(McpAuth.IsAuthorized(authorization, tokenHeader, Token), Is.False);
    }

    [Test]
    public void Отдельный_заголовок_с_токеном_пропускается()
    {
        Assert.That(McpAuth.IsAuthorized(null, Token, Token), Is.True);
    }

    [Test]
    public void Пустой_токен_не_пропускает_никого()
    {
        Assert.Multiple(() =>
        {
            Assert.That(McpAuth.IsAuthorized(null, null, string.Empty), Is.False);
            Assert.That(McpAuth.IsAuthorized("Bearer " + Token, null, string.Empty), Is.False);
        });
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("http://127.0.0.1:7654")]
    [TestCase("http://localhost:3000")]
    [TestCase("http://[::1]:8080")]
    public void Петлевой_или_отсутствующий_Origin_пропускается(string? origin)
    {
        Assert.That(McpAuth.IsOriginAllowed(origin), Is.True);
    }

    [TestCase("https://evil.example")]
    [TestCase("http://192.168.1.10")]
    [TestCase("не-адрес")]
    public void Чужой_Origin_отклоняется(string origin)
    {
        Assert.That(McpAuth.IsOriginAllowed(origin), Is.False);
    }
}
